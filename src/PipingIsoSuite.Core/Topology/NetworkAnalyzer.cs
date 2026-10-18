namespace PipingIsoSuite.Core.Topology;

/// <summary>
/// Splits a piping graph into runs and classifies each connected network into one Header
/// and a tree of Branches.
/// <para>
/// A run continues "straight through" an element: 2-port elements pass flow through both ports;
/// on tees/crosses (3+ ports) the most opposite pair of connectors (ties broken by larger size)
/// is the through path and the remaining connectors start branches.
/// </para>
/// The header of a network is the run containing a preferred element (e.g. the user's pick),
/// otherwise the largest-diameter run, ties broken by greatest pipe length.
/// </summary>
public static class NetworkAnalyzer
{
    public static IReadOnlyList<PipeNetwork> Analyze(IEnumerable<PipeNode> nodes, IReadOnlySet<long>? preferredHeaderIds = null)
    {
        var byId = new Dictionary<long, PipeNode>();
        foreach (var n in nodes) byId.TryAdd(n.Id, n);

        var through = byId.Values.ToDictionary(n => n.Id, ThroughPorts);
        var throughNeighbors = new Dictionary<long, List<long>>();
        var branchEdges = new List<(long From, long To)>();

        foreach (var node in byId.Values.OrderBy(n => n.Id))
        {
            throughNeighbors[node.Id] = new List<long>();
            for (var i = 0; i < node.Ports.Count; i++)
            {
                if (node.Ports[i].ConnectedTo is not long other || !byId.TryGetValue(other, out var neighbor) || other == node.Id)
                    continue;
                var back = neighbor.Ports.FindIndex(p => p.ConnectedTo == node.Id);
                var isThrough = through[node.Id].Contains(i) && back >= 0 && through[other].Contains(back);
                if (isThrough) { if (!throughNeighbors[node.Id].Contains(other)) throughNeighbors[node.Id].Add(other); }
                else branchEdges.Add((node.Id, other));
            }
        }

        // Runs = connected components over through-edges, walked end to end.
        var runOf = new Dictionary<long, PipeRun>();
        var runs = new List<PipeRun>();
        foreach (var start in byId.Keys.OrderBy(id => id))
        {
            if (runOf.ContainsKey(start)) continue;
            var component = Collect(start, id => throughNeighbors[id]);
            var endpoint = component.Where(id => throughNeighbors[id].Count <= 1).DefaultIfEmpty(component.Min()).Min();
            var run = new PipeRun { Id = runs.Count + 1 };
            foreach (var id in Walk(endpoint, throughNeighbors)) { run.Nodes.Add(byId[id]); runOf[id] = run; }
            runs.Add(run);
        }

        var runAdj = runs.ToDictionary(r => r, _ => new List<PipeRun>());
        foreach (var (from, to) in branchEdges)
        {
            var a = runOf[from]; var b = runOf[to];
            if (a != b && !runAdj[a].Contains(b)) { runAdj[a].Add(b); runAdj[b].Add(a); }
        }

        // Networks = components over the run graph; BFS from the header builds the branch tree.
        var networks = new List<PipeNetwork>();
        var assigned = new HashSet<PipeRun>();
        foreach (var seed in runs)
        {
            if (assigned.Contains(seed)) continue;
            var members = Collect(seed, r => runAdj[r]);
            var header = members.FirstOrDefault(r => preferredHeaderIds != null && r.Nodes.Any(n => preferredHeaderIds.Contains(n.Id)))
                         ?? members.OrderByDescending(r => Math.Round(r.MaxDiameter, 6))
                                   .ThenByDescending(r => r.TotalLength)
                                   .ThenBy(r => r.Id).First();

            var network = new PipeNetwork { Id = networks.Count + 1, Header = header };
            header.Role = RunRole.Header;
            var queue = new Queue<PipeRun>();
            queue.Enqueue(header); assigned.Add(header);
            while (queue.Count > 0)
            {
                var run = queue.Dequeue();
                run.Network = network;
                network.Runs.Add(run);
                // Discover children in walk order along the parent so branch numbering follows the run.
                foreach (var node in run.Nodes)
                foreach (var port in node.Ports)
                {
                    if (port.ConnectedTo is not long other || !runOf.TryGetValue(other, out var child) || assigned.Contains(child)) continue;
                    if (!runAdj[run].Contains(child)) continue;
                    child.Role = RunRole.Branch; child.Parent = run; child.Depth = run.Depth + 1;
                    run.Children.Add(child); assigned.Add(child); queue.Enqueue(child);
                }
            }
            networks.Add(network);
        }
        return networks;
    }

    /// <summary>Indices of the ports that carry the straight-through path of a node.</summary>
    internal static HashSet<int> ThroughPorts(PipeNode node)
    {
        var connected = Enumerable.Range(0, node.Ports.Count).ToList();
        if (connected.Count <= 2) return connected.ToHashSet();

        (int A, int B) best = (-1, -1);
        (double Dot, double Size) bestScore = (double.MaxValue, double.MinValue);
        for (var i = 0; i < connected.Count; i++)
        for (var j = i + 1; j < connected.Count; j++)
        {
            var pi = node.Ports[i]; var pj = node.Ports[j];
            // Bucket the dot product so tiny modelling deviations don't beat a larger size.
            var dot = pi.Direction is Vec3 di && pj.Direction is Vec3 dj ? Math.Round(di.Normalize().Dot(dj.Normalize()) * 20) : 0;
            var size = pi.Diameter + pj.Diameter;
            if (dot < bestScore.Dot || (dot == bestScore.Dot && size > bestScore.Size))
            { best = (i, j); bestScore = (dot, size); }
        }
        return new HashSet<int> { best.A, best.B };
    }

    private static List<T> Collect<T>(T start, Func<T, IEnumerable<T>> next) where T : notnull
    {
        var seen = new HashSet<T> { start };
        var stack = new Stack<T>(); stack.Push(start);
        while (stack.Count > 0)
            foreach (var n in next(stack.Pop()))
                if (seen.Add(n)) stack.Push(n);
        return seen.ToList();
    }

    private static IEnumerable<long> Walk(long start, Dictionary<long, List<long>> neighbors)
    {
        long? prev = null; var current = start;
        var visited = new HashSet<long>();
        while (visited.Add(current))
        {
            yield return current;
            var next = neighbors[current].Where(n => n != prev && !visited.Contains(n)).Cast<long?>().FirstOrDefault();
            if (next is null) yield break;
            prev = current; current = next.Value;
        }
    }
}

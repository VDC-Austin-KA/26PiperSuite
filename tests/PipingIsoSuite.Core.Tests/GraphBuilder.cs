using PipingIsoSuite.Core.Topology;

namespace PipingIsoSuite.Core.Tests;

/// <summary>Builds PipeNode graphs. Diameters are in feet. Ports are wired with Connect, mutually.</summary>
internal sealed class GraphBuilder
{
    public static readonly Vec3 East = new(1, 0, 0), West = new(-1, 0, 0), North = new(0, 1, 0), South = new(0, -1, 0);

    private readonly List<Item> _items = new();
    private readonly List<(long A, int PortA, long B, int PortB)> _links = new();

    private sealed record Item(long Id, NodeKind Kind, string TypeName, double Length, (Vec3 Dir, double Dia)[] Ports, Dictionary<string, string> Attrs);

    /// <summary>Two-port pipe along <paramref name="axis"/>: port 0 faces -axis, port 1 faces +axis.</summary>
    public GraphBuilder Pipe(long id, double diameter = 0.5, double length = 10, Vec3? axis = null, string typeName = "Pipe")
    {
        var a = axis ?? East;
        return Add(id, NodeKind.Pipe, typeName, length, new[] { (Neg(a), diameter), (a, diameter) });
    }

    /// <summary>Three-port tee: ports 0 and 1 are the through path (West, East), port 2 is the branch (North).</summary>
    public GraphBuilder Tee(long id, double mainDiameter, double branchDiameter) =>
        Fitting(id, "Tee", (West, mainDiameter), (East, mainDiameter), (North, branchDiameter));

    /// <summary>Four-port cross: West/East and North/South.</summary>
    public GraphBuilder Cross(long id, double mainDiameter, double branchDiameter) =>
        Fitting(id, "Cross", (West, mainDiameter), (East, mainDiameter), (North, branchDiameter), (South, branchDiameter));

    public GraphBuilder Fitting(long id, string typeName, params (Vec3 Dir, double Dia)[] ports) =>
        Add(id, NodeKind.Fitting, typeName, 0, ports);

    public GraphBuilder Connect(long a, int portA, long b, int portB)
    {
        _links.Add((a, portA, b, portB));
        return this;
    }

    /// <summary>Sets an attribute on one element. Defaults are System "CHW" and Level "L1".</summary>
    public GraphBuilder With(long id, string key, string value)
    {
        _items.Single(i => i.Id == id).Attrs[key] = value;
        return this;
    }

    public GraphBuilder WithAll(string key, string value)
    {
        foreach (var item in _items) item.Attrs[key] = value;
        return this;
    }

    public List<PipeNode> Build()
    {
        var peer = new Dictionary<(long, int), (long, int)>();
        foreach (var (a, pa, b, pb) in _links)
        {
            peer[(a, pa)] = (b, pb);
            peer[(b, pb)] = (a, pa);
        }

        return _items.Select(item => new PipeNode
        {
            Id = item.Id,
            Kind = item.Kind,
            TypeName = item.TypeName,
            Length = item.Length,
            Ports = item.Ports.Select((p, i) => new PipePort
            {
                Diameter = p.Dia,
                Direction = p.Dir,
                ConnectedTo = peer.TryGetValue((item.Id, i), out var other) ? (long?)other.Item1 : null,
            }).ToList(),
            Attributes = new Dictionary<string, string>(item.Attrs, StringComparer.OrdinalIgnoreCase),
        }).ToList();
    }

    /// <summary>A run built directly from nodes in the given order, for signature tests.</summary>
    public static PipeRun RunOf(params PipeNode[] nodes)
    {
        var run = new PipeRun();
        run.Nodes.AddRange(nodes);
        return run;
    }

    /// <summary>Pipe <c>first</c> - tee <c>first+1</c> - pipe <c>first+2</c> along X (0.5 ft), with a 0.25 ft branch pipe <c>first+3</c> off the tee's North port.</summary>
    public static GraphBuilder TeeHeaderWithBranch(long first = 1) =>
        new GraphBuilder()
            .Pipe(first, 0.5)
            .Tee(first + 1, 0.5, 0.25)
            .Pipe(first + 2, 0.5)
            .Pipe(first + 3, 0.25, axis: North)
            .Connect(first, 1, first + 1, 0)
            .Connect(first + 1, 1, first + 2, 0)
            .Connect(first + 1, 2, first + 3, 0);

    private GraphBuilder Add(long id, NodeKind kind, string typeName, double length, (Vec3 Dir, double Dia)[] ports)
    {
        var attrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [Attr.System] = "CHW",
            [Attr.Level] = "L1",
        };
        _items.Add(new Item(id, kind, typeName, length, ports, attrs));
        return this;
    }

    private static Vec3 Neg(Vec3 v) => new(-v.X, -v.Y, -v.Z);
}

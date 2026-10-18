using PipingIsoSuite.Core.Config;
using PipingIsoSuite.Core.Naming;
using PipingIsoSuite.Core.Topology;

namespace PipingIsoSuite.Core.LineNumbering;

public sealed record LineNumberAssignment(PipeRun Run, string LineNumber, bool IsNew);

/// <summary>
/// Plans BC_LINENUMBER values: headers take the next free sequence for their
/// prefix/system/level/zone group; branches derive from their parent run's number.
/// </summary>
public static class LineNumberPlanner
{
    public static IReadOnlyList<LineNumberAssignment> Plan(
        IEnumerable<PipeNetwork> networks, LineNumberScheme scheme, IEnumerable<string> numbersInUseOutsideScope)
    {
        var networkList = networks.ToList();
        var used = new HashSet<string>(numbersInUseOutsideScope.Where(s => s.Length > 0), StringComparer.OrdinalIgnoreCase);

        // Reserve kept numbers first so new numbers never collide with ones later in the list.
        var keep = new Dictionary<PipeRun, string>();
        if (!scheme.OverwriteExisting)
            foreach (var run in networkList.SelectMany(n => n.Runs))
                if (run.ExistingLineNumber is { } existing && used.Add(existing))
                    keep[run] = existing;

        var seqCounters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var branchCounters = new Dictionary<PipeRun, int>();
        var assigned = new Dictionary<PipeRun, string>();
        var result = new List<LineNumberAssignment>();

        foreach (var run in networkList.SelectMany(n => n.Runs))
        {
            if (keep.TryGetValue(run, out var kept))
            {
                assigned[run] = kept;
                result.Add(new(run, kept, false));
                continue;
            }

            var isBranch = run.Parent is not null && assigned.ContainsKey(run.Parent);
            var pattern = isBranch ? scheme.BranchPattern : scheme.HeaderPattern;
            var tokens = Tokens(run, scheme);
            if (isBranch) tokens["Header"] = assigned[run.Parent!];

            var usesBranchSeq = isBranch && TemplateFormatter.UsesToken(pattern, "BranchSeq");
            var usesSeq = TemplateFormatter.UsesToken(pattern, "Seq");
            var groupKey = TemplateFormatter.Format(pattern, new Dictionary<string, object?>(tokens) { ["Seq"] = "#", ["BranchSeq"] = "#" });

            string candidate;
            if (usesBranchSeq)
            {
                var n = branchCounters.GetValueOrDefault(run.Parent!, 0);
                do { n++; tokens["BranchSeq"] = n; candidate = TemplateFormatter.Format(pattern, tokens); } while (!used.Add(candidate));
                branchCounters[run.Parent!] = n;
            }
            else if (usesSeq)
            {
                var n = seqCounters.GetValueOrDefault(groupKey, scheme.StartAt - 1);
                do { n++; tokens["Seq"] = n; candidate = TemplateFormatter.Format(pattern, tokens); } while (!used.Add(candidate));
                seqCounters[groupKey] = n;
            }
            else
            {
                // No sequence token: disambiguate collisions with a numeric suffix.
                var baseName = TemplateFormatter.Format(pattern, tokens);
                candidate = baseName;
                for (var n = 2; !used.Add(candidate); n++) candidate = $"{baseName}-{n}";
            }

            assigned[run] = candidate;
            result.Add(new(run, candidate, true));
        }
        return result;
    }

    internal static Dictionary<string, object?> Tokens(PipeRun run, LineNumberScheme scheme)
    {
        var rep = run.Representative;
        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Prefix"] = scheme.Prefix,
            ["System"] = Code(scheme.SystemCodes, rep.Get(Attr.System)),
            ["SystemType"] = Code(scheme.SystemCodes, rep.Get(Attr.SystemType)),
            ["Level"] = Code(scheme.LevelCodes, rep.Get(Attr.Level)),
            ["Zone"] = Code(scheme.ZoneCodes, rep.Get(Attr.Zone)),
            ["Role"] = run.Role == RunRole.Header ? "H" : "B",
        };
    }

    // Case-insensitive even after JSON deserialisation replaced the dictionary comparer.
    private static string Code(Dictionary<string, string> map, string value) =>
        map.FirstOrDefault(kv => kv.Key.Equals(value, StringComparison.OrdinalIgnoreCase)).Value ?? value.Replace(" ", "");
}

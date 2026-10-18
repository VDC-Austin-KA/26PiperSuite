using PipingIsoSuite.Core.Topology;

namespace PipingIsoSuite.Core.LineNumbering;

public enum IssueKind { Unassigned, Mixed, Duplicate }

public sealed record ValidationIssue(IssueKind Kind, string? LineNumber, IReadOnlyList<long> ElementIds, string Message);

public static class LineNumberValidator
{
    /// <summary>
    /// Flags runs with unassigned elements, runs carrying several numbers, and numbers shared by
    /// more than one run (which breaks one-iso-per-line).
    /// </summary>
    public static IReadOnlyList<ValidationIssue> Validate(IEnumerable<PipeNetwork> networks)
    {
        var issues = new List<ValidationIssue>();
        var runsByNumber = new Dictionary<string, List<PipeRun>>(StringComparer.OrdinalIgnoreCase);

        foreach (var run in networks.SelectMany(n => n.Runs))
        {
            var label = $"{run.Role} run #{run.Id} ({run.Network.SystemName})";
            var empty = run.Nodes.Where(n => n.Get(Topology.Attr.LineNumber).Length == 0).Select(n => n.Id).ToList();
            if (empty.Count > 0)
                issues.Add(new(IssueKind.Unassigned, null, empty, $"{label}: {empty.Count} element(s) without a line number"));

            var numbers = run.Nodes.Select(n => n.Get(Topology.Attr.LineNumber)).Where(s => s.Length > 0)
                             .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (numbers.Count > 1)
                issues.Add(new(IssueKind.Mixed, string.Join(", ", numbers), run.ElementIds.ToList(),
                    $"{label}: mixed line numbers {string.Join(", ", numbers)}"));

            foreach (var number in numbers)
            {
                if (!runsByNumber.TryGetValue(number, out var list)) runsByNumber[number] = list = new();
                list.Add(run);
            }
        }

        foreach (var (number, runs) in runsByNumber.Where(kv => kv.Value.Count > 1))
            issues.Add(new(IssueKind.Duplicate, number, runs.SelectMany(r => r.ElementIds).ToList(),
                $"Line number {number} is used by {runs.Count} separate runs"));

        return issues;
    }
}

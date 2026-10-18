using System.Security.Cryptography;
using System.Text;

namespace PipingIsoSuite.Core.Topology;

/// <summary>
/// Position-independent fingerprint of a run and its branches (element kinds, types, sizes), used to
/// find repeating/identical process loops for bulk parameter propagation.
/// </summary>
public static class LoopSignature
{
    public static string Compute(PipeRun run) => Hash(Canonical(run));

    public static IEnumerable<IGrouping<string, PipeRun>> GroupIdentical(IEnumerable<PipeRun> runs) =>
        runs.GroupBy(Compute).Where(g => g.Count() > 1);

    /// <summary>
    /// Pairs each run in <paramref name="source"/>'s subtree with its counterpart in an identical
    /// <paramref name="target"/> subtree. Returns empty when the loops are not identical.
    /// </summary>
    public static IReadOnlyList<(PipeRun Source, PipeRun Target)> Pair(PipeRun source, PipeRun target)
    {
        if (Canonical(source) != Canonical(target)) return Array.Empty<(PipeRun, PipeRun)>();
        var pairs = new List<(PipeRun, PipeRun)> { (source, target) };
        var s = source.Children.OrderBy(Canonical, StringComparer.Ordinal).ToList();
        var t = target.Children.OrderBy(Canonical, StringComparer.Ordinal).ToList();
        for (var i = 0; i < s.Count; i++) pairs.AddRange(Pair(s[i], t[i]));
        return pairs;
    }

    internal static string Canonical(PipeRun run)
    {
        var tokens = run.Nodes.Select(n => $"{n.Kind}:{n.TypeName}:{Math.Round(n.Diameter, 4)}").ToList();
        var forward = string.Join(">", tokens);
        tokens.Reverse();
        var reverse = string.Join(">", tokens);
        var path = string.CompareOrdinal(forward, reverse) <= 0 ? forward : reverse;
        var children = run.Children.Select(Canonical).OrderBy(s => s, StringComparer.Ordinal);
        return $"{path}[{string.Join("|", children)}]";
    }

    private static string Hash(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)))[..12];
}

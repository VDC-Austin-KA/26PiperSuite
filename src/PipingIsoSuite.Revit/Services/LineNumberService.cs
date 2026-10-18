using Autodesk.Revit.DB;
using PipingIsoSuite.Core.Config;
using PipingIsoSuite.Core.LineNumbering;
using PipingIsoSuite.Core.Topology;

namespace PipingIsoSuite.Revit.Services;

public static class LineNumberService
{
    /// <summary>Plans and writes BC_LINENUMBER for <paramref name="runs"/> (or every run in scope).</summary>
    public static string Assign(Document doc, GraphResult graph, SuitePresets presets, IReadOnlySet<long>? runKeys)
    {
        var plan = LineNumberPlanner.Plan(graph.Networks, presets.LineNumbering, graph.NumbersOutsideScope)
            .Where(a => runKeys is null || a.Run.Nodes.Any(n => runKeys.Contains(n.Id)))
            .ToList();

        var toWrite = plan.SelectMany(a => a.Run.Nodes.Where(n => n.Get(Attr.LineNumber) != a.LineNumber)
                                                      .Select(n => (Node: n, a.LineNumber))).ToList();
        if (toWrite.Count == 0) return "All selected runs already carry their line numbers.";

        var guard = WorksharingGuard.Reserve(doc, toWrite.Select(w => new ElementId(w.Node.Id)));
        using var tx = new Transaction(doc, "Assign line numbers");
        tx.Start();
        ParameterService.EnsureTextParameter(doc, presets.LineNumberParameter);

        int written = 0, missing = 0;
        foreach (var (node, number) in toWrite.Where(w => guard.IsEditable(w.Node.Id)))
        {
            var p = doc.GetElement(new ElementId(node.Id))?.LookupParameter(presets.LineNumberParameter);
            if (p is null || p.IsReadOnly) { missing++; continue; }
            p.Set(number);
            node.Attributes[Attr.LineNumber] = number;
            written++;
        }
        tx.Commit();

        var lines = new List<string>
        {
            $"Numbered {plan.Count(a => a.IsNew)} new run(s), kept {plan.Count(a => !a.IsNew)}; wrote {written} element(s).",
        };
        if (missing > 0) lines.Add($"{missing} element(s) have no writable '{presets.LineNumberParameter}' parameter.");
        lines.AddRange(guard.Messages);
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Copies the configured parameters from <paramref name="source"/> to every identical loop in
    /// scope, run by run (header to header, branch to matching branch).
    /// </summary>
    public static string PropagateToIdenticalLoops(Document doc, GraphResult graph, SuitePresets presets, PipeRun source)
    {
        var signature = LoopSignature.Compute(source);
        var targets = graph.Runs.Where(r => r != source && LoopSignature.Compute(r) == signature).ToList();
        if (targets.Count == 0) return "No identical loops found in scope.";

        var pairs = targets.SelectMany(t => LoopSignature.Pair(source, t)).ToList();
        var targetIds = pairs.SelectMany(p => p.Target.ElementIds).Select(i => new ElementId(i));

        var guard = WorksharingGuard.Reserve(doc, targetIds);
        using var tx = new Transaction(doc, "Propagate loop parameters");
        tx.Start();
        var written = 0;
        foreach (var (from, to) in pairs)
        {
            var sourceElement = doc.GetElement(new ElementId(from.Representative.Id));
            // Never copy line numbers: identical loops are still separate lines.
            foreach (var name in presets.PropagateParameters.Where(n => n != presets.LineNumberParameter))
            {
                if (sourceElement?.LookupParameter(name) is not { HasValue: true } value) continue;
                foreach (var id in to.ElementIds.Where(guard.IsEditable))
                    if (ParameterService.CopyValue(value, doc.GetElement(new ElementId(id))?.LookupParameter(name)))
                        written++;
            }
        }
        tx.Commit();
        return string.Join(Environment.NewLine,
            new[] { $"Propagated to {targets.Count} identical loop(s): {written} value(s) written." }.Concat(guard.Messages));
    }
}

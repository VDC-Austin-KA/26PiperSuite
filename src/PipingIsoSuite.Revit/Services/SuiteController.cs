using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using PipingIsoSuite.Core.Config;
using PipingIsoSuite.Core.Export;
using PipingIsoSuite.Core.LineNumbering;
using PipingIsoSuite.Core.Topology;

namespace PipingIsoSuite.Revit.Services;

public sealed record ViewInfo(long Id, string Name);
public sealed record SheetInfo(long Id, string Number, string Name);

/// <summary>Plain-data picture of the model for the dashboard (safe to use off the API context).</summary>
public sealed record Snapshot(
    string DocumentTitle,
    GraphResult Graph,
    IReadOnlyDictionary<string, ViewInfo> IsoViewsByFilter,
    IReadOnlyDictionary<long, SheetInfo> SheetByView,
    IReadOnlyList<ValidationIssue> Issues)
{
    public ViewInfo? ViewFor(string? lineNumber) =>
        lineNumber is null ? null : IsoViewsByFilter.GetValueOrDefault(IsoViewService.FilterName(lineNumber));
}

/// <summary>
/// Entry points shared by ribbon commands and the dashboard. Every method must run in a Revit API
/// context; each re-traces the model so it acts on current data, selecting runs by element id keys.
/// </summary>
public static class SuiteController
{
    public static Snapshot Analyze(UIDocument uidoc, AnalysisScope scope, SuitePresets presets)
    {
        var doc = uidoc.Document;
        var graph = PipeGraphBuilder.Build(uidoc, scope, presets);

        var byFilter = new Dictionary<string, ViewInfo>(StringComparer.OrdinalIgnoreCase);
        var filterNames = new FilteredElementCollector(doc).OfClass(typeof(ParameterFilterElement))
            .Where(f => f.Name.StartsWith(IsoViewService.FilterPrefix, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(f => f.Id, f => f.Name);
        foreach (var view in new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>().Where(v => !v.IsTemplate))
            foreach (var fid in view.GetFilters())
                if (filterNames.TryGetValue(fid, out var name)) byFilter.TryAdd(name, new ViewInfo(view.Id.Value, view.Name));

        var sheetByView = new Dictionary<long, SheetInfo>();
        foreach (var vp in new FilteredElementCollector(doc).OfClass(typeof(Viewport)).Cast<Viewport>())
            if (doc.GetElement(vp.SheetId) is ViewSheet s)
                sheetByView.TryAdd(vp.ViewId.Value, new SheetInfo(s.Id.Value, s.SheetNumber, s.Name));

        return new Snapshot(doc.Title, graph, byFilter, sheetByView, LineNumberValidator.Validate(graph.Networks));
    }

    public static string AssignLineNumbers(UIDocument uidoc, AnalysisScope scope, SuitePresets presets, IReadOnlySet<long>? runKeys) =>
        LineNumberService.Assign(uidoc.Document, PipeGraphBuilder.Build(uidoc, scope, presets), presets, runKeys);

    public static string CreateIsoViews(UIDocument uidoc, AnalysisScope scope, SuitePresets presets, IReadOnlySet<long>? runKeys) =>
        IsoViewService.CreateOrUpdate(uidoc.Document, PipeGraphBuilder.Build(uidoc, scope, presets), presets, runKeys);

    /// <summary>Puts the iso views of the given runs (all in scope when null) on sheets, in line-number order.</summary>
    public static string CreateSheets(UIDocument uidoc, AnalysisScope scope, SuitePresets presets, IReadOnlySet<long>? runKeys)
    {
        var doc = uidoc.Document;
        var snapshot = Analyze(uidoc, scope, presets);
        var views = snapshot.Graph.Runs
            .Where(r => runKeys is null || r.Nodes.Any(n => runKeys.Contains(n.Id)))
            .Select(r => (Run: r, Info: snapshot.ViewFor(r.ExistingLineNumber)))
            .Where(x => x.Info is not null)
            .DistinctBy(x => x.Info!.Id)
            .OrderBy(x => x.Run.ExistingLineNumber, StringComparer.OrdinalIgnoreCase)
            .Select(x => ((View)doc.GetElement(new ElementId(x.Info!.Id)), x.Run.ExistingLineNumber!, x.Run.Network.SystemName))
            .ToList();
        return views.Count == 0 ? "No iso views found for the selected runs - create views first." : SheetService.PlaceOnSheets(doc, views, presets);
    }

    public static string Propagate(UIDocument uidoc, AnalysisScope scope, SuitePresets presets, long sourceElementId)
    {
        var graph = PipeGraphBuilder.Build(uidoc, scope, presets);
        var source = graph.Runs.FirstOrDefault(r => r.Nodes.Any(n => n.Id == sourceElementId));
        return source is null ? "Source run is not in scope." : LineNumberService.PropagateToIdenticalLoops(uidoc.Document, graph, presets, source);
    }

    public static string ExportEzIso(UIDocument uidoc, AnalysisScope scope, SuitePresets presets, string csvPath)
    {
        var snapshot = Analyze(uidoc, scope, presets);
        var records = snapshot.Graph.Runs
            .Where(r => r.ExistingLineNumber is not null)
            .GroupBy(r => r.ExistingLineNumber!, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var run = g.First();
                var rep = run.Representative;
                var view = snapshot.ViewFor(g.Key);
                var sheet = view is null ? null : snapshot.SheetByView.GetValueOrDefault(view.Id);
                return new IsoRecord(
                    g.Key, run.Role.ToString(), run.Parent?.ExistingLineNumber ?? "",
                    rep.Get(Attr.System), rep.Get(Attr.Level), rep.Get(Attr.Zone),
                    g.Max(r => r.MaxDiameter) * 304.8, g.Sum(r => r.TotalLength) * 0.3048,
                    g.Sum(r => r.Nodes.Count), LoopSignature.Compute(run),
                    view?.Name ?? "", sheet?.Number ?? "",
                    g.SelectMany(r => r.ElementIds).ToList());
            }).ToList();

        EzIsoExporter.WriteCsv(csvPath, records);
        var jsonPath = Path.ChangeExtension(csvPath, ".json");
        EzIsoExporter.WriteJson(jsonPath, uidoc.Document.Title, records);
        var unnumbered = snapshot.Graph.Runs.Count(r => r.ExistingLineNumber is null);
        return $"Exported {records.Count} iso group(s) to {csvPath} and {Path.GetFileName(jsonPath)}." +
               (unnumbered > 0 ? $" {unnumbered} unnumbered run(s) were left out." : "");
    }

    /// <summary>Re-applies the view naming rule to the given iso views.</summary>
    public static string RenameViews(UIDocument uidoc, AnalysisScope scope, SuitePresets presets, IReadOnlyCollection<long> viewIds)
    {
        var doc = uidoc.Document;
        var snapshot = Analyze(uidoc, scope, presets);
        var taken = new FilteredElementCollector(doc).OfClass(typeof(View)).Where(v => !viewIds.Contains(v.Id.Value))
            .Select(v => v.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var guard = WorksharingGuard.Reserve(doc, viewIds.Select(i => new ElementId(i)));
        using var tx = new Transaction(doc, "Rename iso views");
        tx.Start();
        var renamed = 0;
        foreach (var run in snapshot.Graph.Runs.Where(r => r.ExistingLineNumber is not null).DistinctBy(r => r.ExistingLineNumber))
        {
            if (snapshot.ViewFor(run.ExistingLineNumber) is not { } info || !viewIds.Contains(info.Id) || !guard.IsEditable(info.Id)) continue;
            var name = Core.Naming.TemplateFormatter.SanitizeRevitName(
                Core.Naming.TemplateFormatter.Format(presets.Naming.ViewName, IsoViewService.Tokens(run.ExistingLineNumber!, run)));
            doc.GetElement(new ElementId(info.Id)).Name = IsoViewService.Unique(taken, name);
            renamed++;
        }
        tx.Commit();
        return string.Join(Environment.NewLine, new[] { $"Renamed {renamed} view(s)." }.Concat(guard.Messages));
    }

    public static string RenumberSheets(UIDocument uidoc, SuitePresets presets, IReadOnlyList<long> sheetIds)
    {
        var doc = uidoc.Document;
        var sheets = sheetIds.Select(i => doc.GetElement(new ElementId(i))).OfType<ViewSheet>().ToList();
        return sheets.Count == 0 ? "No sheets selected." : SheetService.Renumber(doc, sheets, presets);
    }

    public static string Delete(UIDocument uidoc, IReadOnlyCollection<long> elementIds)
    {
        var doc = uidoc.Document;
        var guard = WorksharingGuard.Reserve(doc, elementIds.Select(i => new ElementId(i)));
        using var tx = new Transaction(doc, "Delete iso views/sheets");
        tx.Start();
        var ids = elementIds.Where(guard.IsEditable).Select(i => new ElementId(i)).ToList();
        var deleted = ids.Count == 0 ? 0 : doc.Delete(ids).Count;
        tx.Commit();
        return string.Join(Environment.NewLine, new[] { $"Deleted {ids.Count} item(s) ({deleted} element(s) incl. dependents)." }.Concat(guard.Messages));
    }

    public static void Select(UIDocument uidoc, IEnumerable<long> ids)
    {
        var list = ids.Select(i => new ElementId(i)).Where(i => uidoc.Document.GetElement(i) is not null).ToList();
        uidoc.Selection.SetElementIds(list);
        if (list.Count > 0) uidoc.ShowElements(list);
    }
}

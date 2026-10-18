using Autodesk.Revit.DB;
using PipingIsoSuite.Core.Config;
using PipingIsoSuite.Core.Layout;
using PipingIsoSuite.Core.Naming;
using PipingIsoSuite.Core.Topology;

namespace PipingIsoSuite.Revit.Services;

/// <summary>
/// One isometric per line number: a 3D view with a section box tight around the line and a
/// "PIS_LN_&lt;number&gt;" filter that hides every other line. The filter also links the view back
/// to its line, so lookups survive view renames.
/// </summary>
public static class IsoViewService
{
    public const string FilterPrefix = "PIS_LN_";

    public static string FilterName(string lineNumber) => TemplateFormatter.SanitizeRevitName(FilterPrefix + lineNumber);

    public static string CreateOrUpdate(Document doc, GraphResult graph, SuitePresets presets, IReadOnlySet<long>? runKeys)
    {
        var settings = presets.IsoViews;
        var groups = graph.Runs
            .Where(r => runKeys is null || r.Nodes.Any(n => runKeys.Contains(n.Id)))
            .Select(r => (Run: r, Number: r.ExistingLineNumber))
            .Where(g => g.Number is not null)
            .GroupBy(g => g.Number!, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (groups.Count == 0) return "No numbered runs in scope - assign line numbers first.";

        var paramId = ParameterService.FindParameterId(doc, presets.LineNumberParameter);
        if (paramId == ElementId.InvalidElementId) return $"Parameter '{presets.LineNumberParameter}' is not in this project.";

        var viewType = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
            .Where(t => t.ViewFamily == ViewFamily.ThreeDimensional)
            .OrderBy(t => t.Name == settings.ViewFamilyType ? 0 : 1).FirstOrDefault();
        if (viewType is null) return "No 3D view family type found.";

        var template = settings.ViewTemplate.Length == 0 ? null
            : new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>()
                .FirstOrDefault(v => v.IsTemplate && v.Name == settings.ViewTemplate);

        var views3d = new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>().Where(v => !v.IsTemplate).ToList();
        var viewNames = new FilteredElementCollector(doc).OfClass(typeof(View)).Select(v => v.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var filters = new FilteredElementCollector(doc).OfClass(typeof(ParameterFilterElement))
            .Cast<ParameterFilterElement>().ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase);
        var categories = PipeGraphBuilder.PipeCategories.Select(c => new ElementId(c))
            .Where(c => ParameterFilterUtilities.GetFilterableParametersInCommon(doc, new[] { c }).Contains(paramId)).ToList();
        if (categories.Count == 0) return $"'{presets.LineNumberParameter}' is not filterable on pipe categories.";
        var (forward, up) = IsoMath.Directions(settings.Orientation);

        // Existing isos for these lines get re-cropped, so reserve them first (new views need no checkout).
        var existingIds = filters.Where(f => groups.Any(g => FilterName(g.Key).Equals(f.Key, StringComparison.OrdinalIgnoreCase)))
            .SelectMany(f => views3d.Where(v => v.GetFilters().Contains(f.Value.Id))).Select(v => v.Id);
        var guard = WorksharingGuard.Reserve(doc, existingIds);

        int created = 0, updated = 0;
        var notes = new List<string>(guard.Messages);
        using var tx = new Transaction(doc, "Create isometric views");
        tx.Start();
        foreach (var group in groups)
        {
            var number = group.Key;
            var runs = group.Select(g => g.Run).ToList();
            var box = BoundsOf(doc, runs.SelectMany(r => r.ElementIds), settings.SectionBoxPaddingMm);
            if (box is null) { notes.Add($"{number}: no geometry."); continue; }

            var filterName = FilterName(number);
            if (!filters.TryGetValue(filterName, out var filter))
            {
                var rule = ParameterFilterRuleFactory.CreateNotEqualsRule(paramId, number);
                filter = ParameterFilterElement.Create(doc, filterName, categories, new ElementParameterFilter(rule));
                filters[filterName] = filter;
            }

            var view = settings.UpdateExisting ? views3d.FirstOrDefault(v => v.GetFilters().Contains(filter.Id)) : null;
            if (view is not null && !guard.IsEditable(view.Id.Value)) { notes.Add($"{number}: iso view is owned by another user, skipped."); continue; }
            if (view is null)
            {
                view = View3D.CreateIsometric(doc, viewType.Id);
                view.Name = Unique(viewNames, TemplateFormatter.SanitizeRevitName(
                    TemplateFormatter.Format(presets.Naming.ViewName, Tokens(number, runs[0]))));
                if (settings.Scale > 0) view.Scale = settings.Scale;
                ApplyTemplate(view, template);
                views3d.Add(view);
                created++;
            }
            else updated++;

            if (!view.GetFilters().Contains(filter.Id)) view.AddFilter(filter.Id);
            view.SetFilterVisibility(filter.Id, false);

            if (view.IsLocked) view.Unlock();
            view.IsSectionBoxActive = true;
            view.SetSectionBox(box);
            var center = (box.Min + box.Max) / 2;
            var eye = center - ToXyz(forward) * (box.Max - box.Min).GetLength() * 2;
            view.SetOrientation(new ViewOrientation3D(eye, ToXyz(up), ToXyz(forward)));
            if (settings.LockOrientation) view.SaveOrientationAndLock();
        }
        tx.Commit();

        notes.Insert(0, $"Isometric views: {created} created, {updated} updated.");
        return string.Join(Environment.NewLine, notes);
    }

    internal static Dictionary<string, object?> Tokens(string number, PipeRun run) => new()
    {
        ["LineNumber"] = number,
        ["System"] = run.Representative.Get(Attr.System),
        ["Level"] = run.Representative.Get(Attr.Level),
        ["Role"] = run.Role.ToString(),
        ["Header"] = run.Network.Header.ExistingLineNumber,
    };

    /// <summary>Assigns the template; if it controls V/G filters it is applied once instead so the line filter survives.</summary>
    private static void ApplyTemplate(View view, View? template)
    {
        if (template is null) return;
        var filtersId = new ElementId(BuiltInParameter.VIS_GRAPHICS_FILTERS);
        if (template.GetNonControlledTemplateParameterIds().Contains(filtersId)) view.ViewTemplateId = template.Id;
        else view.ApplyViewTemplateParameters(template);
    }

    private static BoundingBoxXYZ? BoundsOf(Document doc, IEnumerable<long> ids, double paddingMm)
    {
        XYZ? min = null, max = null;
        foreach (var id in ids)
        {
            if (doc.GetElement(new ElementId(id))?.get_BoundingBox(null) is not { } bb) continue;
            min = min is null ? bb.Min : new XYZ(Math.Min(min.X, bb.Min.X), Math.Min(min.Y, bb.Min.Y), Math.Min(min.Z, bb.Min.Z));
            max = max is null ? bb.Max : new XYZ(Math.Max(max.X, bb.Max.X), Math.Max(max.Y, bb.Max.Y), Math.Max(max.Z, bb.Max.Z));
        }
        if (min is null || max is null) return null;
        var pad = UnitUtils.ConvertToInternalUnits(paddingMm, UnitTypeId.Millimeters);
        var p = new XYZ(pad, pad, pad);
        return new BoundingBoxXYZ { Min = min - p, Max = max + p };
    }

    internal static string Unique(HashSet<string> taken, string name)
    {
        var candidate = name;
        for (var i = 2; !taken.Add(candidate); i++) candidate = $"{name} ({i})";
        return candidate;
    }

    private static XYZ ToXyz(Vec3 v) => new(v.X, v.Y, v.Z);
}

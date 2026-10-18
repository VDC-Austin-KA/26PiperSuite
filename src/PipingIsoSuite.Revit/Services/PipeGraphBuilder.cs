using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using PipingIsoSuite.Core.Config;
using PipingIsoSuite.Core.Topology;

namespace PipingIsoSuite.Revit.Services;

public enum AnalysisScope { EntireModel, ActiveView, ConnectedToSelection }

/// <summary>Result of tracing the model: networks in scope plus numbers used elsewhere.</summary>
public sealed record GraphResult(IReadOnlyList<PipeNetwork> Networks, IReadOnlyList<string> NumbersOutsideScope)
{
    public IEnumerable<PipeRun> Runs => Networks.SelectMany(n => n.Runs);
}

/// <summary>Reads pipes, fittings and accessories and their physical connectors into a Core graph.</summary>
public static class PipeGraphBuilder
{
    public static readonly BuiltInCategory[] PipeCategories =
    {
        BuiltInCategory.OST_PipeCurves, BuiltInCategory.OST_PipeFitting, BuiltInCategory.OST_PipeAccessory,
    };

    public static GraphResult Build(UIDocument uidoc, AnalysisScope scope, SuitePresets presets)
    {
        var doc = uidoc.Document;
        var all = Collect(doc, null);
        var candidates = scope == AnalysisScope.ActiveView ? Collect(doc, uidoc.ActiveView.Id) : all;
        var ids = candidates.Select(e => e.Id.Value).ToHashSet();

        var nodes = candidates.Select(e => ToNode(doc, e, ids, presets)).ToList();
        var selected = scope == AnalysisScope.ConnectedToSelection
            ? uidoc.Selection.GetElementIds().Select(i => i.Value).ToHashSet()
            : null;

        var networks = NetworkAnalyzer.Analyze(nodes, selected).ToList();
        if (selected is not null)
            networks = networks.Where(n => n.Runs.Any(r => r.Nodes.Any(x => selected.Contains(x.Id)))).ToList();

        var inScope = networks.SelectMany(n => n.Runs).SelectMany(r => r.ElementIds).ToHashSet();
        var outside = all.Where(e => !inScope.Contains(e.Id.Value))
                         .Select(e => e.LookupParameter(presets.LineNumberParameter)?.AsString() ?? "")
                         .Where(s => s.Length > 0).Distinct().ToList();
        return new GraphResult(networks, outside);
    }

    private static List<Element> Collect(Document doc, ElementId? viewId) =>
        (viewId is null ? new FilteredElementCollector(doc) : new FilteredElementCollector(doc, viewId))
        .WherePasses(new ElementMulticategoryFilter(PipeCategories))
        .WhereElementIsNotElementType()
        .ToElements().ToList();

    private static PipeNode ToNode(Document doc, Element e, HashSet<long> scope, SuitePresets presets)
    {
        var node = new PipeNode
        {
            Id = e.Id.Value,
            Kind = e switch
            {
                MEPCurve => NodeKind.Pipe,
                _ when e.Category?.BuiltInCategory == BuiltInCategory.OST_PipeFitting => NodeKind.Fitting,
                _ when e.Category?.BuiltInCategory == BuiltInCategory.OST_PipeAccessory => NodeKind.Accessory,
                _ => NodeKind.Other,
            },
            TypeName = doc.GetElement(e.GetTypeId()) switch
            {
                FamilySymbol fs => $"{fs.FamilyName}:{fs.Name}",
                Element t => t.Name,
                null => "",
            },
            Length = e is MEPCurve ? e.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH)?.AsDouble() ?? 0 : 0,
        };

        foreach (var c in Connectors(e))
        {
            if (c.Domain != Domain.DomainPiping || (c.ConnectorType & ConnectorType.Physical) == 0) continue;
            long? connected = null;
            if (c.IsConnected)
                foreach (Connector r in c.AllRefs)
                    if (r.Owner.Id != e.Id && r.ConnectorType != ConnectorType.Logical && scope.Contains(r.Owner.Id.Value))
                    { connected = r.Owner.Id.Value; break; }

            node.Ports.Add(new PipePort
            {
                ConnectedTo = connected,
                Diameter = c.Shape == ConnectorProfileType.Round ? c.Radius * 2 : 0,
                Direction = c.ConnectorType == ConnectorType.End ? ToVec(c.CoordinateSystem.BasisZ) : null,
            });
        }

        node.Attributes[Attr.System] = e.get_Parameter(BuiltInParameter.RBS_DUCT_PIPE_SYSTEM_ABBREVIATION_PARAM)?.AsString() ?? "";
        node.Attributes[Attr.SystemType] = e.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM)?.AsValueString() ?? "";
        node.Attributes[Attr.SystemName] = e.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM)?.AsString() ?? "";
        node.Attributes[Attr.Level] = LevelName(doc, e);
        node.Attributes[Attr.LineNumber] = e.LookupParameter(presets.LineNumberParameter)?.AsString() ?? "";
        if (presets.ZoneParameter.Length > 0 && e.LookupParameter(presets.ZoneParameter) is { } zone)
            node.Attributes[Attr.Zone] = zone.AsString() ?? zone.AsValueString() ?? "";
        return node;
    }

    internal static IEnumerable<Connector> Connectors(Element e)
    {
        var set = e switch
        {
            MEPCurve c => c.ConnectorManager?.Connectors,
            FamilyInstance fi => fi.MEPModel?.ConnectorManager?.Connectors,
            _ => null,
        };
        return set?.Cast<Connector>() ?? Enumerable.Empty<Connector>();
    }

    private static string LevelName(Document doc, Element e)
    {
        var id = e is MEPCurve curve ? curve.ReferenceLevel?.Id : e.LevelId;
        if (id is null || id == ElementId.InvalidElementId)
            id = e.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM)?.AsElementId();
        return id is null ? "" : doc.GetElement(id)?.Name ?? "";
    }

    private static Vec3 ToVec(XYZ v) => new(v.X, v.Y, v.Z);
}

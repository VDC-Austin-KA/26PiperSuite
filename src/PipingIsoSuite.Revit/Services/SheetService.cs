using Autodesk.Revit.DB;
using PipingIsoSuite.Core.Config;
using PipingIsoSuite.Core.Layout;
using PipingIsoSuite.Core.Naming;

namespace PipingIsoSuite.Revit.Services;

public static class SheetService
{
    /// <summary>
    /// Places iso views (not yet on a sheet) onto new sheets, Rows x Columns per sheet, in the given order.
    /// </summary>
    public static string PlaceOnSheets(Document doc, IReadOnlyList<(View View, string LineNumber, string System)> views, SuitePresets presets)
    {
        var layout = presets.Sheets;
        var types = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_TitleBlocks)
            .WhereElementIsElementType().Cast<FamilySymbol>().ToList();
        var titleBlock = layout.TitleBlock.Length == 0
            ? types.FirstOrDefault()
            : types.FirstOrDefault(t => $"{t.FamilyName} : {t.Name}" == layout.TitleBlock || t.Name == layout.TitleBlock);
        if (titleBlock is null)
            return layout.TitleBlock.Length == 0 ? "No title block family is loaded." : $"Title block '{layout.TitleBlock}' not found.";

        var placed = PlacedViewIds(doc);
        var pending = views.Where(v => !placed.Contains(v.View.Id)).ToList();
        if (pending.Count == 0) return "All selected iso views are already on sheets.";

        var numbers = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
            .Select(s => s.SheetNumber).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var perSheet = Math.Max(1, layout.Rows) * Math.Max(1, layout.Columns);
        var mm = (double v) => UnitUtils.ConvertToInternalUnits(v, UnitTypeId.Millimeters);
        var seq = presets.Naming.SheetStartAt;
        var sheets = 0;

        using var tx = new Transaction(doc, "Create iso sheets");
        tx.Start();
        if (!titleBlock.IsActive) titleBlock.Activate();
        foreach (var page in pending.Chunk(perSheet))
        {
            var tokens = new Dictionary<string, object?>
            {
                ["LineNumber"] = page[0].LineNumber, ["LastLineNumber"] = page[^1].LineNumber,
                ["Count"] = page.Length, ["System"] = page[0].System,
            };
            string number;
            do { tokens["Seq"] = seq++; number = TemplateFormatter.SanitizeRevitName(TemplateFormatter.Format(presets.Naming.SheetNumber, tokens)); }
            while (!numbers.Add(number));

            var sheet = ViewSheet.Create(doc, titleBlock.Id);
            sheet.SheetNumber = number;
            sheet.Name = TemplateFormatter.SanitizeRevitName(TemplateFormatter.Format(presets.Naming.SheetName, tokens));
            doc.Regenerate();

            var outer = TitleBlockExtents(doc, sheet);
            var area = GridLayout.Inset(outer, mm(layout.MarginLeftMm), mm(layout.MarginRightMm), mm(layout.MarginTopMm), mm(layout.MarginBottomMm));
            var cells = GridLayout.CellCenters(area, layout.Rows, layout.Columns, layout.FillOrder);
            for (var i = 0; i < page.Length; i++)
                if (Viewport.CanAddViewToSheet(doc, sheet.Id, page[i].View.Id))
                    Viewport.Create(doc, sheet.Id, page[i].View.Id, new XYZ(cells[i].X, cells[i].Y, 0));
            sheets++;
        }
        tx.Commit();
        return $"Created {sheets} sheet(s) holding {pending.Count} iso view(s); {views.Count - pending.Count} already placed.";
    }

    /// <summary>Renumbers sheets in the given order using the sheet number rule (two passes avoid transient clashes).</summary>
    public static string Renumber(Document doc, IReadOnlyList<ViewSheet> sheets, SuitePresets presets)
    {
        // Blocked sheets keep their numbers, so they stay in the taken set below.
        var guard = WorksharingGuard.Reserve(doc, sheets.Select(s => s.Id));
        var blocked = sheets.Count(s => !guard.IsEditable(s.Id.Value));
        sheets = sheets.Where(s => guard.IsEditable(s.Id.Value)).ToList();
        var others = new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).Cast<ViewSheet>()
            .Where(s => !sheets.Any(x => x.Id == s.Id)).Select(s => s.SheetNumber).ToHashSet(StringComparer.OrdinalIgnoreCase);
        using var tx = new Transaction(doc, "Renumber iso sheets");
        tx.Start();
        foreach (var s in sheets) s.SheetNumber = "PIS-TMP-" + s.Id.Value;
        var seq = presets.Naming.SheetStartAt;
        foreach (var s in sheets)
        {
            string number;
            do { number = TemplateFormatter.SanitizeRevitName(TemplateFormatter.Format(presets.Naming.SheetNumber, new Dictionary<string, object?> { ["Seq"] = seq++ })); }
            while (!others.Add(number));
            s.SheetNumber = number;
        }
        tx.Commit();
        return $"Renumbered {sheets.Count} sheet(s)." + (blocked > 0 ? $" {blocked} owned by others were skipped." : "");
    }

    public static HashSet<ElementId> PlacedViewIds(Document doc) =>
        new FilteredElementCollector(doc).OfClass(typeof(Viewport)).Cast<Viewport>().Select(v => v.ViewId).ToHashSet();

    private static Rect TitleBlockExtents(Document doc, ViewSheet sheet)
    {
        var tb = new FilteredElementCollector(doc, sheet.Id).OfCategory(BuiltInCategory.OST_TitleBlocks).FirstElement();
        var bb = tb?.get_BoundingBox(sheet);
        return bb is null
            ? new Rect(sheet.Outline.Min.U, sheet.Outline.Min.V, sheet.Outline.Max.U, sheet.Outline.Max.V)
            : new Rect(bb.Min.X, bb.Min.Y, bb.Max.X, bb.Max.Y);
    }
}

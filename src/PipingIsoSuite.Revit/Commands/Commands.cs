using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Microsoft.Win32;
using PipingIsoSuite.Revit.Services;

namespace PipingIsoSuite.Revit.Commands;

/// <summary>Base for ribbon commands: selection present = connected networks, otherwise whole model.</summary>
public abstract class SuiteCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
    {
        var uidoc = data.Application.ActiveUIDocument;
        if (uidoc is null) { message = "Open a project first."; return Result.Failed; }
        var scope = uidoc.Selection.GetElementIds().Count > 0 ? AnalysisScope.ConnectedToSelection : AnalysisScope.EntireModel;
        try
        {
            var result = Run(uidoc, scope);
            if (result is not null) TaskDialog.Show("Piping ISO Suite", result);
            return Result.Succeeded;
        }
        catch (Autodesk.Revit.Exceptions.OperationCanceledException) { return Result.Cancelled; }
        catch (Exception ex) { message = ex.Message; return Result.Failed; }
    }

    protected abstract string? Run(UIDocument uidoc, AnalysisScope scope);
}

[Transaction(TransactionMode.Manual)]
public sealed class ShowDashboardCommand : SuiteCommand
{
    protected override string? Run(UIDocument uidoc, AnalysisScope scope)
    {
        var pane = uidoc.Application.GetDockablePane(App.PaneId);
        if (pane.IsShown()) pane.Hide(); else pane.Show();
        return null;
    }
}

[Transaction(TransactionMode.Manual)]
public sealed class AssignLineNumbersCommand : SuiteCommand
{
    protected override string Run(UIDocument uidoc, AnalysisScope scope) =>
        SuiteController.AssignLineNumbers(uidoc, scope, App.Settings.Presets, null);
}

[Transaction(TransactionMode.Manual)]
public sealed class ValidateLineNumbersCommand : SuiteCommand
{
    protected override string Run(UIDocument uidoc, AnalysisScope scope)
    {
        var issues = SuiteController.Analyze(uidoc, scope, App.Settings.Presets).Issues;
        if (issues.Count == 0) return "No line number issues found.";
        SuiteController.Select(uidoc, issues.SelectMany(i => i.ElementIds));
        var shown = issues.Take(15).Select(i => "• " + i.Message);
        return $"{issues.Count} issue(s); offending elements are selected.\n\n{string.Join("\n", shown)}" +
               (issues.Count > 15 ? "\n… see the dashboard for the full list." : "");
    }
}

[Transaction(TransactionMode.Manual)]
public sealed class CreateIsoViewsCommand : SuiteCommand
{
    protected override string Run(UIDocument uidoc, AnalysisScope scope) =>
        SuiteController.CreateIsoViews(uidoc, scope, App.Settings.Presets, null);
}

[Transaction(TransactionMode.Manual)]
public sealed class CreateSheetsCommand : SuiteCommand
{
    protected override string Run(UIDocument uidoc, AnalysisScope scope) =>
        SuiteController.CreateSheets(uidoc, scope, App.Settings.Presets, null);
}

[Transaction(TransactionMode.Manual)]
public sealed class ExportEzIsoCommand : SuiteCommand
{
    protected override string? Run(UIDocument uidoc, AnalysisScope scope)
    {
        var path = AskExportPath(uidoc.Document);
        return path is null ? null : SuiteController.ExportEzIso(uidoc, scope, App.Settings.Presets, path);
    }

    internal static string? AskExportPath(Document doc)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export EZ-ISO map",
            Filter = "CSV (*.csv)|*.csv",
            FileName = $"{Path.GetFileNameWithoutExtension(doc.Title)}_EZISO.csv",
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}

using System.Collections.ObjectModel;
using System.Windows.Input;
using Autodesk.Revit.UI;
using PipingIsoSuite.Core.Config;
using PipingIsoSuite.Core.LineNumbering;
using PipingIsoSuite.Core.Topology;
using PipingIsoSuite.Revit.Commands;
using PipingIsoSuite.Revit.Infrastructure;
using PipingIsoSuite.Revit.Services;

namespace PipingIsoSuite.Revit.UI.ViewModels;

public sealed class DashboardViewModel : ObservableObject
{
    private readonly RevitTask _revit;
    private AnalysisScope _scope = AnalysisScope.EntireModel;
    private string _search = "", _status = "Press Refresh to trace piping networks.";
    private bool _busy;
    private TreeItem? _selected;

    public DashboardViewModel(RevitTask revit)
    {
        _revit = revit;
        // Refresh/validate: the rebuilt status line reports network and issue counts.
        RefreshCommand = Command(_ => RunAsync(_ => null));
        AssignCommand = Command(_ => RunAsync(ui => SuiteController.AssignLineNumbers(ui, Scope, Presets, CheckedRunKeys())));
        ValidateCommand = RefreshCommand;
        CreateViewsCommand = Command(_ => RunAsync(ui => SuiteController.CreateIsoViews(ui, Scope, Presets, CheckedRunKeys())));
        CreateSheetsCommand = Command(_ => RunAsync(ui => SuiteController.CreateSheets(ui, Scope, Presets, CheckedRunKeys())));
        ExportCommand = Command(_ => RunAsync(ui => ExportEzIsoCommand.AskExportPath(ui.Document) is { } path
            ? SuiteController.ExportEzIso(ui, Scope, Presets, path) : "Export cancelled."));
        PropagateCommand = Command(_ => RunAsync(ui => Selected?.Run is { } run
            ? SuiteController.Propagate(ui, Scope, Presets, run.Nodes[0].Id) : "Select a header or branch to copy from."));
        RenameCommand = Command(_ => RunAsync(ui => SuiteController.RenameViews(ui, Scope, Presets, CheckedIds(TreeKind.View))));
        RenumberCommand = Command(_ => RunAsync(ui => SuiteController.RenumberSheets(ui, Presets, SheetsToRenumber())));
        DeleteCommand = Command(_ => RunAsync(ui => CheckedIds(TreeKind.View, TreeKind.Sheet).Count == 0 ? "Check the views/sheets to delete."
            : Confirm($"Delete {CheckedIds(TreeKind.View).Count} iso view(s) and {CheckedIds(TreeKind.Sheet).Count} sheet(s)?")
            ? SuiteController.Delete(ui, CheckedIds(TreeKind.View, TreeKind.Sheet)) : "Delete cancelled."));
        SelectInModelCommand = Command(p => RunAsync(ui => { SuiteController.Select(ui, IdsOf(p as TreeItem ?? Selected)); return null; }, refresh: false));
        SelectIssueCommand = Command(p => RunAsync(ui => { if (p is ValidationIssue i) SuiteController.Select(ui, i.ElementIds); return null; }, refresh: false));
        SaveSettingsCommand = Command(_ => Try(() => { ConfigStore.Save(ConfigStore.DefaultUserSettingsPath, App.Settings); return "Settings saved locally."; }));
        PublishSharedCommand = Command(_ => Try(() => { ConfigStore.Save(ConfigStore.DefaultUserSettingsPath, App.Settings); ConfigStore.PublishShared(App.Settings); return "Presets published to the shared file."; }));
        ReloadSharedCommand = Command(_ => Try(() =>
        {
            var shared = SharedConfigPath;
            (App.Settings, App.SettingsSource) = ConfigStore.LoadEffective();
            if (shared.Length > 0 && App.Settings.SharedConfigPath != shared)
            {   // A newly typed path that was not saved yet.
                App.Settings.SharedConfigPath = shared;
                if (File.Exists(shared)) App.Settings.Presets = ConfigStore.Load<SuitePresets>(shared);
            }
            OnPropertyChanged(string.Empty);
            return App.SettingsSource;
        }));
    }

    public ObservableCollection<TreeItem> Items { get; } = new();
    public ObservableCollection<ValidationIssue> Issues { get; } = new();
    public Array Scopes { get; } = Enum.GetValues<AnalysisScope>();
    public Array Orientations { get; } = Enum.GetValues<IsoOrientation>();
    public Array FillOrders { get; } = Enum.GetValues<FillOrder>();

    public SuitePresets Presets => App.Settings.Presets;
    public string SettingsSource => App.SettingsSource;
    public string SharedConfigPath { get => App.Settings.SharedConfigPath; set { App.Settings.SharedConfigPath = value.Trim(); OnPropertyChanged(); } }

    public string SystemCodes { get => Codes.Format(Presets.LineNumbering.SystemCodes); set => Presets.LineNumbering.SystemCodes = Codes.Parse(value); }
    public string LevelCodes { get => Codes.Format(Presets.LineNumbering.LevelCodes); set => Presets.LineNumbering.LevelCodes = Codes.Parse(value); }
    public string ZoneCodes { get => Codes.Format(Presets.LineNumbering.ZoneCodes); set => Presets.LineNumbering.ZoneCodes = Codes.Parse(value); }
    public string PropagateParameters
    {
        get => string.Join("; ", Presets.PropagateParameters);
        set => Presets.PropagateParameters = value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    public AnalysisScope Scope { get => _scope; set => Set(ref _scope, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public bool IsBusy { get => _busy; private set => Set(ref _busy, value); }
    public TreeItem? Selected { get => _selected; set => Set(ref _selected, value); }

    public string SearchText
    {
        get => _search;
        set { if (Set(ref _search, value)) foreach (var i in Items) i.ApplyFilter(value.Trim()); }
    }

    public ICommand RefreshCommand { get; }
    public ICommand AssignCommand { get; }
    public ICommand ValidateCommand { get; }
    public ICommand CreateViewsCommand { get; }
    public ICommand CreateSheetsCommand { get; }
    public ICommand ExportCommand { get; }
    public ICommand PropagateCommand { get; }
    public ICommand RenameCommand { get; }
    public ICommand RenumberCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand SelectInModelCommand { get; }
    public ICommand SelectIssueCommand { get; }
    public ICommand SaveSettingsCommand { get; }
    public ICommand PublishSharedCommand { get; }
    public ICommand ReloadSharedCommand { get; }

    private RelayCommand Command(Action<object?> run) => new(run, _ => !IsBusy);

    /// <summary>Runs <paramref name="work"/> in the Revit API context, then re-traces and rebuilds the tree.</summary>
    private async void RunAsync(Func<UIDocument, string?> work, bool refresh = true)
    {
        IsBusy = true;
        try
        {
            var (message, snapshot) = await _revit.Run(app =>
            {
                var ui = app.ActiveUIDocument ?? throw new InvalidOperationException("Open a project first.");
                var msg = work(ui);
                return (msg, refresh ? SuiteController.Analyze(ui, Scope, Presets) : null);
            });
            if (snapshot is not null) Rebuild(snapshot);
            if (message is not null) Status = message;
        }
        catch (Exception ex) { Status = "Error: " + ex.Message; }
        finally { IsBusy = false; CommandManager.InvalidateRequerySuggested(); }
    }

    private void Try(Func<string> action)
    {
        try { Status = action(); }
        catch (Exception ex) { Status = "Error: " + ex.Message; }
    }

    private void Rebuild(Snapshot s)
    {
        var expanded = Items.SelectMany(i => i.SelfAndDescendants()).Where(i => i.IsExpanded).Select(Key).ToHashSet();
        Items.Clear();
        foreach (var system in s.Graph.Networks.GroupBy(n => n.SystemName).OrderBy(g => g.Key))
        {
            var runs = system.SelectMany(n => n.Runs).ToList();
            var node = new TreeItem(TreeKind.System, system.Key.Length > 0 ? system.Key : "(no system)",
                $"{system.Count()} network(s), {runs.Count} run(s), {runs.Count(r => r.ExistingLineNumber is null)} unnumbered");
            foreach (var network in system) node.Children.Add(RunItem(network.Header, s));
            Items.Add(node);
        }
        foreach (var item in Items.SelectMany(i => i.SelfAndDescendants()))
            item.IsExpanded = expanded.Contains(Key(item));
        if (_search.Length > 0) foreach (var i in Items) i.ApplyFilter(_search.Trim());

        Issues.Clear();
        foreach (var issue in s.Issues) Issues.Add(issue);
        Status = $"{s.DocumentTitle}: {s.Graph.Networks.Count} network(s), {s.Graph.Runs.Count()} run(s), {s.Issues.Count} issue(s).";
    }

    private static TreeItem RunItem(PipeRun run, Snapshot s)
    {
        var number = run.ExistingLineNumber;
        var size = run.MaxDiameter * 304.8;
        var item = new TreeItem(run.Role == RunRole.Header ? TreeKind.Header : TreeKind.Branch,
            number ?? $"(unnumbered {run.Role.ToString().ToLowerInvariant()} #{run.Id})",
            $"Ø{size:0} mm · {run.TotalLength * 0.3048:0.0} m · {run.Nodes.Count} el · {run.Representative.Get(Attr.Level)}")
        { Run = run };

        if (s.ViewFor(number) is { } view)
        {
            var viewItem = new TreeItem(TreeKind.View, view.Name) { ElementId = view.Id };
            if (s.SheetByView.GetValueOrDefault(view.Id) is { } sheet)
                viewItem.Children.Add(new TreeItem(TreeKind.Sheet, $"{sheet.Number} - {sheet.Name}") { ElementId = sheet.Id });
            item.Children.Add(viewItem);
        }
        foreach (var child in run.Children) item.Children.Add(RunItem(child, s));
        return item;
    }

    private static string Key(TreeItem i) => i.Run is { } r ? "run:" + r.Nodes[0].Id : i.ElementId is { } id ? "el:" + id : "sys:" + i.Text;

    private IEnumerable<TreeItem> AllItems => Items.SelectMany(i => i.SelfAndDescendants());

    /// <summary>Element ids identifying checked runs, or null (= all runs in scope) when nothing is checked.</summary>
    private IReadOnlySet<long>? CheckedRunKeys()
    {
        var keys = AllItems.Where(i => i.IsChecked && i.Run is not null).SelectMany(i => i.Run!.ElementIds).ToHashSet();
        return keys.Count == 0 ? null : keys;
    }

    private List<long> CheckedIds(params TreeKind[] kinds) =>
        AllItems.Where(i => i.IsChecked && kinds.Contains(i.Kind) && i.ElementId is not null).Select(i => i.ElementId!.Value).Distinct().ToList();

    /// <summary>Checked sheets, or every sheet in the tree (in tree order) when none are checked.</summary>
    private List<long> SheetsToRenumber()
    {
        var ids = CheckedIds(TreeKind.Sheet);
        return ids.Count > 0 ? ids : AllItems.Where(i => i.Kind == TreeKind.Sheet && i.ElementId is not null).Select(i => i.ElementId!.Value).Distinct().ToList();
    }

    private static IEnumerable<long> IdsOf(TreeItem? item) =>
        item is null ? Enumerable.Empty<long>()
        : item.ElementId is { } id ? new[] { id }
        : item.SelfAndDescendants().Where(i => i.Run is not null).SelectMany(i => i.Run!.ElementIds);

    private static bool Confirm(string text) =>
        TaskDialog.Show("Piping ISO Suite", text, TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No) == TaskDialogResult.Yes;

    /// <summary>"KEY=CODE; KEY=CODE" text form for code maps in the settings tab.</summary>
    private static class Codes
    {
        public static string Format(Dictionary<string, string> map) => string.Join("; ", map.Select(kv => $"{kv.Key}={kv.Value}"));

        public static Dictionary<string, string> Parse(string text) =>
            text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(p => p.Split('=', 2, StringSplitOptions.TrimEntries))
                .Where(p => p.Length == 2 && p[0].Length > 0)
                .GroupBy(p => p[0], StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Last()[1], StringComparer.OrdinalIgnoreCase);
    }
}

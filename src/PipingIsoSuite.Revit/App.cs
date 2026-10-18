using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;
using PipingIsoSuite.Core.Config;
using PipingIsoSuite.Revit.Commands;
using PipingIsoSuite.Revit.Infrastructure;
using PipingIsoSuite.Revit.UI.ViewModels;
using PipingIsoSuite.Revit.UI.Views;

namespace PipingIsoSuite.Revit;

public sealed class App : IExternalApplication
{
    public static readonly DockablePaneId PaneId = new(new Guid("8E3C6F2A-41B7-4C1D-9F0E-6B2D7A5C3E91"));
    private const string Tab = "Piping ISO Suite";

    /// <summary>Effective settings (presets from the shared file when configured).</summary>
    public static UserSettings Settings { get; set; } = new();
    public static string SettingsSource { get; set; } = "";
    public static RevitTask Revit { get; private set; } = null!;

    public Result OnStartup(UIControlledApplication app)
    {
        (Settings, SettingsSource) = ConfigStore.LoadEffective();
        Revit = new RevitTask();

        app.CreateRibbonTab(Tab);
        var panel = app.CreateRibbonPanel(Tab, "Piping");
        Add<ShowDashboardCommand>(panel, "Dashboard", "Open the Piping ISO Suite dashboard", "D", Colors.SteelBlue);
        panel.AddSeparator();
        Add<AssignLineNumbersCommand>(panel, "Assign\nLine Nos", "Trace headers/branches and assign BC_LINENUMBER (selection = connected networks)", "#", Colors.SeaGreen);
        Add<ValidateLineNumbersCommand>(panel, "Validate", "Flag duplicate, mixed and unassigned line numbers", "✓", Colors.DarkOrange);
        panel.AddSeparator();
        Add<CreateIsoViewsCommand>(panel, "ISO\nViews", "Create/update one isometric per line number", "3D", Colors.MediumPurple);
        Add<CreateSheetsCommand>(panel, "ISO\nSheets", "Place iso views on sheets using the sheet layout preset", "S", Colors.IndianRed);
        Add<ExportEzIsoCommand>(panel, "EZ-ISO\nExport", "Export the EZ-ISO map (CSV + JSON)", "EZ", Colors.Teal);

        var vm = new DashboardViewModel(Revit);
        app.RegisterDockablePane(PaneId, "Piping ISO Suite", new DashboardPage(vm));
        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication app) => Result.Succeeded;

    private static void Add<T>(RibbonPanel panel, string text, string tooltip, string glyph, Color color) where T : IExternalCommand
    {
        var data = new PushButtonData(typeof(T).Name, text, Assembly.GetExecutingAssembly().Location, typeof(T).FullName)
        {
            ToolTip = tooltip,
            LargeImage = Glyph(glyph, color, 32),
            Image = Glyph(glyph, color, 16),
        };
        panel.AddItem(data);
    }

    /// <summary>Draws a simple lettered tile so the add-in ships without image assets.</summary>
    private static BitmapSource Glyph(string text, Color color, int size)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRoundedRectangle(new SolidColorBrush(color), null, new Rect(0, 0, size, size), size / 6.0, size / 6.0);
            var ft = new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                size * (text.Length > 1 ? 0.42 : 0.6), Brushes.White, 1.0);
            dc.DrawText(ft, new Point((size - ft.Width) / 2, (size - ft.Height) / 2));
        }
        var bmp = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }
}

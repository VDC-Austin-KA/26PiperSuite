using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Autodesk.Revit.UI;
using PipingIsoSuite.Revit.UI.ViewModels;

namespace PipingIsoSuite.Revit.UI.Views;

public partial class DashboardPage : Page, IDockablePaneProvider
{
    private readonly DashboardViewModel _vm;

    public DashboardPage(DashboardViewModel vm)
    {
        InitializeComponent();
        DataContext = _vm = vm;
    }

    public void SetupDockablePane(DockablePaneProviderData data)
    {
        data.FrameworkElement = this;
        data.InitialState = new DockablePaneState { DockPosition = DockPosition.Right, MinimumWidth = 360 };
    }

    // TreeView.SelectedItem is read-only, so selection is pushed to the view model here.
    private void OnTreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e) =>
        _vm.Selected = e.NewValue as TreeItem;

    private void OnTreeDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_vm.Selected is { } item && _vm.SelectInModelCommand.CanExecute(item)) _vm.SelectInModelCommand.Execute(item);
    }

    private void OnIssueDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (IssueList.SelectedItem is { } issue && _vm.SelectIssueCommand.CanExecute(issue)) _vm.SelectIssueCommand.Execute(issue);
    }
}

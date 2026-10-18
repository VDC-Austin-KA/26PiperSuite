using System.Collections.ObjectModel;
using PipingIsoSuite.Core.Topology;
using PipingIsoSuite.Revit.Infrastructure;

namespace PipingIsoSuite.Revit.UI.ViewModels;

public enum TreeKind { System, Header, Branch, View, Sheet }

/// <summary>Dashboard tree node: System → Header → Branch… → Iso View → Sheet.</summary>
public sealed class TreeItem : ObservableObject
{
    private bool _isChecked, _isExpanded, _isVisible = true;

    public TreeItem(TreeKind kind, string text, string detail = "")
    {
        Kind = kind; Text = text; Detail = detail;
    }

    public TreeKind Kind { get; }
    public string Text { get; }
    public string Detail { get; }
    public string Badge => Kind switch { TreeKind.System => "SYS", TreeKind.Header => "HDR", TreeKind.Branch => "BR", TreeKind.View => "ISO", _ => "SHT" };
    public PipeRun? Run { get; init; }
    /// <summary>Revit element id for view/sheet nodes.</summary>
    public long? ElementId { get; init; }
    public ObservableCollection<TreeItem> Children { get; } = new();

    public bool IsChecked
    {
        get => _isChecked;
        set { if (Set(ref _isChecked, value)) foreach (var c in Children) c.IsChecked = value; }
    }

    public bool IsExpanded { get => _isExpanded; set => Set(ref _isExpanded, value); }
    public bool IsVisible { get => _isVisible; set => Set(ref _isVisible, value); }

    public IEnumerable<TreeItem> SelfAndDescendants() => Children.SelectMany(c => c.SelfAndDescendants()).Prepend(this);

    /// <summary>Shows items matching <paramref name="text"/> (and their ancestors); returns true if anything matched.</summary>
    public bool ApplyFilter(string text)
    {
        var self = text.Length == 0 || Text.Contains(text, StringComparison.OrdinalIgnoreCase) || Detail.Contains(text, StringComparison.OrdinalIgnoreCase);
        var any = false;
        foreach (var c in Children) any |= c.ApplyFilter(self ? "" : text);
        IsVisible = self || any;
        if (text.Length > 0 && any) IsExpanded = true;
        return IsVisible;
    }
}

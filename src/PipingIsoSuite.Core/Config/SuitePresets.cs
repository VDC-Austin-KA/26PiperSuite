namespace PipingIsoSuite.Core.Config;

/// <summary>Team-shareable rules: line numbering, iso views, sheets and naming.</summary>
public sealed class SuitePresets
{
    public int SchemaVersion { get; set; } = 1;
    public string LineNumberParameter { get; set; } = "BC_LINENUMBER";
    /// <summary>Instance parameter read for the {Zone} token; empty disables zones.</summary>
    public string ZoneParameter { get; set; } = "";
    public LineNumberScheme LineNumbering { get; set; } = new();
    public IsoViewSettings IsoViews { get; set; } = new();
    public SheetLayoutSettings Sheets { get; set; } = new();
    public NamingRules Naming { get; set; } = new();
    /// <summary>Parameters copied between identical loops by "Propagate".</summary>
    public List<string> PropagateParameters { get; set; } = new() { "Comments" };
}

public sealed class LineNumberScheme
{
    public string Prefix { get; set; } = "L";
    /// <summary>Tokens: {Prefix} {System} {SystemType} {Level} {Zone} {Role} {Seq}.</summary>
    public string HeaderPattern { get; set; } = "{Prefix}-{System}-{Level}-{Seq:000}";
    /// <summary>Header tokens plus {Header} (parent run number) and {BranchSeq}.</summary>
    public string BranchPattern { get; set; } = "{Header}-{BranchSeq:00}";
    public int StartAt { get; set; } = 1;
    public bool OverwriteExisting { get; set; }
    public Dictionary<string, string> SystemCodes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> LevelCodes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> ZoneCodes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public enum IsoOrientation { SouthEast, SouthWest, NorthEast, NorthWest }

public sealed class IsoViewSettings
{
    /// <summary>3D view family type name; empty uses the first available.</summary>
    public string ViewFamilyType { get; set; } = "";
    public string ViewTemplate { get; set; } = "";
    public IsoOrientation Orientation { get; set; } = IsoOrientation.SouthEast;
    public double SectionBoxPaddingMm { get; set; } = 300;
    /// <summary>0 keeps the scale from the template / view type.</summary>
    public int Scale { get; set; }
    public bool LockOrientation { get; set; } = true;
    /// <summary>Re-use (and re-crop) an existing iso for the same line instead of creating a new one.</summary>
    public bool UpdateExisting { get; set; } = true;
}

public enum FillOrder { RowMajor, ColumnMajor }

public sealed class SheetLayoutSettings
{
    /// <summary>"Family : Type" of the title block; empty uses the first loaded.</summary>
    public string TitleBlock { get; set; } = "";
    public int Rows { get; set; } = 1;
    public int Columns { get; set; } = 1;
    public FillOrder FillOrder { get; set; } = FillOrder.RowMajor;
    /// <summary>Margins inside the title block extents, millimetres.</summary>
    public double MarginLeftMm { get; set; } = 25;
    public double MarginRightMm { get; set; } = 120;
    public double MarginTopMm { get; set; } = 15;
    public double MarginBottomMm { get; set; } = 15;
}

public sealed class NamingRules
{
    /// <summary>Tokens: {LineNumber} {System} {Level} {Role} {Header}.</summary>
    public string ViewName { get; set; } = "ISO - {LineNumber}";
    /// <summary>Tokens: {Seq} {LineNumber} (first on sheet) {LastLineNumber} {Count} {System}.</summary>
    public string SheetNumber { get; set; } = "ISO-{Seq:000}";
    public string SheetName { get; set; } = "ISOMETRIC - {LineNumber}";
    public int SheetStartAt { get; set; } = 1;
}

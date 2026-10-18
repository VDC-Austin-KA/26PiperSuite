using PipingIsoSuite.Core.Config;

namespace PipingIsoSuite.Core.Tests;

public sealed class ConfigStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "PipingIsoSuite.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private string In(string name) => Path.Combine(_dir, name);

    [Fact]
    public void Save_then_Load_round_trips_presets()
    {
        var presets = new SuitePresets { LineNumberParameter = "BC_LN" };
        presets.LineNumbering.Prefix = "P";
        presets.LineNumbering.OverwriteExisting = true;
        presets.LineNumbering.SystemCodes["CHW"] = "CW";
        presets.IsoViews.Orientation = IsoOrientation.NorthWest;
        presets.Sheets.FillOrder = FillOrder.ColumnMajor;
        presets.Naming.SheetNumber = "S-{Seq:000}";
        var path = In("presets.json");

        ConfigStore.Save(path, presets);
        var loaded = ConfigStore.Load<SuitePresets>(path);

        Assert.Equal("BC_LN", loaded.LineNumberParameter);
        Assert.Equal("P", loaded.LineNumbering.Prefix);
        Assert.True(loaded.LineNumbering.OverwriteExisting);
        Assert.Equal("CW", loaded.LineNumbering.SystemCodes["CHW"]);
        Assert.Equal(IsoOrientation.NorthWest, loaded.IsoViews.Orientation);
        Assert.Equal(FillOrder.ColumnMajor, loaded.Sheets.FillOrder);
        Assert.Equal("S-{Seq:000}", loaded.Naming.SheetNumber);
    }

    [Fact]
    public void LoadEffective_uses_shared_presets_when_the_shared_file_exists()
    {
        var shared = In("shared.json");
        ConfigStore.Save(shared, new SuitePresets { LineNumbering = { Prefix = "TEAM" } });
        var userPath = In("settings.json");
        ConfigStore.Save(userPath, new UserSettings
        {
            SharedConfigPath = shared,
            Presets = new SuitePresets { LineNumbering = { Prefix = "LOCAL" } },
        });

        var (settings, source) = ConfigStore.LoadEffective(userPath);

        Assert.StartsWith("Shared presets", source);
        Assert.Equal("TEAM", settings.Presets.LineNumbering.Prefix);
    }

    [Fact]
    public void LoadEffective_falls_back_to_local_presets_when_shared_file_is_missing()
    {
        var userPath = In("settings.json");
        ConfigStore.Save(userPath, new UserSettings
        {
            SharedConfigPath = In("missing.json"),
            Presets = new SuitePresets { LineNumbering = { Prefix = "LOCAL" } },
        });

        var (settings, source) = ConfigStore.LoadEffective(userPath);

        Assert.Contains("not found", source);
        Assert.Equal("LOCAL", settings.Presets.LineNumbering.Prefix);
    }
}

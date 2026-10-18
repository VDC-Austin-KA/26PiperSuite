using System.Text.Json;
using System.Text.Json.Serialization;

namespace PipingIsoSuite.Core.Config;

/// <summary>Per-user settings. Presets come from the shared file when one is configured.</summary>
public sealed class UserSettings
{
    /// <summary>Central JSON on a network drive, e.g. \\server\bim\PipingIsoSuite.json.</summary>
    public string SharedConfigPath { get; set; } = "";
    public SuitePresets Presets { get; set; } = new();
}

public static class ConfigStore
{
    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static string DefaultUserSettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PipingIsoSuite", "settings.json");

    public static T Load<T>(string path) where T : new() =>
        File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json) ?? new T() : new T();

    /// <summary>Writes via a temp file + replace so concurrent readers on a share never see a partial file.</summary>
    public static void Save<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, Json));
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>
    /// Loads user settings and, if a reachable shared file is configured, replaces the presets with
    /// the team copy. Returns a message describing where presets came from.
    /// </summary>
    public static (UserSettings Settings, string Source) LoadEffective(string? userPath = null)
    {
        userPath ??= DefaultUserSettingsPath;
        var settings = Load<UserSettings>(userPath);
        if (settings.SharedConfigPath.Length == 0) return (settings, $"Local presets ({userPath})");
        try
        {
            if (!File.Exists(settings.SharedConfigPath))
                return (settings, $"Shared file not found, using local presets ({settings.SharedConfigPath})");
            settings.Presets = Load<SuitePresets>(settings.SharedConfigPath);
            return (settings, $"Shared presets ({settings.SharedConfigPath})");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return (settings, $"Shared file unreadable ({ex.Message}), using local presets");
        }
    }

    public static void PublishShared(UserSettings settings)
    {
        if (settings.SharedConfigPath.Length == 0) throw new InvalidOperationException("No shared configuration path is set.");
        Save(settings.SharedConfigPath, settings.Presets);
    }
}

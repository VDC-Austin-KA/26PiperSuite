using System.Globalization;
using System.Text;
using System.Text.Json;
using PipingIsoSuite.Core.Config;

namespace PipingIsoSuite.Core.Export;

/// <summary>One isometric group (one line number) in the EZ-ISO map.</summary>
public sealed record IsoRecord(
    string LineNumber,
    string Role,
    string ParentLineNumber,
    string System,
    string Level,
    string Zone,
    double MaxDiameterMm,
    double PipeLengthM,
    int ElementCount,
    string LoopSignature,
    string ViewName,
    string SheetNumber,
    IReadOnlyList<long> ElementIds);

/// <summary>
/// Writes the EZ-ISO preparation map as CSV (one row per line number) and JSON (full element lists).
/// Column names are stable so downstream EZ-ISO import mappings can rely on them.
/// </summary>
public static class EzIsoExporter
{
    public const string Schema = "PipingIsoSuite.EzIsoMap/1";

    private static readonly string[] Columns =
    {
        "LineNumber", "Role", "ParentLineNumber", "System", "Level", "Zone", "MaxDiameterMm",
        "PipeLengthM", "ElementCount", "LoopSignature", "ViewName", "SheetNumber", "ElementIds",
    };

    public static void WriteCsv(string path, IEnumerable<IsoRecord> records)
    {
        var sb = new StringBuilder().AppendLine(string.Join(",", Columns));
        foreach (var r in records)
        {
            string[] cells =
            {
                r.LineNumber, r.Role, r.ParentLineNumber, r.System, r.Level, r.Zone,
                r.MaxDiameterMm.ToString("0.#", CultureInfo.InvariantCulture),
                r.PipeLengthM.ToString("0.###", CultureInfo.InvariantCulture),
                r.ElementCount.ToString(CultureInfo.InvariantCulture), r.LoopSignature, r.ViewName, r.SheetNumber,
                string.Join(";", r.ElementIds),
            };
            sb.AppendLine(string.Join(",", cells.Select(Escape)));
        }
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    public static void WriteJson(string path, string projectName, IEnumerable<IsoRecord> records)
    {
        var doc = new { schema = Schema, project = projectName, exportedUtc = DateTime.UtcNow, isos = records };
        File.WriteAllText(path, JsonSerializer.Serialize(doc, ConfigStore.Json));
    }

    internal static string Escape(string value) =>
        value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
}

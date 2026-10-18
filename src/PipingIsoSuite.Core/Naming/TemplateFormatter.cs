using System.Globalization;
using System.Text.RegularExpressions;

namespace PipingIsoSuite.Core.Naming;

/// <summary>
/// Expands templates such as <c>"{Prefix}-{System}-{Seq:000}"</c>. Tokens are case-insensitive;
/// the optional format after ':' is a .NET format string applied to numeric values.
/// A missing/empty token is dropped together with one separator next to it, so
/// <c>"{Prefix}-{Level}-{Seq}"</c> with no level yields <c>"L-001"</c>, not <c>"L--001"</c>.
/// </summary>
public static partial class TemplateFormatter
{
    [GeneratedRegex(@"\{(?<name>[A-Za-z]+)(?::(?<fmt>[^}]+))?\}")]
    private static partial Regex Token();

    [GeneratedRegex("^(\u0001[-_. /]?)+")]
    private static partial Regex LeadingEmpty();

    [GeneratedRegex("[-_. /]?\u0001")]
    private static partial Regex Empty();

    private const string EmptyMark = "\u0001";

    public static string Format(string template, IReadOnlyDictionary<string, object?> values)
    {
        var lookup = new Dictionary<string, object?>(values, StringComparer.OrdinalIgnoreCase);
        var raw = Token().Replace(template, m =>
        {
            if (!lookup.TryGetValue(m.Groups["name"].Value, out var v) || v is null) return EmptyMark;
            var fmt = m.Groups["fmt"].Success ? m.Groups["fmt"].Value : null;
            var text = v is IFormattable f ? f.ToString(fmt, CultureInfo.InvariantCulture) : v.ToString() ?? "";
            return text.Length == 0 ? EmptyMark : text;
        });
        return Empty().Replace(LeadingEmpty().Replace(raw, ""), "").Trim();
    }

    public static bool UsesToken(string template, string token) =>
        Token().Matches(template).Any(m => m.Groups["name"].Value.Equals(token, StringComparison.OrdinalIgnoreCase));

    /// <summary>Replaces characters Revit rejects in view/sheet names: \ : { } [ ] | ; &lt; &gt; ? ` ~</summary>
    public static string SanitizeRevitName(string name)
    {
        var chars = name.Select(c => @"\:{}[]|;<>?`~".Contains(c) ? '_' : c).ToArray();
        return new string(chars).Trim();
    }
}

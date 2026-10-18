using PipingIsoSuite.Core.Naming;

namespace PipingIsoSuite.Core.Tests;

public class TemplateFormatterTests
{
    [Fact]
    public void Format_specifiers_apply_to_numbers_and_tokens_are_case_insensitive()
    {
        var values = new Dictionary<string, object?> { ["Prefix"] = "L", ["Seq"] = 7 };

        Assert.Equal("L-007", TemplateFormatter.Format("{prefix}-{SEQ:000}", values));
    }

    [Fact]
    public void Missing_null_and_leading_empty_tokens_collapse_with_one_adjacent_separator()
    {
        var values = new Dictionary<string, object?> { ["Prefix"] = "L", ["Level"] = null, ["Seq"] = 1 };

        Assert.Equal("L-001", TemplateFormatter.Format("{Prefix}-{Level}-{Seq:000}", values));
        Assert.Equal("L-001", TemplateFormatter.Format("{Prefix}-{Zone}-{Seq:000}", values));
        Assert.Equal("3", TemplateFormatter.Format("{Level}-{Seq}", new Dictionary<string, object?> { ["Seq"] = 3 }));
    }

    [Fact]
    public void Literal_separators_are_kept_when_value_present()
    {
        var values = new Dictionary<string, object?> { ["LineNumber"] = "L-1" };

        Assert.Equal("ISO - L-1", TemplateFormatter.Format("ISO - {LineNumber}", values));
    }

    [Fact]
    public void UsesToken_is_case_insensitive_and_SanitizeRevitName_replaces_invalid_characters()
    {
        Assert.True(TemplateFormatter.UsesToken("{Prefix}-{seq:000}", "Seq"));
        Assert.False(TemplateFormatter.UsesToken("{Prefix}-{System}", "Seq"));

        Assert.Equal("ISO_L_1_", TemplateFormatter.SanitizeRevitName("ISO:L{1}"));
        Assert.Equal("A_B", TemplateFormatter.SanitizeRevitName("  A|B  "));
    }
}

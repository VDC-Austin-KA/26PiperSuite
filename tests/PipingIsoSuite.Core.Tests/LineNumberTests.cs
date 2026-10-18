using PipingIsoSuite.Core.Config;
using PipingIsoSuite.Core.LineNumbering;
using PipingIsoSuite.Core.Topology;

namespace PipingIsoSuite.Core.Tests;

public class LineNumberPlannerTests
{
    private static IReadOnlyList<PipeNetwork> Networks(GraphBuilder g) => NetworkAnalyzer.Analyze(g.Build());

    private static string NumberOf(IEnumerable<LineNumberAssignment> plan, PipeRun run) =>
        plan.Single(a => a.Run == run).LineNumber;

    [Fact]
    public void Header_and_branch_get_sequenced_and_derived_numbers()
    {
        var networks = Networks(GraphBuilder.TeeHeaderWithBranch());
        var net = networks.Single();

        var plan = LineNumberPlanner.Plan(networks, new LineNumberScheme(), Array.Empty<string>());

        Assert.Equal("L-CHW-L1-001", NumberOf(plan, net.Header));
        Assert.Equal("L-CHW-L1-001-01", NumberOf(plan, net.Header.Children[0]));
    }

    [Fact]
    public void Numbers_in_use_outside_scope_are_skipped()
    {
        var networks = Networks(GraphBuilder.TeeHeaderWithBranch());
        var net = networks.Single();

        var plan = LineNumberPlanner.Plan(networks, new LineNumberScheme(), new[] { "L-CHW-L1-001" });

        Assert.Equal("L-CHW-L1-002", NumberOf(plan, net.Header));
        Assert.Equal("L-CHW-L1-002-01", NumberOf(plan, net.Header.Children[0]));
    }

    [Fact]
    public void Existing_consistent_numbers_are_kept_unless_overwriting()
    {
        var g = GraphBuilder.TeeHeaderWithBranch()
            .With(1, Attr.LineNumber, "L-CHW-L1-007")
            .With(2, Attr.LineNumber, "L-CHW-L1-007")
            .With(3, Attr.LineNumber, "L-CHW-L1-007")
            .With(4, Attr.LineNumber, "L-CHW-L1-007-01");
        var networks = Networks(g);
        var net = networks.Single();

        var kept = LineNumberPlanner.Plan(networks, new LineNumberScheme(), Array.Empty<string>());
        Assert.All(kept, a => Assert.False(a.IsNew));
        Assert.Equal("L-CHW-L1-007", NumberOf(kept, net.Header));

        var overwrite = LineNumberPlanner.Plan(networks, new LineNumberScheme { OverwriteExisting = true }, Array.Empty<string>());
        Assert.All(overwrite, a => Assert.True(a.IsNew));
        Assert.Equal("L-CHW-L1-001", NumberOf(overwrite, net.Header));
    }

    [Fact]
    public void Newly_drawn_unnumbered_element_inherits_its_runs_number()
    {
        var g = GraphBuilder.TeeHeaderWithBranch()
            .With(1, Attr.LineNumber, "L-CHW-L1-007")
            .With(2, Attr.LineNumber, "L-CHW-L1-007");
        var networks = Networks(g);

        var plan = LineNumberPlanner.Plan(networks, new LineNumberScheme(), Array.Empty<string>());
        var header = plan.Single(a => a.Run == networks.Single().Header);
        Assert.False(header.IsNew);
        Assert.Equal("L-CHW-L1-007", header.LineNumber);
    }

    [Fact]
    public void Empty_level_drops_its_separator()
    {
        var networks = Networks(GraphBuilder.TeeHeaderWithBranch().WithAll(Attr.Level, ""));
        var net = networks.Single();

        var plan = LineNumberPlanner.Plan(networks, new LineNumberScheme(), Array.Empty<string>());

        Assert.Equal("L-CHW-001", NumberOf(plan, net.Header));
        Assert.Equal("L-CHW-001-01", NumberOf(plan, net.Header.Children[0]));
    }

    [Fact]
    public void System_codes_are_applied_to_tokens()
    {
        var networks = Networks(GraphBuilder.TeeHeaderWithBranch());
        var scheme = new LineNumberScheme { SystemCodes = new() { ["CHW"] = "CW" } };

        var plan = LineNumberPlanner.Plan(networks, scheme, Array.Empty<string>());

        Assert.Equal("L-CW-L1-001", NumberOf(plan, networks.Single().Header));
    }

    [Fact]
    public void Pattern_without_sequence_token_gets_numeric_suffix_on_collision()
    {
        var nodes = GraphBuilder.TeeHeaderWithBranch(1).Build()
            .Concat(GraphBuilder.TeeHeaderWithBranch(11).Build());
        var networks = NetworkAnalyzer.Analyze(nodes);
        var scheme = new LineNumberScheme { HeaderPattern = "{Prefix}-{System}" };

        var plan = LineNumberPlanner.Plan(networks, scheme, Array.Empty<string>());

        Assert.Equal("L-CHW", NumberOf(plan, networks[0].Header));
        Assert.Equal("L-CHW-2", NumberOf(plan, networks[1].Header));
    }
}

public class LineNumberValidatorTests
{
    [Fact]
    public void Elements_without_a_line_number_are_unassigned()
    {
        var g = GraphBuilder.TeeHeaderWithBranch()
            .With(1, Attr.LineNumber, "L-1")
            .With(2, Attr.LineNumber, "L-1")
            .With(4, Attr.LineNumber, "L-2");

        var issue = Assert.Single(LineNumberValidator.Validate(NetworkAnalyzer.Analyze(g.Build())));

        Assert.Equal(IssueKind.Unassigned, issue.Kind);
        Assert.Equal(new long[] { 3 }, issue.ElementIds);
    }

    [Fact]
    public void Run_with_several_numbers_is_mixed()
    {
        var g = GraphBuilder.TeeHeaderWithBranch()
            .With(1, Attr.LineNumber, "L-1")
            .With(2, Attr.LineNumber, "L-1")
            .With(3, Attr.LineNumber, "L-2")
            .With(4, Attr.LineNumber, "L-3");

        var issue = Assert.Single(LineNumberValidator.Validate(NetworkAnalyzer.Analyze(g.Build())));

        Assert.Equal(IssueKind.Mixed, issue.Kind);
        Assert.Equal(new long[] { 1, 2, 3 }, issue.ElementIds);
    }

    [Fact]
    public void Same_number_on_two_disconnected_runs_is_duplicate()
    {
        var g = new GraphBuilder().Pipe(1).Pipe(2).WithAll(Attr.LineNumber, "L-1");

        var issue = Assert.Single(LineNumberValidator.Validate(NetworkAnalyzer.Analyze(g.Build())));

        Assert.Equal(IssueKind.Duplicate, issue.Kind);
        Assert.Equal("L-1", issue.LineNumber);
        Assert.Equal(2, issue.ElementIds.Count);
    }
}

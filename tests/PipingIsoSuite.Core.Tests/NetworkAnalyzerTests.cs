using PipingIsoSuite.Core.Topology;

namespace PipingIsoSuite.Core.Tests;

public class NetworkAnalyzerTests
{
    [Fact]
    public void Straight_header_with_branch_is_one_network_of_two_runs_in_walk_order()
    {
        var network = Assert.Single(NetworkAnalyzer.Analyze(GraphBuilder.TeeHeaderWithBranch().Build()));

        Assert.Equal(2, network.Runs.Count);
        Assert.Equal(new long[] { 1, 2, 3 }, network.Header.ElementIds);
    }

    [Fact]
    public void Branch_is_child_of_header_at_depth_one()
    {
        var header = NetworkAnalyzer.Analyze(GraphBuilder.TeeHeaderWithBranch().Build()).Single().Header;
        var branch = Assert.Single(header.Children);

        Assert.Equal(new long[] { 4 }, branch.ElementIds);
        Assert.Same(header, branch.Parent);
        Assert.Equal(RunRole.Branch, branch.Role);
        Assert.Equal(1, branch.Depth);
    }

    [Fact]
    public void Preferred_header_element_makes_its_run_the_header()
    {
        var network = NetworkAnalyzer.Analyze(GraphBuilder.TeeHeaderWithBranch().Build(), new HashSet<long> { 4 }).Single();

        Assert.Equal(new long[] { 4 }, network.Header.ElementIds);
        var through = Assert.Single(network.Header.Children);
        Assert.Equal(new long[] { 1, 2, 3 }, through.ElementIds);
        Assert.Equal(RunRole.Branch, through.Role);
        Assert.Equal(1, through.Depth);
    }

    [Fact]
    public void Disconnected_pipes_form_separate_networks()
    {
        var networks = NetworkAnalyzer.Analyze(new GraphBuilder().Pipe(1).Pipe(2).Build());

        Assert.Equal(2, networks.Count);
    }

    [Fact]
    public void Reducing_tee_keeps_collinear_pair_as_through_even_when_branch_is_larger()
    {
        var nodes = new GraphBuilder()
            .Pipe(1).Tee(2, 0.5, 0.75).Pipe(3).Pipe(4, 0.75, axis: GraphBuilder.North)
            .Connect(1, 1, 2, 0).Connect(2, 1, 3, 0).Connect(2, 2, 4, 0)
            .Build();

        var network = NetworkAnalyzer.Analyze(nodes).Single();

        Assert.Equal(2, network.Runs.Count);
        Assert.Equal(new long[] { 1, 2, 3 }, network.Runs.Single(r => r.ElementIds.Contains(2L)).ElementIds);
    }

    [Fact]
    public void Cross_picks_larger_collinear_pair_as_through()
    {
        var nodes = new GraphBuilder()
            .Pipe(1).Cross(2, 0.5, 0.25).Pipe(3)
            .Pipe(4, 0.25, axis: GraphBuilder.North).Pipe(5, 0.25, axis: GraphBuilder.North)
            .Connect(1, 1, 2, 0).Connect(2, 1, 3, 0).Connect(2, 2, 4, 0).Connect(2, 3, 5, 0)
            .Build();

        var network = NetworkAnalyzer.Analyze(nodes).Single();

        Assert.Equal(3, network.Runs.Count);
        Assert.Equal(new long[] { 1, 2, 3 }, network.Runs.Single(r => r.ElementIds.Contains(2L)).ElementIds);
    }
}

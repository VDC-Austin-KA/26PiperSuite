using PipingIsoSuite.Core.Topology;

namespace PipingIsoSuite.Core.Tests;

public class LoopSignatureTests
{
    private static PipeRun Chain(double diameter) =>
        GraphBuilder.RunOf(new GraphBuilder().Pipe(1, diameter).Pipe(2, diameter).Build().ToArray());

    private static List<PipeRun> TwoIdenticalHeaders()
    {
        var nodes = GraphBuilder.TeeHeaderWithBranch(1).Build()
            .Concat(GraphBuilder.TeeHeaderWithBranch(11).Build());
        return NetworkAnalyzer.Analyze(nodes).Select(n => n.Header).ToList();
    }

    [Fact]
    public void Identical_networks_have_equal_signatures_and_group_together()
    {
        var headers = TwoIdenticalHeaders();

        Assert.Equal(LoopSignature.Compute(headers[0]), LoopSignature.Compute(headers[1]));
        var group = Assert.Single(LoopSignature.GroupIdentical(headers));
        Assert.Equal(2, group.Count());
    }

    [Fact]
    public void Reversed_node_order_has_same_signature()
    {
        var n = new GraphBuilder().Pipe(1, typeName: "A").Pipe(2, typeName: "A").Pipe(3, typeName: "B").Build();

        var forward = GraphBuilder.RunOf(n[0], n[1], n[2]);
        var reversed = GraphBuilder.RunOf(n[2], n[1], n[0]);

        Assert.Equal(LoopSignature.Compute(forward), LoopSignature.Compute(reversed));
    }

    [Fact]
    public void Different_diameter_changes_signature()
    {
        Assert.NotEqual(LoopSignature.Compute(Chain(0.5)), LoopSignature.Compute(Chain(0.75)));
    }

    [Fact]
    public void Pair_matches_header_and_branch_of_identical_networks()
    {
        var networks = NetworkAnalyzer.Analyze(
            GraphBuilder.TeeHeaderWithBranch(1).Build().Concat(GraphBuilder.TeeHeaderWithBranch(11).Build()));

        var pairs = LoopSignature.Pair(networks[0].Header, networks[1].Header);

        Assert.Equal(2, pairs.Count);
        Assert.Same(networks[0].Header, pairs[0].Source);
        Assert.Same(networks[1].Header, pairs[0].Target);
        Assert.Same(networks[0].Header.Children[0], pairs[1].Source);
        Assert.Same(networks[1].Header.Children[0], pairs[1].Target);
    }
}

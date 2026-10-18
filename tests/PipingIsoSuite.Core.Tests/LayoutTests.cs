using PipingIsoSuite.Core.Config;
using PipingIsoSuite.Core.Layout;

namespace PipingIsoSuite.Core.Tests;

public class GridLayoutTests
{
    private static readonly Rect Area = new(0, 0, 2, 2);

    [Fact]
    public void Row_major_fills_left_to_right_then_top_to_bottom()
    {
        var cells = GridLayout.CellCenters(Area, 2, 2, FillOrder.RowMajor);

        Assert.Equal(new[] { (0.5, 1.5), (1.5, 1.5), (0.5, 0.5), (1.5, 0.5) }, cells);
    }

    [Fact]
    public void Column_major_fills_top_to_bottom_then_left_to_right()
    {
        var cells = GridLayout.CellCenters(Area, 2, 2, FillOrder.ColumnMajor);

        Assert.Equal(new[] { (0.5, 1.5), (0.5, 0.5), (1.5, 1.5), (1.5, 0.5) }, cells);
    }

    [Fact]
    public void Inset_with_margins_larger_than_area_returns_outer()
    {
        var outer = new Rect(0, 0, 10, 10);

        Assert.Equal(outer, GridLayout.Inset(outer, 6, 6, 0, 0));
    }
}

public class IsoMathTests
{
    [Fact]
    public void Forward_and_up_are_orthogonal_unit_vectors_with_up_above_ground()
    {
        foreach (var orientation in Enum.GetValues<IsoOrientation>())
        {
            var (forward, up) = IsoMath.Directions(orientation);

            Assert.Equal(1.0, forward.Length, 9);
            Assert.Equal(1.0, up.Length, 9);
            Assert.True(Math.Abs(forward.Dot(up)) < 1e-9, orientation.ToString());
            Assert.True(up.Z > 0, orientation.ToString());
        }
    }

    [Fact]
    public void South_east_forward_looks_toward_negative_x_and_positive_y()
    {
        var (forward, _) = IsoMath.Directions(IsoOrientation.SouthEast);

        Assert.True(forward.X < 0 && forward.Y > 0);
    }
}

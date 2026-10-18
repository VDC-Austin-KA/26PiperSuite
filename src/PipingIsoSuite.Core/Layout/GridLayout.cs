using PipingIsoSuite.Core.Config;

namespace PipingIsoSuite.Core.Layout;

public readonly record struct Rect(double MinX, double MinY, double MaxX, double MaxY)
{
    public double Width => MaxX - MinX;
    public double Height => MaxY - MinY;
}

public static class GridLayout
{
    /// <summary>
    /// Cell centres for a rows x columns grid inside <paramref name="area"/> (Y up), in fill order
    /// starting top-left.
    /// </summary>
    public static IReadOnlyList<(double X, double Y)> CellCenters(Rect area, int rows, int columns, FillOrder order)
    {
        rows = Math.Max(1, rows); columns = Math.Max(1, columns);
        var w = area.Width / columns; var h = area.Height / rows;
        var cells = new List<(double, double)>(rows * columns);
        for (var a = 0; a < (order == FillOrder.RowMajor ? rows : columns); a++)
        for (var b = 0; b < (order == FillOrder.RowMajor ? columns : rows); b++)
        {
            var (r, c) = order == FillOrder.RowMajor ? (a, b) : (b, a);
            cells.Add((area.MinX + (c + 0.5) * w, area.MaxY - (r + 0.5) * h));
        }
        return cells;
    }

    /// <summary>Usable area of a title block after margins (all values in the same unit).</summary>
    public static Rect Inset(Rect outer, double left, double right, double top, double bottom)
    {
        var r = new Rect(outer.MinX + left, outer.MinY + bottom, outer.MaxX - right, outer.MaxY - top);
        return r.Width > 0 && r.Height > 0 ? r : outer;
    }
}

public static class IsoMath
{
    /// <summary>Forward (eye toward target) and up vectors for a standard 3D isometric.</summary>
    public static (Topology.Vec3 Forward, Topology.Vec3 Up) Directions(IsoOrientation orientation)
    {
        // Eye sits at the named corner looking down toward the model centre.
        var (ex, ey) = orientation switch
        {
            IsoOrientation.SouthEast => (1.0, -1.0),
            IsoOrientation.SouthWest => (-1.0, -1.0),
            IsoOrientation.NorthEast => (1.0, 1.0),
            _ => (-1.0, 1.0),
        };
        var forward = new Topology.Vec3(-ex, -ey, -1).Normalize();
        var right = forward.Cross(new Topology.Vec3(0, 0, 1));
        return (forward, right.Cross(forward).Normalize());
    }
}

namespace PipingIsoSuite.Core.Topology;

public enum NodeKind { Pipe, Fitting, Accessory, Other }

public readonly record struct Vec3(double X, double Y, double Z)
{
    public double Dot(Vec3 o) => X * o.X + Y * o.Y + Z * o.Z;
    public Vec3 Cross(Vec3 o) => new(Y * o.Z - Z * o.Y, Z * o.X - X * o.Z, X * o.Y - Y * o.X);
    public double Length => Math.Sqrt(Dot(this));
    public Vec3 Normalize() { var l = Length; return l < 1e-12 ? this : new(X / l, Y / l, Z / l); }
}

/// <summary>Well-known attribute keys populated by the Revit graph builder.</summary>
public static class Attr
{
    public const string System = "System";         // system abbreviation (e.g. CHWS)
    public const string SystemType = "SystemType"; // system type name
    public const string SystemName = "SystemName"; // piping system instance name
    public const string Level = "Level";
    public const string Zone = "Zone";
    public const string LineNumber = "LineNumber";
}

/// <summary>A physical connector on a node. Direction points out of the element.</summary>
public sealed class PipePort
{
    public long? ConnectedTo { get; init; }
    public double Diameter { get; init; }
    public Vec3? Direction { get; init; }
}

/// <summary>A Revit-agnostic piping element (pipe, fitting, accessory) and its connectors.</summary>
public sealed class PipeNode
{
    public required long Id { get; init; }
    public NodeKind Kind { get; init; }
    public string TypeName { get; init; } = "";
    public double Length { get; init; }
    public List<PipePort> Ports { get; init; } = new();
    public Dictionary<string, string> Attributes { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public double Diameter => Ports.Count == 0 ? 0 : Ports.Max(p => p.Diameter);
    public string Get(string key) => Attributes.TryGetValue(key, out var v) ? v : "";
}

public enum RunRole { Header, Branch }

/// <summary>A straight-through chain of elements: either a header or a branch.</summary>
public sealed class PipeRun
{
    public int Id { get; internal set; }
    public List<PipeNode> Nodes { get; } = new();
    public RunRole Role { get; internal set; }
    public PipeRun? Parent { get; internal set; }
    public List<PipeRun> Children { get; } = new();
    public int Depth { get; internal set; }
    public PipeNetwork Network { get; internal set; } = null!;

    public double MaxDiameter => Nodes.Count == 0 ? 0 : Nodes.Max(n => n.Diameter);
    public double TotalLength => Nodes.Where(n => n.Kind == NodeKind.Pipe).Sum(n => n.Length);
    public IEnumerable<long> ElementIds => Nodes.Select(n => n.Id);

    /// <summary>Node whose attributes describe the run (first pipe, else first node).</summary>
    public PipeNode Representative => Nodes.FirstOrDefault(n => n.Kind == NodeKind.Pipe) ?? Nodes[0];

    /// <summary>
    /// The one line number carried by the run's numbered elements (unnumbered ones, e.g. newly
    /// drawn pipe, are ignored so they inherit it), or null when none or several are present.
    /// </summary>
    public string? ExistingLineNumber
    {
        get
        {
            var values = Nodes.Select(n => n.Get(Attr.LineNumber)).Where(v => v.Length > 0)
                              .Distinct(StringComparer.OrdinalIgnoreCase).Take(2).ToList();
            return values.Count == 1 ? values[0] : null;
        }
    }

    public IEnumerable<PipeRun> SelfAndDescendants()
    {
        yield return this;
        foreach (var c in Children)
            foreach (var d in c.SelfAndDescendants())
                yield return d;
    }
}

/// <summary>A connected set of runs rooted at one header.</summary>
public sealed class PipeNetwork
{
    public int Id { get; internal set; }
    public PipeRun Header { get; internal set; } = null!;
    /// <summary>Runs in breadth-first order (header first, parents before children).</summary>
    public List<PipeRun> Runs { get; } = new();
    public string SystemName => Header.Representative.Get(Attr.SystemName) is { Length: > 0 } s ? s : Header.Representative.Get(Attr.System);
}

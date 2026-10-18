using PipingIsoSuite.Core.Export;

namespace PipingIsoSuite.Core.Tests;

public sealed class EzIsoExporterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "PipingIsoSuite.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void WriteCsv_writes_stable_header_and_quotes_cells_with_commas_and_quotes()
    {
        var record = new IsoRecord("L-1", "Header", "", "CHW", "L1", "", 12.5, 3.2, 4, "abc123",
            "ISO, \"A\"", "ISO-001", new long[] { 1, 2 });
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "iso.csv");

        EzIsoExporter.WriteCsv(path, new[] { record });
        var lines = File.ReadAllLines(path);

        Assert.Equal("LineNumber,Role,ParentLineNumber,System,Level,Zone,MaxDiameterMm,PipeLengthM,ElementCount,LoopSignature,ViewName,SheetNumber,ElementIds", lines[0]);
        Assert.Equal("L-1,Header,,CHW,L1,,12.5,3.2,4,abc123,\"ISO, \"\"A\"\"\",ISO-001,1;2", lines[1]);
    }

    [Fact]
    public void Escape_wraps_values_with_comma_or_quote_and_doubles_quotes()
    {
        Assert.Equal("\"ISO, \"\"A\"\"\"", EzIsoExporter.Escape("ISO, \"A\""));
        Assert.Equal("plain", EzIsoExporter.Escape("plain"));
    }
}

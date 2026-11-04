using System.Text;
using FluentAssertions;
using HMISync.Web.Production;

namespace HMISync.Tests.Production;

public sealed class BatchDxfLayerTests
{
    internal static string Line(string layer, string handle = "10") => $"0\nLINE\n5\n{handle}\n8\n{layer}\n10\n0\n20\n0\n11\n1\n21\n1\n";
    internal static byte[] Dxf(string entities) => Encoding.Latin1.GetBytes($"0\nSECTION\n2\nENTITIES\n{entities}0\nENDSEC\n0\nEOF\n");

    [Fact]
    public void RemovesOnlyBendsByDefault_AndPreservesAllOtherBytes()
    {
        var cut = Line("SHEETMETAL_CUT_LINES");
        var other = Line("COUNTERSINK", "11") + Line("GRAIN", "12");
        var result = BatchDxfLayers.Prepare(Dxf(cut + Line("SHEETMETAL_BEND_LINES_UP", "13") + other), []);
        result.Bytes.Should().Equal(Dxf(cut + other));
        result.Layers.Single(l => l.Name == "SHEETMETAL_BEND_LINES_UP").Removed.Should().BeTrue();
        result.Layers.Single(l => l.Name == "COUNTERSINK").RequiresReview.Should().BeTrue();
        result.Layers.Single(l => l.Name == "GRAIN").Removed.Should().BeFalse();
        result.Layers.Single(l => l.Name == "SHEETMETAL_CUT_LINES").RequiresReview.Should().BeFalse();
    }

    [Fact]
    public void ExplicitRemoval_IsExact_NotSubstring_AndDoesNotChangeCutGeometry()
    {
        var kept = Line("0") + Line("COUNTERSINK_OUTLINE", "11") + Line("BENDER", "12");
        var result = BatchDxfLayers.Prepare(Dxf(kept + Line("COUNTERSINK", "13")), ["COUNTERSINK"]);
        result.Bytes.Should().Equal(Dxf(kept));
        result.Layers.Single(l => l.Name == "BENDER").Removed.Should().BeFalse();
        Action action = () => BatchDxfLayers.Prepare(Dxf(kept), ["0"]);
        action.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void PolylineRemoval_IncludesVerticesAndSequenceEnd()
    {
        var poly = "0\nPOLYLINE\n8\nBEND\n66\n1\n0\nVERTEX\n8\n0\n10\n0\n20\n0\n0\nSEQEND\n";
        BatchDxfLayers.Prepare(Dxf(Line("0") + poly), []).Bytes.Should().Equal(Dxf(Line("0")));
    }

    [Theory]
    [InlineData("0\nINSERT\n8\n0\n2\nBLOCK1\n")]
    [InlineData("0\nPOLYLINE\n8\nBEND\n0\nVERTEX\n8\n0\n")]
    [InlineData("0\nVERTEX\n8\nBEND\n")]
    [InlineData("0\nLINE\n8\nBEND\n")]
    public void RejectsUnsupportedCompoundMalformedOrEmptyCutOutput(string entities)
    {
        Action action = () => BatchDxfLayers.Prepare(Dxf(entities), []);
        action.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void RejectsRemovalOfExternallyReferencedEntity()
    {
        var bytes = Encoding.Latin1.GetBytes(Encoding.Latin1.GetString(Dxf(Line("0") + Line("BEND", "11")))
            .Replace("0\nEOF", "0\nSECTION\n2\nOBJECTS\n0\nXRECORD\n330\n11\n0\nENDSEC\n0\nEOF"));
        Action action = () => BatchDxfLayers.Prepare(bytes, []);
        action.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void PreservesCrLfEncodingTablesAndUnsupportedRetainedEntities()
    {
        var source = Encoding.Latin1.GetString(Dxf(Line("0") + "0\nTEXT\n8\nNOTES\n1\ncaf\u00e9\n"))
            .Replace("\n", "\r\n");
        var bytes = Encoding.Latin1.GetBytes(source);
        BatchDxfLayers.Prepare(bytes, []).Bytes.Should().Equal(bytes);
        Action action = () => BatchDxfLayers.Prepare(Encoding.ASCII.GetBytes("AutoCAD Binary DXF\0"), []);
        action.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void EmptyLayerTablesDoNotBecomeWarnings_AndUnusedBlocksStayByteIdentical()
    {
        var cut = Dxf(Line("0"));
        var prefix = "0\nSECTION\n2\nTABLES\n0\nTABLE\n2\nLAYER\n0\nLAYER\n2\nCOUNTERSINK\n0\nENDTAB\n0\nENDSEC\n" +
            "0\nSECTION\n2\nBLOCKS\n0\nBLOCK\n2\nUNUSED\n" + Line("BEND", "99") + "0\nENDBLK\n0\nENDSEC\n";
        var bytes = Encoding.Latin1.GetBytes(prefix + Encoding.Latin1.GetString(cut));
        var result = BatchDxfLayers.Prepare(bytes, []);
        result.Bytes.Should().Equal(bytes);
        result.Layers.Should().ContainSingle(l => l.Name == "0");
    }

    [Fact]
    public void CannotExportOnlyGrainAfterRemovingBends()
    {
        Action action = () => BatchDxfLayers.Prepare(Dxf(Line("GRAIN") + Line("BEND", "11")), []);
        action.Should().Throw<InvalidDataException>();
    }
}

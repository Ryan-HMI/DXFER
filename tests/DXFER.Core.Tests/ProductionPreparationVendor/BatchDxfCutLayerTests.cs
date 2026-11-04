using System.Text;
using FluentAssertions;
using HMISync.Web.Production;

namespace HMISync.Tests.Production;

public sealed class BatchDxfCutLayerTests
{
    private static byte[] Export(byte[] source, params string[] excluded) =>
        BatchDxfCutLayers.Consolidate(BatchDxfLayers.Prepare(source, excluded).Bytes);

    [Fact]
    public void ConsolidationRunsAfterRemovalAndSlotConversionWithoutChangingSourceOrGeometry()
    {
        var source = BatchDxfLayerTests.Dxf(
            BatchDxfTearSlotTests.Line("SHEETMETAL_CUT_LINES", -5, -5, 10, -5) +
            BatchDxfTearSlotTests.Line("SHEETMETAL_FORM_TEAR", 0, 0, 3, 0) +
            BatchDxfTearSlotTests.Line("SHEETMETAL_FORM_UP", 0, 0, 1, 1) +
            BatchDxfTearSlotTests.Line("SHEETMETAL_FORM_CENTERMARK", 0, 0, 1, 1) +
            BatchDxfTearSlotTests.Line("BEND", 0, 0, 3, 0) +
            BatchDxfTearSlotTests.Line("COUNTERBORES", 20, 20, 21, 21));
        var original = source.ToArray();
        var cleaned = BatchDxfLayers.Prepare(source, ["COUNTERBORES"]);
        var output = BatchDxfCutLayers.Consolidate(cleaned.Bytes);

        cleaned.TearSlotsCreated.Should().Be(1);
        var expected = Encoding.Latin1.GetString(cleaned.Bytes).Replace("8\nSHEETMETAL_CUT_LINES\n", "8\nCUT\n");
        Encoding.Latin1.GetString(output).Should().EndWith(expected)
            .And.NotContain("SHEETMETAL_FORM").And.NotContain("BEND").And.NotContain("COUNTERBORES");
        BatchDxfCutLayers.Consolidate(output).Should().Equal(output);
        source.Should().Equal(original);
    }

    [Fact]
    public void ExistingSlotOnMixedLayersKeepsAllFourEdges()
    {
        var source = BatchDxfLayerTests.Dxf(BatchDxfTearSlotTests.Line("CUT", -5, -5, 10, -5) +
            BatchDxfTearSlotTests.Line("SHEETMETAL_FORM_TEAR", 0, 0, 3, 0) +
            BatchDxfTearSlotTests.Line("0", 0, -.04, 3, -.04) +
            BatchDxfTearSlotTests.Arc("0", 0, -.02, 90, 270) + BatchDxfTearSlotTests.Arc("0", 3, -.02, 270, 90));
        var cleaned = BatchDxfLayers.Prepare(source, []);
        cleaned.ExistingTearSlots.Should().Be(1);
        var expected = Encoding.Latin1.GetString(source).Replace("8\nSHEETMETAL_FORM_TEAR\n", "8\nCUT\n")
            .Replace("8\n0\n", "8\nCUT\n");

        Encoding.Latin1.GetString(BatchDxfCutLayers.Consolidate(cleaned.Bytes)).Should().EndWith(expected);
    }

    [Fact]
    public void TextIsNeverPromotedToCutEvenWhenItsSourceLayerIsCut()
    {
        var source = BatchDxfLayerTests.Dxf(BatchDxfLayerTests.Line("OUTLINE") +
            "0\nTEXT\n8\nCUT\n1\nn=P_Rev_A\n0\nMTEXT\n8\nNOTES\n1\nm=SS-304 t=0.075 q=2\n");
        var output = Encoding.Latin1.GetString(Export(source));
        output.Should().Contain("0\nTEXT\n8\nHMI_IMPORT_TEXT_NOT_CUT\n1\nn=P_Rev_A\n")
            .And.Contain("0\nMTEXT\n8\nHMI_IMPORT_TEXT_NOT_CUT\n1\nm=SS-304 t=0.075 q=2\n");
        output.Should().Contain("0\nLINE\n5\n10\n8\nCUT\n");
    }

    [Fact]
    public void RetainedUnusedBlocksAndLayerMetadataAreNotRewrittenOrPromoted()
    {
        var metadata = "0\nSECTION\n2\nTABLES\n0\nTABLE\n2\nLAYER\n70\n1\n" +
            "0\nLAYER\n2\nSOURCE\n70\n0\n62\n2\n6\nContinuous\n1001\nSTOCK\n1000\nSS-304\n1040\n0.075\n";
        var blocks = "0\nSECTION\n2\nBLOCKS\n0\nBLOCK\n2\nUNUSED\n" +
            BatchDxfLayerTests.Line("BEND", "99") + "0\nENDBLK\n0\nENDSEC\n";
        var source = Encoding.Latin1.GetBytes(metadata + "0\nENDTAB\n0\nENDSEC\n" + blocks +
            Encoding.Latin1.GetString(BatchDxfLayerTests.Dxf(BatchDxfLayerTests.Line("SOURCE"))));
        var output = Encoding.Latin1.GetString(Export(source));

        output.Should().Contain(metadata.Replace("70\n1\n", "70\n2\n")).And.Contain(blocks);
        output.Should().EndWith(Encoding.Latin1.GetString(BatchDxfLayerTests.Dxf(BatchDxfLayerTests.Line("CUT"))));
        Action insert = () => Export(BatchDxfLayerTests.Dxf(BatchDxfLayerTests.Line("0") + "0\nINSERT\n2\nUNUSED\n8\n0\n"));
        insert.Should().Throw<InvalidDataException>().WithMessage("*Block references*");
    }

    [Fact]
    public void PolylineSequenceAndMissingLayersAreConsolidatedWithoutChangingCoordinates()
    {
        var poly = "0\nPOLYLINE\n8\nPROFILE\n66\n1\n0\nVERTEX\n8\n0\n10\n0\n20\n0\n" +
            "0\nVERTEX\n8\n0\n10\n2\n20\n2\n0\nSEQEND\n";
        var source = BatchDxfLayerTests.Dxf(poly + "0\nLINE\n10\n4\n20\n4\n11\n5\n21\n5\n");
        var output = Encoding.Latin1.GetString(Export(source));
        output.Should().Contain(poly.Replace("8\nPROFILE\n", "8\nCUT\n").Replace("8\n0\n", "8\nCUT\n")
            .Replace("0\nSEQEND\n", "0\nSEQEND\n8\nCUT\n"));
        output.Should().Contain("0\nLINE\n8\nCUT\n10\n4\n20\n4\n11\n5\n21\n5\n");
    }

    [Fact]
    public void CrLfAndLegacyTextEncodingSurvive()
    {
        var source = Encoding.Latin1.GetBytes(Encoding.Latin1.GetString(BatchDxfLayerTests.Dxf(
            BatchDxfLayerTests.Line("0") + "0\nTEXT\n8\nNOTES\n1\ncaf\u00e9\n")).Replace("\n", "\r\n"));
        var output = Encoding.Latin1.GetString(Export(source));
        output.Should().Contain("8\r\nCUT\r\n").And.Contain("1\r\ncaf\u00e9\r\n");
        output.Replace("\r\n", "").Should().NotContain("\n");
    }

    [Fact]
    public void RetainedNonCutGeometryAndUnsupportedEntitiesAreNeverPromoted()
    {
        var grain = BatchDxfLayerTests.Line("GRAIN_DIRECTION", "11") +
            "0\nPOLYLINE\n8\nGRAIN\n66\n1\n0\nVERTEX\n8\n0\n10\n0\n20\n0\n0\nSEQEND\n";
        var other = "0\nPOINT\n8\nDATUM\n10\n1\n20\n2\n0\nHATCH\n8\nNOTES\n";
        var output = Encoding.Latin1.GetString(Export(BatchDxfLayerTests.Dxf(BatchDxfLayerTests.Line("0") + grain + other)));

        output.Should().Contain(grain + other).And.Contain("0\nLINE\n5\n10\n8\nCUT\n");
    }

    [Fact]
    public void GeometryUsingReservedAnnotationLayerIsHeldRatherThanReclassifiedAsCut()
    {
        Action export = () => Export(BatchDxfLayerTests.Dxf(BatchDxfLayerTests.Line("0") +
            BatchDxfLayerTests.Line("hmi_import_text_not_cut", "11")));

        export.Should().Throw<InvalidDataException>().WithMessage("*reserved non-cut*");
    }
}

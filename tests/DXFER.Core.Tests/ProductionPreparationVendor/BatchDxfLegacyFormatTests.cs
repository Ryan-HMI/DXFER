using System.Text;
using FluentAssertions;
using HMISync.Web.Production;

namespace HMISync.Tests.Production;

public sealed class BatchDxfLegacyFormatTests
{
    private const string Curve = "70\n8\n71\n2\n72\n6\n73\n3\n74\n0\n40\n0\n40\n0\n40\n0\n40\n1\n40\n1\n40\n1\n10\n0.753358\n20\n0.1723491068\n10\n0.9\n20\n0.1\n10\n1.080338105\n20\n0\n";
    private const string Spline = "0\nSPLINE\n5\nspline-100001C6\n8\nSHEETMETAL_CUT_LINES\n" + Curve;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepairsLegacyMetadataWithoutChangingSplineParameters(bool header)
    {
        var source = BatchDxfLayerTests.Dxf(Spline + BatchDxfLayerTests.Line("BEND", "line-old"));
        if (header) source = Encoding.ASCII.GetBytes("0\nSECTION\n2\nHEADER\n9\n$INSUNITS\n70\n1\n0\nENDSEC\n" + Encoding.ASCII.GetString(source));
        var original = source.ToArray();
        var result = BatchDxfLayers.Prepare(source, []);
        var text = Encoding.ASCII.GetString(result.Bytes);
        text.Should().Contain("9\n$ACADVER\n1\nAC1015\n");
        WithoutZeroZ(text).Should().Contain("0\nSPLINE\n5\n100\n330\n2\n100\nAcDbEntity\n8\nSHEETMETAL_CUT_LINES\n100\nAcDbSpline\n" + Curve);
        text.Should().Contain("2\nBLOCK_RECORD\n").And.Contain("2\nBLOCKS\n").And.Contain("2\n*Model_Space\n").And.Contain("2\n*Paper_Space\n");
        text.Should().NotContain("line-old").And.NotContain("spline-100001C6").And.NotContain("8\nBEND\n");
        source.Should().Equal(original);
        BatchDxfLayers.Prepare(result.Bytes, []).Bytes.Should().Equal(result.Bytes);
        if (header) text.Should().Contain("9\n$INSUNITS\n70\n1\n");
    }

    [Theory]
    [InlineData("330\nline-old\n")]
    [InlineData("100\nAcDbSpline\n")]
    [InlineData("102\n{ACAD_REACTORS\n")]
    [InlineData("1001\nCUSTOM_APP\n")]
    public void RefusesMixedOrReferencedLegacyMetadata(string extra)
    {
        Action action = () => BatchDxfLayers.Prepare(BatchDxfLayerTests.Dxf(Spline + extra), []);
        action.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void NormalDxfIsNotRewritten()
    {
        var bytes = BatchDxfLayerTests.Dxf(Spline.Replace("spline-100001C6", "ABC"));
        BatchDxfLayers.Prepare(bytes, []).Bytes.Should().Equal(bytes);
    }

    [Fact]
    public void LegacyTablesAndEntitiesGetUniqueHandles_AndSlotExportRemainsIdempotent()
    {
        var prefix = "0\nSECTION\n2\nTABLES\n0\nTABLE\n2\nLAYER\n70\n1\n0\nLAYER\n2\nSHEETMETAL_CUT_LINES\n70\n0\n62\n7\n6\nCONTINUOUS\n0\nENDTAB\n0\nENDSEC\n";
        var bytes = Encoding.ASCII.GetBytes(prefix + Encoding.ASCII.GetString(BatchDxfLayerTests.Dxf(Spline +
            BatchDxfTearSlotTests.Line("SHEETMETAL_FORM_TEAR", 10, 10, 13, 10))));
        var result = BatchDxfLayers.Prepare(bytes, []);
        result.TearSlotsCreated.Should().Be(1);
        var lines = Encoding.ASCII.GetString(result.Bytes).Split('\n');
        var handles = Enumerable.Range(0, (lines.Length - 1) / 2).Where(i => lines[2 * i] == "5").Select(i => lines[2 * i + 1]).ToArray();
        handles.Should().HaveCount(14).And.OnlyHaveUniqueItems();
        handles.Should().OnlyContain(h => h.All(char.IsAsciiHexDigit));
        WithoutZeroZ(Encoding.ASCII.GetString(result.Bytes)).Should().Contain("100\nAcDbSymbolTable\n").And.Contain("100\nAcDbSpline\n" + Curve);
        BatchDxfLayers.Prepare(result.Bytes, []).Bytes.Should().Equal(result.Bytes);
    }

    [Fact]
    public void LegacyUnsupportedGeometryIsHeldRatherThanDropped()
    {
        Action action = () => BatchDxfLayers.Prepare(BatchDxfLayerTests.Dxf(Spline + "0\nHATCH\n8\n0\n"), []);
        action.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void RepairsPriorAc1015WriterMissingSpaceRecords_WithoutHandleCollisions()
    {
        var header = "0\nSECTION\n2\nHEADER\n9\n$ACADVER\n1\nAC1015\n0\nENDSEC\n";
        var spline = Spline.Replace("5\nspline-100001C6\n", "5\n2\n100\nAcDbEntity\n").Replace("70\n8\n", "100\nAcDbSpline\n70\n8\n");
        var bytes = Encoding.ASCII.GetBytes(header + Encoding.ASCII.GetString(BatchDxfLayerTests.Dxf(spline)));
        var result = BatchDxfLayers.Prepare(bytes, []);
        var text = Encoding.ASCII.GetString(result.Bytes);
        text.Should().Contain("0\nSPLINE\n5\n100\n330\n2\n");
        WithoutZeroZ(text).Should().Contain("100\nAcDbSpline\n" + Curve);
        BatchDxfLayers.Prepare(result.Bytes, []).Bytes.Should().Equal(result.Bytes);
    }

    private static string WithoutZeroZ(string text) => text.Replace("30\n0\n", "").Replace("31\n0\n", "");
}

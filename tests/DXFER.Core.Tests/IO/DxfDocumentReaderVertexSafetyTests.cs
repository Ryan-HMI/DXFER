using DXFER.CadIO;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Operations;
using FluentAssertions;

namespace DXFER.Core.Tests.IO;

public sealed class DxfDocumentReaderVertexSafetyTests
{
    private const string Line = "0\nLINE\n10\n0\n20\n0\n30\n0\n11\n10\n21\n0\n31\n0\n";
    private const string First = "10\n0\n20\n0\n";
    private const string Last = "10\n2\n20\n2\n";

    [Theory]
    [InlineData(10, "0")]
    [InlineData(10, "100")]
    [InlineData(20, "100")]
    [InlineData(30, "0")]
    [InlineData(11, "100")]
    [InlineData(21, "100")]
    [InlineData(31, "0")]
    public void DuplicateLineCoordinatesAreRejectedEvenWhenValuesAgree(int code, string value)
    {
        AssertRejected(Line + $"{code}\n{value}\n");
    }

    [Theory]
    [InlineData("10\n0\n10\n1\n20\n0\n20\n1\n10\n2\n20\n2\n")]
    [InlineData("20\n9\n" + First + Last)]
    [InlineData(First + Last + "10\n9\n")]
    [InlineData(First + "10\n1\n10\n1\n20\n1\n" + Last)]
    [InlineData(First + "10\n1\n20\n1\n20\n1\n" + Last)]
    [InlineData(First + "10\nbad\n20\n1\n" + Last)]
    [InlineData(First + "10\n1\n20\nNaN\n" + Last)]
    [InlineData(First + "10\n1\n" + Last)]
    public void LightweightPolylineCannotDropOrMergeMalformedVertices(string coordinates)
    {
        AssertRejected("0\nLWPOLYLINE\n" + coordinates);
    }

    [Theory]
    [InlineData("90\n3\n")]
    [InlineData("90\n2\n90\n2\n")]
    [InlineData("90\nbad\n")]
    public void LightweightPolylineDeclaredCountMustBeUnambiguousAndMatchDecodedVertices(string count)
    {
        AssertRejected("0\nLWPOLYLINE\n" + count + First + Last);
    }

    [Theory]
    [InlineData("10\n1\n10\n1\n20\n1\n")]
    [InlineData("10\n1\n20\n1\n20\n1\n")]
    [InlineData("10\n1\n20\n1\n30\n0\n30\n0\n")]
    [InlineData("20\n1\n10\n1\n")]
    [InlineData("10\n1\n")]
    [InlineData("20\n1\n")]
    [InlineData("10\nbad\n20\n1\n")]
    [InlineData("10\n1\n20\nInfinity\n")]
    [InlineData("10\n1\n20\n1\n10\n3\n20\n3\n")]
    [InlineData("10\n1\n0\nVERTEX\n20\n1\n")]
    public void ClassicPolylineRequiresExactlyOneCompleteOrderedPointPerVertex(string vertex)
    {
        AssertRejected("0\nPOLYLINE\n10\n0\n20\n0\n0\nVERTEX\n" + First
            + "0\nVERTEX\n" + vertex + "0\nVERTEX\n" + Last + "0\nSEQEND\n");
    }

    [Fact]
    public void UnterminatedClassicSequenceIsRejectedWithoutConsumingFollowingEntity()
    {
        AssertRejected("0\nPOLYLINE\n0\nVERTEX\n" + First + "0\nVERTEX\n" + Last);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ValidVertexRecordsPreserveCoordinatesAndHeaderClosure(bool classic, bool closed)
    {
        var header = closed ? "70\n1\n" : "70\n0\n";
        var source = classic
            ? "0\nPOLYLINE\n" + header + "10\n0\n20\n0\n0\nVERTEX\n" + First + "30\n0\n70\n0\n0\nVERTEX\n" + Last + "30\n0\n0\nSEQEND\n"
            : "0\nLWPOLYLINE\n90\n2\n" + header + First + Last;
        var document = DxfDocumentReader.Read(Dxf(source));
        var polyline = document.Entities.Should().ContainSingle().Which.Should().BeOfType<PolylineEntity>().Subject;

        polyline.Vertices.Should().Equal(closed
            ? new[] { new Point2(0, 0), new Point2(2, 2), new Point2(0, 0) }
            : new[] { new Point2(0, 0), new Point2(2, 2) });
        document.Metadata.Warnings.Should().NotContain(warning => warning.Code == CutPathLengthService.UnsafeImportCode);
    }

    [Fact]
    public void RepeatedCompleteVerticesAreNotMistakenForDuplicateCoordinateFields()
    {
        var document = DxfDocumentReader.Read(Dxf("0\nLWPOLYLINE\n90\n3\n" + First + First + Last));
        document.Entities.Should().ContainSingle().Which.Should().BeOfType<PolylineEntity>()
            .Which.Vertices.Should().Equal(new Point2(0, 0), new Point2(0, 0), new Point2(2, 2));
        document.Metadata.Warnings.Should().NotContain(warning => warning.Code == CutPathLengthService.UnsafeImportCode);
    }

    [Theory]
    [InlineData("0\nLINE\n8\nGRAIN\n10\n0\n10\n1\n20\n0\n11\n2\n21\n0\n")]
    [InlineData("0\nLWPOLYLINE\n8\nGRAIN\n" + First + "10\n1\n" + Last)]
    public void NonCutLayerDoesNotHideMalformedSupportedGeometry(string malformed)
    {
        AssertRejected(malformed);
    }

    private static void AssertRejected(string malformed)
    {
        var document = DxfDocumentReader.Read(Dxf(Line + malformed + Line));
        document.Entities.Should().HaveCount(2).And.OnlyContain(entity => entity is LineEntity);
        document.Metadata.Warnings.Should().Contain(warning => warning.Code == CutPathLengthService.UnsafeImportCode);
        var reopened = DxfDocumentReader.Read(DxfDocumentWriter.Write(document));
        reopened.Metadata.Warnings.Should().Contain(warning => warning.Code == CutPathLengthService.UnsafeImportCode);
    }

    private static string Dxf(string entities) => $"0\nSECTION\n2\nENTITIES\n{entities}0\nENDSEC\n0\nEOF\n";
}

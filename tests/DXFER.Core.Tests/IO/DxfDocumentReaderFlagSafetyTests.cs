using DXFER.CadIO;
using DXFER.Core.Documents;
using DXFER.Core.Operations;
using FluentAssertions;

namespace DXFER.Core.Tests.IO;

public sealed class DxfDocumentReaderFlagSafetyTests
{
    private const string Line = "0\nLINE\n10\n0\n20\n0\n11\n10\n21\n0\n";
    private const string SplineDefinition = "71\n2\n72\n6\n73\n3\n74\n0\n"
        + "40\n0\n40\n0\n40\n0\n40\n1\n40\n1\n40\n1\n"
        + "10\n0\n20\n0\n10\n1\n20\n2\n10\n3\n20\n0\n";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PolylineHeaderFlagsMustBeOneIntegerWhenPresent(bool classic)
    {
        foreach (var flags in new[] { "70\n1\n70\n0\n", "70\n1\n70\n1\n", "70\n1.5\n", "70\nbad\n", "70\n2147483648\n" })
            AssertRejected(Polyline(classic, flags));
    }

    [Theory]
    [InlineData("70\n1\n")]
    [InlineData("70\n2\n")]
    [InlineData("70\n9\n")]
    [InlineData("70\n10\n")]
    [InlineData("70\n15\n")]
    [InlineData("70\n8\n70\n12\n")]
    [InlineData("70\n8.5\n")]
    public void UnsupportedOrAmbiguousSplineFlagsCannotLoseIntentOnExport(string flags)
    {
        AssertRejected("0\nSPLINE\n" + flags + SplineDefinition);
    }

    [Fact]
    public void MissingAndSupportedFlagsPreserveExistingGeometry()
    {
        foreach (var classic in new[] { false, true })
        foreach (var flags in new[] { "", "70\n0\n", "70\n1\n" })
        {
            var document = DxfDocumentReader.Read(Dxf(Polyline(classic, flags)));
            var polyline = document.Entities.Should().ContainSingle().Which.Should().BeOfType<PolylineEntity>().Subject;
            polyline.Vertices.Should().HaveCount(flags == "70\n1\n" ? 3 : 2);
            document.Metadata.Warnings.Should().NotContain(warning => warning.Code == CutPathLengthService.UnsafeImportCode);
        }

        foreach (var flags in new[] { "", "70\n8\n", "70\n12\n" })
        {
            var document = DxfDocumentReader.Read(Dxf("0\nSPLINE\n" + flags + SplineDefinition));
            document.Entities.Should().ContainSingle().Which.Should().BeOfType<SplineEntity>();
            document.Metadata.Warnings.Should().NotContain(warning => warning.Code == CutPathLengthService.UnsafeImportCode);
        }
    }

    private static void AssertRejected(string entity)
    {
        var document = DxfDocumentReader.Read(Dxf(Line + entity + Line));
        document.Entities.Should().HaveCount(2).And.OnlyContain(item => item is LineEntity);
        document.Metadata.Warnings.Should().Contain(warning => warning.Code == CutPathLengthService.UnsafeImportCode);
        DxfDocumentReader.Read(DxfDocumentWriter.Write(document)).Metadata.Warnings.Should()
            .Contain(warning => warning.Code == CutPathLengthService.UnsafeImportCode);
    }

    private static string Polyline(bool classic, string flags) => classic
        ? "0\nPOLYLINE\n" + flags + "0\nVERTEX\n10\n0\n20\n0\n70\n0\n0\nVERTEX\n10\n2\n20\n2\n70\n0\n0\nSEQEND\n"
        : "0\nLWPOLYLINE\n90\n2\n" + flags + "10\n0\n20\n0\n10\n2\n20\n2\n";

    private static string Dxf(string entities) => $"0\nSECTION\n2\nENTITIES\n{entities}0\nENDSEC\n0\nEOF\n";
}

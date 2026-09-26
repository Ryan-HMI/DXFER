using DXFER.CadIO;
using DXFER.Core.Documents;
using DXFER.Core.Operations;
using FluentAssertions;

namespace DXFER.Core.Tests.IO;

public sealed class DxfCutLengthTests
{
    private const string Line = "0\nLINE\n10\n0\n20\n0\n11\n25.4\n21\n0\n";

    [Theory]
    [InlineData(1, 25.4)]
    [InlineData(4, 1)]
    public void InsunitsSurviveNormalizeAndExport(int units, double inches)
    {
        var document = DxfDocumentReader.Read(Dxf(Line, units));
        var normalized = DrawingNormalizationService.AutoNormalize(document).NormalizedDocument;
        var result = CutPathLengthService.Calculate(DxfDocumentReader.Read(DxfDocumentWriter.Write(normalized)));
        result.CutLengthInches.Should().BeApproximately((decimal)inches, 0.000001m);
        result.CutLengthReviewReason.Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(99)]
    public void UnitlessOrUnsupportedUnitsRequireReview(int units)
    {
        var result = CutPathLengthService.Calculate(DxfDocumentReader.Read(Dxf(Line, units)));
        result.CutLengthInches.Should().BeNull();
        result.CutLengthReviewReason.Should().Contain("units");
    }

    [Theory]
    [InlineData("0\nLWPOLYLINE\n90\n3\n70\n1\n10\n0\n20\n0\n10\n3\n20\n0\n10\n3\n20\n4\n")]
    [InlineData("0\nPOLYLINE\n70\n1\n0\nVERTEX\n10\n0\n20\n0\n0\nVERTEX\n10\n3\n20\n0\n0\nVERTEX\n10\n3\n20\n4\n0\nSEQEND\n")]
    public void BothPolylineFormatsIncludeClosingEdge(string geometry)
    {
        CutPathLengthService.Calculate(DxfDocumentReader.Read(Dxf(geometry)))
            .CutLengthInches.Should().Be(12m);
    }

    [Theory]
    [InlineData("0\nLWPOLYLINE\n90\n2\n10\n0\n20\n0\n42\n1\n10\n2\n20\n0\n")]
    [InlineData("0\nPOLYLINE\n70\n0\n0\nVERTEX\n10\n0\n20\n0\n42\n1\n0\nVERTEX\n10\n2\n20\n0\n0\nSEQEND\n")]
    [InlineData("0\nINSERT\n2\nCUTBLOCK\n10\n0\n20\n0\n")]
    [InlineData("0\nLINE\n10\n0\n20\n0\n30\n3\n11\n2\n21\n0\n")]
    [InlineData("0\nLINE\n10\n0\n20\n0\n11\n2\n21\n0\n230\n-1\n")]
    [InlineData("0\nCIRCLE\n10\n0\n20\n0\n40\n-2\n")]
    [InlineData("0\nLINE\n10\n0\n20\n0\n11\nNaN\n21\n0\n")]
    public void UnsafeImportAndItsReexportNeverQuotePartialGeometry(string unsafeGeometry)
    {
        var document = DxfDocumentReader.Read(Dxf(Line + unsafeGeometry));
        var result = CutPathLengthService.Calculate(document);
        result.CutLengthInches.Should().BeNull();
        result.CutLengthReviewReason.Should().NotBeNullOrWhiteSpace();
        // Invalid coordinates need not be exported to check safety persistence.
        var partial = new DrawingDocument(document.Entities.Take(1), [], [], document.Metadata);
        CutPathLengthService.Calculate(DxfDocumentReader.Read(DxfDocumentWriter.Write(partial)))
            .CutLengthInches.Should().BeNull();
    }

    [Fact]
    public void GrainDimensionsAndBlockDefinitionsDoNotInflateCutLength()
    {
        var text = Dxf(Line + "0\nLINE\n8\nGRAIN\n10\n0\n20\n0\n11\n100\n21\n0\n0\nDIMENSION\n2\n*D1\n")
            .Replace("2\nENTITIES", "2\nBLOCKS\n0\nBLOCK\n2\n*D1\n" + Line + "0\nENDBLK\n0\nENDSEC\n0\nSECTION\n2\nENTITIES");
        var document = DxfDocumentReader.Read(text);
        CutPathLengthService.Calculate(document).CutLengthInches.Should().Be(25.4m);
    }

    private static string Dxf(string geometry, int units = 1) =>
        $"0\nSECTION\n2\nHEADER\n9\n$INSUNITS\n70\n{units}\n0\nENDSEC\n0\nSECTION\n2\nENTITIES\n{geometry}0\nENDSEC\n0\nEOF\n";
}

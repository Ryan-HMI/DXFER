using DXFER.CadIO;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Operations;
using FluentAssertions;

namespace DXFER.Core.Tests.IO;

public sealed class DxfDocumentReaderSafetyTests
{
    private const string Line = "0\nLINE\n10\n0\n20\n0\n11\n10\n21\n0\n";
    private const string Knots = "40\n0\n40\n0\n40\n0\n40\n0.35\n40\n1\n40\n1\n40\n1\n";
    private const string Weights = "41\n1\n41\n0.7\n41\n1.25\n41\n1\n";
    private const string Controls = "10\n0\n20\n0\n10\n1\n20\n2\n10\n3\n20\n2\n10\n4\n20\n0\n";
    private const string Spline = "0\nSPLINE\n70\n12\n71\n2\n72\n7\n73\n4\n74\n0\n" + Knots + Weights + Controls;
    private const string Circle = "0\nCIRCLE\n10\n1\n20\n2\n40\n3\n";
    private const string Arc = "0\nARC\n10\n1\n20\n2\n40\n3\n50\n350\n51\n10\n";
    private const string Ellipse = "0\nELLIPSE\n10\n1\n20\n2\n11\n3\n21\n0\n40\n0.5\n41\n0\n42\n1.5\n";

    [Theory]
    [InlineData(Knots, "")]
    [InlineData("40\n0.35\n", "")]
    [InlineData("40\n0.35\n", "40\n0.35\n40\n0.5\n")]
    [InlineData("40\n0.35\n", "40\n-1\n")]
    [InlineData("40\n0.35\n", "40\nNaN\n")]
    [InlineData("40\n0.35\n", "40\nInfinity\n")]
    [InlineData("40\n0.35\n", "40\nbad\n")]
    [InlineData(Knots, "40\n0\n40\n0\n40\n1\n40\n1\n40\n1\n40\n2\n40\n2\n")]
    [InlineData("41\n0.7\n", "")]
    [InlineData("41\n0.7\n", "41\n0.7\n41\n2\n")]
    [InlineData("41\n0.7\n", "41\nNaN\n")]
    [InlineData("41\n0.7\n", "41\nInfinity\n")]
    [InlineData("41\n0.7\n", "41\nbad\n")]
    [InlineData("41\n0.7\n", "41\n0\n")]
    [InlineData("41\n0.7\n", "41\n-1\n")]
    [InlineData("71\n2\n", "71\n1.5\n")]
    [InlineData("71\n2\n", "71\n2147483647\n")]
    [InlineData("71\n2\n", "71\n2\n71\n3\n")]
    [InlineData("71\n2\n", "")]
    [InlineData("72\n7\n", "72\n6\n")]
    [InlineData("72\n7\n", "72\n7\n72\n7\n")]
    [InlineData("73\n4\n", "73\n3\n")]
    [InlineData("73\n4\n", "73\nbad\n")]
    [InlineData("74\n0\n", "74\n2\n")]
    [InlineData("10\n1\n20\n2\n", "10\n1\n")]
    [InlineData("10\n1\n20\n2\n", "20\n2\n")]
    [InlineData("10\n1\n20\n2\n", "10\n1\n10\n2\n20\n2\n")]
    [InlineData("10\n1\n20\n2\n", "10\nNaN\n20\n2\n")]
    [InlineData("74\n0\n", "74\n0\n11\n1\n")]
    public void MalformedSplineIsWarnedAndNotReconstructed(string original, string replacement)
    {
        var document = DxfDocumentReader.Read(Dxf(Line + Spline.Replace(original, replacement, StringComparison.Ordinal)));

        document.Entities.Should().ContainSingle().Which.Should().BeOfType<LineEntity>();
        AssertUnsafe(document);
    }

    [Fact]
    public void FitPointsDoNotSubstituteForMissingControlDefinition()
    {
        var fitOnly = "0\nSPLINE\n71\n3\n72\n0\n73\n0\n74\n3\n11\n0\n21\n0\n11\n1\n21\n2\n11\n4\n21\n0\n";
        var document = DxfDocumentReader.Read(Dxf(Line + fitOnly));

        document.Entities.Should().ContainSingle().Which.Should().BeOfType<LineEntity>();
        AssertUnsafe(document);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ValidNonuniformSplinePreservesDefinitionWithoutUnsafeImportWarning(bool explicitWeights)
    {
        var document = DxfDocumentReader.Read(Dxf(explicitWeights ? Spline : Spline.Replace(Weights, "")));
        var spline = document.Entities.Should().ContainSingle().Which.Should().BeOfType<SplineEntity>().Subject;

        spline.Degree.Should().Be(2);
        spline.Knots.Should().Equal(0, 0, 0, 0.35, 1, 1, 1);
        spline.Weights.Should().Equal(explicitWeights ? new[] { 1d, 0.7, 1.25, 1 } : new[] { 1d, 1, 1, 1 });
        spline.ControlPoints.Should().Equal(new Point2(0, 0), new Point2(1, 2), new Point2(3, 2), new Point2(4, 0));
        document.Metadata.Warnings.Should().NotContain(warning => warning.Code == CutPathLengthService.UnsafeImportCode);
        document.Metadata.UnsupportedEntityCounts.Should().BeEmpty();
        CutPathLengthService.Calculate(document).CutLengthReviewReason.Should().Contain("Spline");

        var roundTripped = DxfDocumentReader.Read(DxfDocumentWriter.Write(document));
        var writtenSpline = roundTripped.Entities.Should().ContainSingle().Which.Should().BeOfType<SplineEntity>().Subject;
        writtenSpline.Degree.Should().Be(spline.Degree);
        writtenSpline.Knots.Should().Equal(spline.Knots);
        writtenSpline.Weights.Should().Equal(spline.Weights);
        writtenSpline.ControlPoints.Should().Equal(spline.ControlPoints);
        roundTripped.Metadata.Warnings.Should().NotContain(warning => warning.Code == CutPathLengthService.UnsafeImportCode);
    }

    [Theory]
    [InlineData("1", "40\n-1\n40\n0\n40\n0.2\n40\n0.8\n40\n1\n40\n2\n")]
    [InlineData("3", "40\n0\n40\n0\n40\n0\n40\n0\n40\n1\n40\n1\n40\n1\n40\n1\n")]
    public void OtherValidDegreesAndUnclampedKnotsAreNotReplaced(string degree, string knots)
    {
        var source = Spline.Replace("71\n2\n", $"71\n{degree}\n").Replace("72\n7\n", "").Replace(Knots, knots);
        var document = DxfDocumentReader.Read(Dxf(source));
        var spline = document.Entities.Should().ContainSingle().Which.Should().BeOfType<SplineEntity>().Subject;

        spline.Degree.Should().Be(int.Parse(degree));
        spline.Knots.Should().Equal(knots.Split('\n').Where((_, index) => index % 2 == 1)
            .Select(value => double.Parse(value, System.Globalization.CultureInfo.InvariantCulture)));
        document.Metadata.Warnings.Should().NotContain(warning => warning.Code == CutPathLengthService.UnsafeImportCode);
    }

    [Theory]
    [InlineData(Circle + "40\n4\n")]
    [InlineData(Circle + "10\n9\n")]
    [InlineData(Arc + "50\n20\n")]
    [InlineData(Ellipse + "41\n0.5\n")]
    [InlineData("0\nARC\n10\n0\n20\n0\n40\n1\n50\n90\n51\n90\n")]
    [InlineData("0\nARC\n10\n0\n20\n0\n40\n1\n50\n360\n51\n0\n")]
    [InlineData("0\nELLIPSE\n10\n0\n20\n0\n11\n0\n21\n0\n40\n0.5\n")]
    [InlineData("0\nELLIPSE\n10\n0\n20\n0\n11\n1\n21\n0\n40\n2\n")]
    [InlineData("0\nELLIPSE\n10\n0\n20\n0\n11\n1\n21\n0\n40\n0.5\n41\nbad\n")]
    [InlineData("0\nELLIPSE\n10\n0\n20\n0\n11\n1\n21\n0\n40\n0.5\n41\n1\n42\n1\n")]
    public void AmbiguousOrMalformedCurveParametersRequireReview(string curve)
    {
        AssertUnsafe(DxfDocumentReader.Read(Dxf(Line + curve)));
    }

    [Theory]
    [InlineData("0\nCIRCLE\n10\n0\n20\n0\n40\n0\n")]
    [InlineData("0\nCIRCLE\n10\n0\n20\n0\n40\n-1\n")]
    [InlineData("0\nARC\n10\n0\n20\n0\n40\n0\n50\n0\n51\n90\n")]
    [InlineData("0\nARC\n10\n0\n20\n0\n40\n-1\n50\n0\n51\n90\n")]
    [InlineData("0\nELLIPSE\n10\n0\n20\n0\n11\n1\n21\n0\n40\n0\n")]
    [InlineData("0\nELLIPSE\n10\n0\n20\n0\n11\n1\n21\n0\n40\n-0.5\n")]
    [InlineData("0\nELLIPSE\n10\n0\n20\n0\n11\n1\n21\n0\n40\n2\n")]
    [InlineData("0\nELLIPSE\n10\n0\n20\n0\n11\n0\n21\n0\n40\n0.5\n")]
    [InlineData("0\nELLIPSE\n10\n0\n20\n0\n11\nNaN\n21\n0\n40\n0.5\n")]
    [InlineData("0\nELLIPSE\n10\n0\n20\n0\n11\n1\n40\n0.5\n")]
    public void DegenerateCurvesAreSkippedWithUnsafeImportWarning(string curve)
    {
        var document = DxfDocumentReader.Read(Dxf(Line + curve));

        document.Entities.Should().ContainSingle().Which.Should().BeOfType<LineEntity>();
        document.Metadata.UnsupportedEntityCounts.Should().BeEmpty();
        AssertUnsafe(document);
    }

    [Theory]
    [InlineData(Circle)]
    [InlineData(Arc)]
    [InlineData(Ellipse)]
    [InlineData("0\nELLIPSE\n10\n0\n20\n0\n11\n2\n21\n0\n40\n0.5\n")]
    public void ValidCurvesDoNotAcquireUnsafeImportWarnings(string curve)
    {
        var document = DxfDocumentReader.Read(Dxf(curve));

        document.Entities.Should().ContainSingle();
        document.Metadata.Warnings.Should().NotContain(warning => warning.Code == CutPathLengthService.UnsafeImportCode);
    }

    [Theory]
    [InlineData(Line)]
    [InlineData(Spline)]
    [InlineData("0\nPOINT\n10\n1\n20\n2\n")]
    public void NonModelLayoutRequiresReviewEvenWithoutPaperSpaceFlag(string entity)
    {
        AssertUnsafe(DxfDocumentReader.Read(Dxf(entity + "410\nLayout1\n")));
        var model = DxfDocumentReader.Read(Dxf(entity + "410\nmOdEl\n"));
        model.Metadata.Warnings.Should().NotContain(warning => warning.Code == CutPathLengthService.UnsafeImportCode);
    }

    [Theory]
    [InlineData("10\nNaN\n20\n1\n")]
    [InlineData("10\n1\n20\nInfinity\n")]
    [InlineData("10\n1\n20\n2\n30\n-Infinity\n")]
    [InlineData("10\n1\n")]
    public void InvalidPointDoesNotSilentlyDisappearOrLoseInvalidZ(string coordinates)
    {
        AssertUnsafe(DxfDocumentReader.Read(Dxf(Line + "0\nPOINT\n" + coordinates)));
    }

    [Fact]
    public void RejectedSplineWarningSurvivesWritingRemainingGeometry()
    {
        var document = DxfDocumentReader.Read(Dxf(Line + Spline.Replace(Weights, "41\n2\n")));
        AssertUnsafe(DxfDocumentReader.Read(DxfDocumentWriter.Write(document)));
    }

    private static void AssertUnsafe(DrawingDocument document) => document.Metadata.Warnings.Should().Contain(warning =>
        warning.Code == CutPathLengthService.UnsafeImportCode && warning.Severity == DrawingDocumentWarningSeverity.Warning);

    private static string Dxf(string entities) => $"0\nSECTION\n2\nENTITIES\n{entities}0\nENDSEC\n0\nEOF\n";
}

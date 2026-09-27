using DXFER.CadIO;
using FluentAssertions;

namespace DXFER.Core.Tests.Operations;

public sealed class DxfCleanupTests
{
    [Fact]
    public void CleanupPreservesOriginalRecordsAndCoordinatesAndIsIdempotent()
    {
        var source = File(Circle("A") + Circle("B") + "0\nTEXT\n5\nC\n8\nGRAIN\n1\nKEEP THIS\n");
        var result = DxfCleanupAnalysis.Analyze(source);
        result.ReviewReason.Should().BeNull();
        result.RemovedCount.Should().Be(1);
        result.CleanedDxf.Should().Be(source.Replace(Circle("B"), ""));
        result.ClosedContourCount.Should().Be(1);
        result.CutLengthInches.Should().BeApproximately(6.283185m, .000001m);
        result.BoundingWidth.Should().Be(2);
        DxfCleanupAnalysis.Analyze(result.CleanedDxf!).CleanedDxf.Should().BeNull();
    }

    [Fact]
    public void ReferencesToRemovedEntitiesRequireReviewWithoutRewriting()
    {
        var result = DxfCleanupAnalysis.Analyze(File(Circle("A") + Circle("B") + "0\nDIMENSION\n340\nB\n"));
        result.ReviewReason.Should().NotBeNull();
        result.CleanedDxf.Should().BeNull();
        result.CutLengthInches.Should().BeNull();
    }

    [Fact]
    public void MissingAndDuplicateHandlesCannotBeGuessed()
    {
        DxfCleanupAnalysis.Analyze(File(Circle("A") + Circle("A"))).ReviewReason.Should().NotBeNull();
        DxfCleanupAnalysis.Analyze(File((Circle("A") + Circle("B")).Replace("5\nA\n", "").Replace("5\nB\n", "")))
            .ReviewReason.Should().NotBeNull();
    }

    [Fact]
    public void TrailingWhitespaceDoesNotMakeValidFilesMalformed()
    {
        DxfCleanupAnalysis.Analyze(File(Circle("A")) + "\n  \n").CutLengthInches.Should().NotBeNull();
    }

    [Fact]
    public void OpenPathsKeepLengthButNeverInventPierces()
    {
        var result = DxfCleanupAnalysis.Analyze(File("0\nLINE\n5\nA\n10\n0\n20\n0\n11\n2\n21\n0\n"));
        result.CutLengthInches.Should().Be(2);
        result.ClosedContourCount.Should().BeNull();
        result.ContourReviewReason.Should().NotBeNull();
    }

    private static string Circle(string handle) => $"0\nCIRCLE\n5\n{handle}\n10\n12\n20\n23\n40\n1\n";
    private static string File(string entities) => "0\nSECTION\n2\nHEADER\n9\n$INSUNITS\n70\n1\n0\nENDSEC\n0\nSECTION\n2\nENTITIES\n" + entities + "0\nENDSEC\n0\nEOF\n";
}

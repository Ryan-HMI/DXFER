using DXFER.CadIO;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Operations;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Operations;

public sealed class EllipseCutAnalysisTests
{
    [Theory]
    [InlineData(0, 0, 360)]
    [InlineData(37, 0, 360)]
    [InlineData(90, 0, 360)]
    [InlineData(37, 25, 220)]
    [InlineData(37, 300, 40)]
    [InlineData(90, -90, 90)]
    [InlineData(37, 30, 30)]
    public void SwappedAxisAnalysisMatchesCanonicalCurveAndPreservesDocument(double angle, double start, double end)
    {
        var radians = angle * Math.PI / 180;
        var ellipse = new EllipseEntity(EntityId.Create("ellipse"), new(7, -2),
            new(3 * Math.Cos(radians), 3 * Math.Sin(radians)), 4.0 / 3, start, end);
        var canonical = Canonical(ellipse);
        var dimensions = new[]
        {
            new SketchDimension("major", SketchDimensionKind.LinearDistance,
                new[] { "ellipse|point|major-start|0|0", "ellipse|point|major-end|0|0" }, 6, new(0, 0), true),
            new SketchDimension("minor", SketchDimensionKind.LinearDistance,
                new[] { "ellipse|point|minor-start|0|0", "ellipse|point|minor-end|0|0" }, 8, new(0, 0), true)
        };
        var constraints = new[] { new SketchConstraint("center", SketchConstraintKind.Fix, new[] { "ellipse:center" }) };
        var metadata = DrawingDocumentMetadata.Empty with
        {
            Units = DrawingUnits.Inches,
            EntityStyles = new Dictionary<string, DxfEntityStyle> { ["ellipse"] = new("CUT") }
        };
        var document = new DrawingDocument(new[] { ellipse }, dimensions, constraints, metadata);
        var expected = CutPathLengthService.Calculate(Doc(canonical));
        expected.CutLengthInches.Should().NotBeNull();

        var length = CutPathLengthService.Calculate(document);
        var contours = ClosedContourService.Calculate(document);

        length.CutLengthReviewReason.Should().BeNull();
        length.CutLengthInches.Should().BeApproximately(expected.CutLengthInches!.Value, 0.000001m);
        contours.Should().Be(ClosedContourService.Calculate(Doc(canonical)));
        if (start == end || end - start == 360)
        {
            contours.Should().Be(new ClosedContourResult(1, null));
        }
        else
        {
            contours.Should().Be(new ClosedContourResult(null, "Partial ellipse contours require review."));
        }

        var native = DxfDocumentReader.Read(DxfDocumentWriter.Write(document,
            new DxfWriteOptions(IncludeNativeDocument: true)), out var restored);
        restored.Should().BeTrue();
        native.Should().BeEquivalentTo(document, options => options.WithStrictOrdering());
        CutPathLengthService.Calculate(native).Should().Be(length);
        ClosedContourService.Calculate(native).Should().Be(contours);

        var plain = DxfDocumentReader.Read(DxfDocumentWriter.Write(document));
        CutPathLengthService.Calculate(plain).CutLengthInches.Should().BeApproximately(length.CutLengthInches!.Value, 0.000001m);
        ClosedContourService.Calculate(plain).Should().Be(contours);
        document.Entities.Single().Should().BeSameAs(ellipse);
        document.Metadata.Should().BeSameAs(metadata);
        document.Dimensions.Should().Equal(dimensions);
        document.Constraints.Should().Equal(constraints);
        ellipse.StartParameterDegrees.Should().Be(start);
        ellipse.EndParameterDegrees.Should().Be(end);
        ellipse.MinorRadiusRatio.Should().Be(4.0 / 3);
    }

    [Fact]
    public void FullCrossedEllipseAndDisjointHoleCountTwoContours()
    {
        var ellipse = new EllipseEntity(EntityId.Create("ellipse"), new(0, 0), new(3, 0), 4.0 / 3);
        var document = new DrawingDocument(new DrawingEntity[]
        {
            ellipse, new CircleEntity(EntityId.Create("hole"), new(10, 0), 1)
        });

        ClosedContourService.Calculate(document).Should().Be(new ClosedContourResult(2, null));
        CutPathLengthService.Calculate(document).CutLengthInches.Should().BeApproximately(
            CutPathLengthService.Calculate(Doc(Canonical(ellipse))).CutLengthInches!.Value + (decimal)Math.Tau, 0.000001m);
    }

    [Theory]
    [InlineData(0.001, 1000)]
    [InlineData(0.005, 200)]
    public void ContourSamplingRetainsLargerAxisDeviationAndClearanceGuards(double axis, double ratio)
    {
        var ellipse = new EllipseEntity(EntityId.Create("thin"), new(0, 0), new(axis, 0), ratio);
        var canonical = Canonical(ellipse);
        CutPathLengthService.Calculate(Doc(canonical)).CutLengthInches.Should().NotBeNull();
        CutPathLengthService.Calculate(Doc(ellipse)).CutLengthInches.Should().NotBeNull();
        var expected = ClosedContourService.Calculate(Doc(canonical));
        expected.ClosedContourCount.Should().BeNull();

        ClosedContourService.Calculate(Doc(ellipse)).ClosedContourCount.Should().BeNull();
        ClosedContourService.Calculate(Doc(ellipse)).ContourReviewReason.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(double.Epsilon)]
    [InlineData(0.0005)]
    [InlineData(2000)]
    public void InvalidOrTooEccentricGeometryStillRequiresReview(double ratio)
    {
        var ellipse = new EllipseEntity(EntityId.Create("invalid"), new(0, 0), new(3, 0), ratio);
        var document = Doc(ellipse);

        CutPathLengthService.Calculate(document).CutLengthInches.Should().BeNull();
        CutPathLengthService.Calculate(document).CutLengthReviewReason.Should().NotBeNullOrWhiteSpace();
        ClosedContourService.Calculate(document).ClosedContourCount.Should().BeNull();
        document.Entities.Single().Should().BeSameAs(ellipse);
    }

    [Fact]
    public void InvalidAxesSpansAndOverflowRemainReviewWithoutReturningPartialLength()
    {
        var valid = new EllipseEntity(EntityId.Create("invalid"), new(0, 0), new(3, 0), 4.0 / 3);
        foreach (var invalid in new[]
        {
            valid with { Center = new(double.NaN, 0) },
            valid with { Center = new(0, double.PositiveInfinity) },
            valid with { MajorAxisEndPoint = new(0, 0) },
            valid with { MajorAxisEndPoint = new(double.NaN, 0) },
            valid with { MajorAxisEndPoint = new(0, double.PositiveInfinity) },
            valid with { MajorAxisEndPoint = new(double.MaxValue, 0), MinorRadiusRatio = 2 },
            valid with { MajorAxisEndPoint = new(1e154, 0), MinorRadiusRatio = 1000 },
            valid with { StartParameterDegrees = double.NaN },
            valid with { EndParameterDegrees = double.PositiveInfinity },
            valid with { EndParameterDegrees = 720 }
        })
        {
            var document = new DrawingDocument(new DrawingEntity[]
            {
                new LineEntity(EntityId.Create("valid-line"), new(0, 0), new(3, 4)), invalid
            });

            CutPathLengthService.Calculate(document).CutLengthInches.Should().BeNull();
            CutPathLengthService.Calculate(document).CutLengthReviewReason.Should().NotBeNullOrWhiteSpace();
            ClosedContourService.Calculate(document).ClosedContourCount.Should().BeNull();
            document.Entities.Last().Should().BeSameAs(invalid);
        }
    }

    private static EllipseEntity Canonical(EllipseEntity ellipse) => ellipse with
    {
        MajorAxisEndPoint = new(-ellipse.MajorAxisEndPoint.Y * ellipse.MinorRadiusRatio,
            ellipse.MajorAxisEndPoint.X * ellipse.MinorRadiusRatio),
        MinorRadiusRatio = 1 / ellipse.MinorRadiusRatio,
        StartParameterDegrees = ellipse.StartParameterDegrees - 90,
        EndParameterDegrees = ellipse.EndParameterDegrees - 90
    };

    private static DrawingDocument Doc(EllipseEntity ellipse) => new(new[] { ellipse });
}

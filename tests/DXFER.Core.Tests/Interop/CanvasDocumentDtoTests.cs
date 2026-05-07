using DXFER.Blazor.Interop;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Interop;

public sealed class CanvasDocumentDtoTests
{
    [Fact]
    public void ExposesConstructionStateForRendering()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(
                EntityId.Create("construction-line"),
                new Point2(0, 0),
                new Point2(1, 0),
                IsConstruction: true)
        });

        var dto = CanvasDocumentDto.FromDocument(document);

        dto.Entities[0].IsConstruction.Should().BeTrue();
    }

    [Fact]
    public void ExposesSketchDimensionsAndConstraintsForRendering()
    {
        var document = new DrawingDocument(
            new DrawingEntity[]
            {
                new LineEntity(EntityId.Create("edge"), new Point2(0, 0), new Point2(3, 4))
            },
            new[]
            {
                new SketchDimension(
                    "dim-1",
                    SketchDimensionKind.LinearDistance,
                    new[] { "edge:start", "edge:end" },
                    5,
                    new Point2(1.5, 2),
                    isDriving: true)
            },
            new[]
            {
                new SketchConstraint(
                    "horizontal-1",
                    SketchConstraintKind.Horizontal,
                    new[] { "edge" },
                    SketchConstraintState.Satisfied)
            });

        var dto = CanvasDocumentDto.FromDocument(document);

        dto.Dimensions.Should().ContainSingle()
            .Which.Should().Match<CanvasSketchDimensionDto>(dimension =>
                dimension.Id == "dim-1"
                && dimension.Kind == "LinearDistance"
                && dimension.ReferenceKeys.SequenceEqual(new[] { "edge:start", "edge:end" })
                && dimension.Value == 5
                && dimension.Anchor == new CanvasPointDto(1.5, 2)
                && dimension.IsDriving
                && dimension.State == "Satisfied"
                && dimension.AffectedReferenceKeys.Count == 0);
        dto.Constraints.Should().ContainSingle()
            .Which.Should().Match<CanvasSketchConstraintDto>(constraint =>
                constraint.Id == "horizontal-1"
                && constraint.Kind == "Horizontal"
                && constraint.ReferenceKeys.SequenceEqual(new[] { "edge" })
                && constraint.State == "Satisfied"
                && constraint.AffectedReferenceKeys.Count == 0);
    }

    [Fact]
    public void ExposesUnsatisfiedSketchItemsWithAffectedReferences()
    {
        var document = new DrawingDocument(
            new DrawingEntity[]
            {
                new LineEntity(EntityId.Create("edge"), new Point2(0, 0), new Point2(4, 1))
            },
            new[]
            {
                new SketchDimension(
                    "distance",
                    SketchDimensionKind.LinearDistance,
                    new[] { "edge:start", "edge:end" },
                    10,
                    isDriving: true)
            },
            new[]
            {
                new SketchConstraint(
                    "horizontal",
                    SketchConstraintKind.Horizontal,
                    new[] { "edge" },
                    SketchConstraintState.Unsatisfied)
            });

        var dto = CanvasDocumentDto.FromDocument(document);

        dto.Dimensions.Should().ContainSingle()
            .Which.Should().Match<CanvasSketchDimensionDto>(dimension =>
                dimension.Id == "distance"
                && dimension.State == "Unsatisfied"
                && dimension.AffectedReferenceKeys.SequenceEqual(new[] { "edge:start", "edge:end" }));
        dto.Constraints.Should().ContainSingle()
            .Which.Should().Match<CanvasSketchConstraintDto>(constraint =>
                constraint.Id == "horizontal"
                && constraint.State == "Unsatisfied"
                && constraint.AffectedReferenceKeys.SequenceEqual(new[] { "edge" }));
    }

    [Fact]
    public void ExposesEllipseAxesForRendering()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new EllipseEntity(EntityId.Create("ellipse-a"), new Point2(1, 2), new Point2(4, 0), 0.5, 0, 90)
        });

        var dto = CanvasDocumentDto.FromDocument(document);

        dto.Entities.Should().ContainSingle()
            .Which.Should().Match<CanvasEntityDto>(entity =>
                entity.Kind == "ellipse"
                && entity.Center == new CanvasPointDto(1, 2)
                && entity.MajorAxisEndPoint == new CanvasPointDto(4, 0)
                && entity.MinorRadiusRatio == 0.5
                && entity.StartAngleDegrees == 0
                && entity.EndAngleDegrees == 90);
    }

    [Fact]
    public void ExposesPolygonMetadataForParametricRendering()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new PolygonEntity(EntityId.Create("poly-a"), new Point2(1, 2), 10, 30, 8, Circumscribed: true)
        });

        var dto = CanvasDocumentDto.FromDocument(document);

        var polygon = dto.Entities.Should().ContainSingle().Subject;
        polygon.Kind.Should().Be("polygon");
        polygon.Center.Should().Be(new CanvasPointDto(1, 2));
        polygon.Radius.Should().Be(10);
        polygon.RotationAngleDegrees.Should().Be(30);
        polygon.SideCount.Should().Be(8);
        polygon.Circumscribed.Should().BeTrue();
        polygon.Points.Should().HaveCount(8);
    }

    [Fact]
    public void ExposesSplineFitPointsForPersistentHandles()
    {
        var fitPoints = new[]
        {
            new Point2(0, 0),
            new Point2(1, 2),
            new Point2(3, 1),
            new Point2(5, 4)
        };
        var document = new DrawingDocument(new DrawingEntity[]
        {
            SplineEntity.FromFitPoints(EntityId.Create("spline-a"), fitPoints)
        });

        var dto = CanvasDocumentDto.FromDocument(document);

        var spline = dto.Entities.Should().ContainSingle().Subject;
        spline.Kind.Should().Be("spline");
        spline.FitPoints.Should().Equal(fitPoints.Select(point => new CanvasPointDto(point.X, point.Y)));
        spline.Points.Should().HaveCountGreaterThan(fitPoints.Length);
    }
}

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
                && dimension.IsDriving);
        dto.Constraints.Should().ContainSingle()
            .Which.Should().Match<CanvasSketchConstraintDto>(constraint =>
                constraint.Id == "horizontal-1"
                && constraint.Kind == "Horizontal"
                && constraint.ReferenceKeys.SequenceEqual(new[] { "edge" })
                && constraint.State == "Satisfied");
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
}

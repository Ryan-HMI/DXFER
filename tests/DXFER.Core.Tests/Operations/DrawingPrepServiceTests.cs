using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Operations;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Operations;

public sealed class DrawingPrepServiceTests
{
    [Fact]
    public void MovesDocumentBoundsMinimumToOrigin()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("a"), new Point2(12, 8), new Point2(20, 8)),
            new LineEntity(EntityId.Create("b"), new Point2(20, 8), new Point2(20, 16))
        });

        var moved = DrawingPrepService.MoveBoundsMinimumToOrigin(document);

        moved.GetBounds().MinX.Should().BeApproximately(0, 0.0001);
        moved.GetBounds().MinY.Should().BeApproximately(0, 0.0001);
        moved.GetBounds().Width.Should().BeApproximately(8, 0.0001);
        moved.GetBounds().Height.Should().BeApproximately(8, 0.0001);
    }

    [Fact]
    public void TransformsOnlySelectedEntities()
    {
        var selectedId = EntityId.Create("selected");
        var stationaryId = EntityId.Create("stationary");
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(selectedId, new Point2(1, 1), new Point2(2, 1)),
            new LineEntity(stationaryId, new Point2(10, 10), new Point2(11, 10))
        });

        var transformed = DrawingPrepService.TransformSelected(
            document,
            new[] { selectedId.Value },
            Transform2.Translation(5, -1));

        transformed.Entities[0].Should().BeOfType<LineEntity>()
            .Which.Start.Should().Be(new Point2(6, 0));
        transformed.Entities[1].Should().Be(document.Entities[1]);
    }

    [Fact]
    public void RotatesDocumentAboutBoundsCenter()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("edge"), new Point2(0, 0), new Point2(10, 0))
        });

        var rotated = DrawingPrepService.RotateAboutBoundsCenter(document, 90);

        var edge = rotated.Entities[0].Should().BeOfType<LineEntity>().Subject;
        edge.Start.X.Should().BeApproximately(5, 0.0001);
        edge.Start.Y.Should().BeApproximately(-5, 0.0001);
        edge.End.X.Should().BeApproximately(5, 0.0001);
        edge.End.Y.Should().BeApproximately(5, 0.0001);
    }

    [Fact]
    public void AlignsSelectedVectorToGlobalXAxisAroundDocumentCenter()
    {
        var vectorId = EntityId.Create("vector");
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(vectorId, new Point2(0, 0), new Point2(0, 10)),
            new LineEntity(EntityId.Create("other"), new Point2(10, 0), new Point2(10, 10))
        });

        var aligned = DrawingPrepService.AlignVectorToAxis(document, vectorId.Value, AxisDirection.X);

        var vector = aligned.Entities[0].Should().BeOfType<LineEntity>().Subject;
        Math.Abs(vector.End.Y - vector.Start.Y).Should().BeLessThan(0.0001);
        Math.Abs(vector.End.X - vector.Start.X).Should().BeGreaterThan(9.999);
    }

    [Fact]
    public void AlignsExplicitVectorToGlobalYAxisAroundDocumentCenter()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("edge"), new Point2(0, 0), new Point2(10, 0)),
            new LineEntity(EntityId.Create("other"), new Point2(0, 10), new Point2(10, 10))
        });

        var aligned = DrawingPrepService.AlignVectorToAxis(
            document,
            new Point2(0, 0),
            new Point2(10, 0),
            AxisDirection.Y);

        var edge = aligned.Entities[0].Should().BeOfType<LineEntity>().Subject;
        Math.Abs(edge.End.X - edge.Start.X).Should().BeLessThan(0.0001);
        Math.Abs(edge.End.Y - edge.Start.Y).Should().BeGreaterThan(9.999);
    }

    [Fact]
    public void RepeatingAxisAlignmentFlipsParallelVectorDirection()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("edge"), new Point2(0, 0), new Point2(10, 0)),
            new LineEntity(EntityId.Create("other"), new Point2(0, 10), new Point2(10, 10))
        });

        var flipped = DrawingPrepService.AlignVectorToAxis(
            document,
            new Point2(0, 0),
            new Point2(10, 0),
            AxisDirection.X);

        var edge = flipped.Entities[0].Should().BeOfType<LineEntity>().Subject;
        edge.End.X.Should().BeLessThan(edge.Start.X);
        Math.Abs(edge.End.Y - edge.Start.Y).Should().BeLessThan(0.0001);
    }

    [Fact]
    public void ReportsDistanceWithAxisDeltas()
    {
        var measurement = MeasurementService.Measure(new Point2(2, 3), new Point2(8, 11));

        measurement.DeltaX.Should().Be(6);
        measurement.DeltaY.Should().Be(8);
        measurement.Distance.Should().BeApproximately(10, 0.0001);
    }

    [Fact]
    public void ReportsSelectedEntityBoundsWhenMultipleEntitiesAreSelected()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("edge"), new Point2(-2, 1), new Point2(2, 3)),
            new CircleEntity(EntityId.Create("hole"), new Point2(5, 1), 2)
        });

        DrawingPrepService.TryGetMeasurement(document, new[] { "edge", "hole" }, out var measurement)
            .Should().BeTrue();

        measurement.DeltaX.Should().Be(9);
        measurement.DeltaY.Should().Be(4);
        measurement.Distance.Should().BeApproximately(Math.Sqrt(97), 0.0001);
    }

    [Fact]
    public void TransformPreservesSketchData()
    {
        var metadata = DrawingDocumentMetadata.Empty with
        {
            SourceFileName = "source.dxf",
            Units = DrawingUnits.Inches,
            TrustedSource = true
        };
        var dimension = new SketchDimension(
            "dim-1",
            SketchDimensionKind.LinearDistance,
            new[] { "edge:start", "edge:end" },
            10);
        var constraint = new SketchConstraint(
            "constraint-1",
            SketchConstraintKind.Horizontal,
            new[] { "edge" });
        var document = new DrawingDocument(
            new DrawingEntity[]
            {
                new LineEntity(EntityId.Create("edge"), new Point2(0, 0), new Point2(10, 0))
            },
            new[] { dimension },
            new[] { constraint },
            metadata);

        var transformed = DrawingPrepService.Transform(document, Transform2.Translation(1, 2));

        transformed.Dimensions.Should().ContainSingle()
            .Which.Should().BeSameAs(dimension);
        transformed.Constraints.Should().ContainSingle()
            .Which.Should().BeSameAs(constraint);
        transformed.Metadata.Should().BeEquivalentTo(metadata);
    }
}

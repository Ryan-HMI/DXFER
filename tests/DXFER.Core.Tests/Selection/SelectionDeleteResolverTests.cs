using DXFER.Blazor.Selection;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Selection;

public sealed class SelectionDeleteResolverTests
{
    [Fact]
    public void DeletesWholeSelectedEntities()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("keep"), new Point2(0, 0), new Point2(1, 0)),
            new CircleEntity(EntityId.Create("delete"), new Point2(5, 5), 2)
        });

        var result = SelectionDeleteResolver.DeleteSelection(document, new[] { "delete" });

        result.DeletedEntities.Should().Be(1);
        result.DeletedSegments.Should().Be(0);
        result.Document.Entities.Should().ContainSingle()
            .Which.Id.Value.Should().Be("keep");
    }

    [Fact]
    public void DeletesWholeEntitiesWhenPointSelectionsAreAlsoPresent()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("edge"), new Point2(0, 0), new Point2(10, 0))
        });

        var result = SelectionDeleteResolver.DeleteSelection(
            document,
            new[] { "edge|point|mid|5|0", "edge" });

        result.DeletedEntities.Should().Be(1);
        result.Document.Entities.Should().BeEmpty();
    }

    [Fact]
    public void SplitsPolylineWhenSelectedSegmentIsDeleted()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new PolylineEntity(
                EntityId.Create("poly"),
                new[]
                {
                    new Point2(0, 0),
                    new Point2(10, 0),
                    new Point2(20, 0),
                    new Point2(30, 0)
                })
        });

        var result = SelectionDeleteResolver.DeleteSelection(document, new[] { "poly|segment|1" });

        result.DeletedEntities.Should().Be(0);
        result.DeletedSegments.Should().Be(1);
        result.Document.Entities.Should().HaveCount(2);
        result.Document.Entities[0].Should().BeOfType<PolylineEntity>()
            .Which.Vertices.Should().Equal(new Point2(0, 0), new Point2(10, 0));
        result.Document.Entities[1].Should().BeOfType<PolylineEntity>()
            .Which.Vertices.Should().Equal(new Point2(20, 0), new Point2(30, 0));
    }

    [Fact]
    public void IgnoresPointOnlySelection()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("edge"), new Point2(0, 0), new Point2(1, 0))
        });

        var result = SelectionDeleteResolver.DeleteSelection(
            document,
            new[] { "edge|point|start|0|0" });

        result.DeletedGeometryCount.Should().Be(0);
        result.Document.Should().BeSameAs(document);
    }

    [Fact]
    public void DeletesSelectedDimensions()
    {
        var dimension = new SketchDimension(
            "dim-1",
            SketchDimensionKind.LinearDistance,
            new[] { "edge:start", "edge:end" },
            10);
        var document = new DrawingDocument(
            new DrawingEntity[]
            {
                new LineEntity(EntityId.Create("edge"), new Point2(0, 0), new Point2(10, 0))
            },
            new[] { dimension },
            Array.Empty<SketchConstraint>());

        var result = SelectionDeleteResolver.DeleteSelection(document, new[] { "persistent-dim-1" });

        result.DeletedDimensions.Should().Be(1);
        result.DeletedCount.Should().Be(1);
        result.Document.Entities.Should().ContainSingle();
        result.Document.Dimensions.Should().BeEmpty();
    }

    [Fact]
    public void DeletesSelectedConstraints()
    {
        var constraint = new SketchConstraint(
            "constraint-1",
            SketchConstraintKind.Horizontal,
            new[] { "edge" });
        var document = new DrawingDocument(
            new DrawingEntity[]
            {
                new LineEntity(EntityId.Create("edge"), new Point2(0, 0), new Point2(10, 0))
            },
            Array.Empty<SketchDimension>(),
            new[] { constraint });

        var result = SelectionDeleteResolver.DeleteSelection(document, new[] { "constraint:constraint-1" });

        result.DeletedConstraints.Should().Be(1);
        result.DeletedCount.Should().Be(1);
        result.Document.Entities.Should().ContainSingle();
        result.Document.Constraints.Should().BeEmpty();
    }

    [Fact]
    public void DeletesSketchItemsThatReferenceDeletedGeometry()
    {
        var referencingDimension = new SketchDimension(
            "delete-dim",
            SketchDimensionKind.LinearDistance,
            new[] { "delete:start", "delete:end" },
            10);
        var keptDimension = new SketchDimension(
            "keep-dim",
            SketchDimensionKind.Radius,
            new[] { "keep" },
            2);
        var referencingConstraint = new SketchConstraint(
            "delete-constraint",
            SketchConstraintKind.Horizontal,
            new[] { "delete" });
        var keptConstraint = new SketchConstraint(
            "keep-constraint",
            SketchConstraintKind.Fix,
            new[] { "keep" });
        var document = new DrawingDocument(
            new DrawingEntity[]
            {
                new LineEntity(EntityId.Create("delete"), new Point2(0, 0), new Point2(10, 0)),
                new CircleEntity(EntityId.Create("keep"), new Point2(20, 0), 2)
            },
            new[] { referencingDimension, keptDimension },
            new[] { referencingConstraint, keptConstraint });

        var result = SelectionDeleteResolver.DeleteSelection(document, new[] { "delete" });

        result.DeletedEntities.Should().Be(1);
        result.DeletedDimensions.Should().Be(1);
        result.Document.Dimensions.Should().ContainSingle()
            .Which.Id.Should().Be("keep-dim");
        result.Document.Constraints.Should().ContainSingle()
            .Which.Id.Should().Be("keep-constraint");
    }
}

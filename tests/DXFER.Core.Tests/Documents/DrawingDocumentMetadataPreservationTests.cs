using DXFER.Blazor.Selection;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Operations;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Documents;

public sealed class DrawingDocumentMetadataPreservationTests
{
    [Fact]
    public void LineSplitPreservesDocumentMetadata()
    {
        var metadata = CreateMetadata();
        var document = new DrawingDocument(
            new DrawingEntity[]
            {
                new LineEntity(EntityId.Create("edge"), new Point2(0, 0), new Point2(10, 0))
            },
            Array.Empty<SketchDimension>(),
            Array.Empty<SketchConstraint>(),
            metadata);

        var split = LineSplitService.TrySplitLineAtPoint(
            document,
            "edge",
            new Point2(4, 0),
            EntityId.Create("edge-split"),
            out var nextDocument);

        split.Should().BeTrue();
        nextDocument.Metadata.Should().BeSameAs(metadata);
    }

    [Fact]
    public void CurveSplitPreservesDocumentMetadata()
    {
        var metadata = CreateMetadata();
        var document = new DrawingDocument(
            new DrawingEntity[]
            {
                new CircleEntity(EntityId.Create("circle"), new Point2(0, 0), 5)
            },
            Array.Empty<SketchDimension>(),
            Array.Empty<SketchConstraint>(),
            metadata);

        var split = CurveSplitService.TrySplitCircleAtPoints(
            document,
            "circle",
            new Point2(5, 0),
            new Point2(0, 5),
            EntityId.Create("circle-split"),
            out var nextDocument);

        split.Should().BeTrue();
        nextDocument.Metadata.Should().BeSameAs(metadata);
    }

    [Fact]
    public void ModifyServicePreservesDocumentMetadata()
    {
        var metadata = CreateMetadata();
        var document = new DrawingDocument(
            new DrawingEntity[]
            {
                new LineEntity(EntityId.Create("edge"), new Point2(0, 0), new Point2(10, 0))
            },
            Array.Empty<SketchDimension>(),
            Array.Empty<SketchConstraint>(),
            metadata);

        var offset = DrawingModifyService.TryOffsetSelected(
            document,
            new[] { "edge" },
            new Point2(0, 2),
            kind => EntityId.Create($"{kind}-copy"),
            out var nextDocument,
            out _);

        offset.Should().BeTrue();
        nextDocument.Metadata.Should().BeSameAs(metadata);
    }

    [Fact]
    public void ConstructionTogglePreservesDocumentMetadata()
    {
        var metadata = CreateMetadata();
        var document = new DrawingDocument(
            new DrawingEntity[]
            {
                new LineEntity(EntityId.Create("edge"), new Point2(0, 0), new Point2(10, 0))
            },
            Array.Empty<SketchDimension>(),
            Array.Empty<SketchConstraint>(),
            metadata);

        var result = DrawingConstructionService.ToggleSelected(document, new[] { "edge" });

        result.ChangedCount.Should().Be(1);
        result.Document.Metadata.Should().BeSameAs(metadata);
    }

    [Fact]
    public void SelectionDeletePreservesDocumentMetadata()
    {
        var metadata = CreateMetadata();
        var document = new DrawingDocument(
            new DrawingEntity[]
            {
                new LineEntity(EntityId.Create("keep"), new Point2(0, 0), new Point2(10, 0)),
                new CircleEntity(EntityId.Create("delete"), new Point2(5, 5), 1)
            },
            Array.Empty<SketchDimension>(),
            Array.Empty<SketchConstraint>(),
            metadata);

        var result = SelectionDeleteResolver.DeleteSelection(document, new[] { "delete" });

        result.DeletedEntities.Should().Be(1);
        result.Document.Metadata.Should().BeSameAs(metadata);
    }

    [Fact]
    public void SketchConstraintServicePreservesDocumentMetadata()
    {
        var metadata = CreateMetadata();
        var document = new DrawingDocument(
            new DrawingEntity[]
            {
                new LineEntity(EntityId.Create("edge"), new Point2(0, 0), new Point2(10, 2))
            },
            Array.Empty<SketchDimension>(),
            Array.Empty<SketchConstraint>(),
            metadata);
        var constraint = new SketchConstraint(
            "horizontal",
            SketchConstraintKind.Horizontal,
            new[] { "edge" });

        var constrained = SketchConstraintService.ApplyConstraint(document, constraint);

        constrained.Metadata.Should().BeSameAs(metadata);
    }

    [Fact]
    public void SketchDimensionSolverPreservesDocumentMetadata()
    {
        var metadata = CreateMetadata();
        var document = new DrawingDocument(
            new DrawingEntity[]
            {
                new LineEntity(EntityId.Create("edge"), new Point2(0, 0), new Point2(3, 4))
            },
            Array.Empty<SketchDimension>(),
            Array.Empty<SketchConstraint>(),
            metadata);
        var dimension = new SketchDimension(
            "length",
            SketchDimensionKind.LinearDistance,
            new[] { "edge:start", "edge:end" },
            10,
            isDriving: true);

        var solved = SketchDimensionSolverService.ApplyDimension(document, dimension);

        solved.Metadata.Should().BeSameAs(metadata);
    }

    [Fact]
    public void SketchGeometryDragPreservesDocumentMetadata()
    {
        var metadata = CreateMetadata();
        var document = new DrawingDocument(
            new DrawingEntity[]
            {
                new LineEntity(EntityId.Create("edge"), new Point2(0, 0), new Point2(10, 0))
            },
            Array.Empty<SketchDimension>(),
            Array.Empty<SketchConstraint>(),
            metadata);

        var changed = SketchGeometryDragService.TryApplyDrag(
            document,
            "edge",
            new Point2(0, 0),
            new Point2(1, 1),
            false,
            out var nextDocument,
            out _);

        changed.Should().BeTrue();
        nextDocument.Metadata.Should().BeSameAs(metadata);
    }

    private static DrawingDocumentMetadata CreateMetadata() =>
        DrawingDocumentMetadata.Empty with
        {
            SourceFileName = "source.dxf",
            SourceSha256 = "sha",
            Units = DrawingUnits.Millimeters,
            TrustedSource = true,
            Warnings = new[]
            {
                new DrawingDocumentWarning(
                    "unsupported-entity",
                    DrawingDocumentWarningSeverity.Warning,
                    "Skipped unsupported entity.")
            },
            UnsupportedEntityCounts = new Dictionary<string, int>
            {
                ["3DSOLID"] = 1
            }
        };
}

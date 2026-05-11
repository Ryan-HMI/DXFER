using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using DXFER.Blazor.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class SketchGeometryDragServiceTests
{
    [Fact]
    public void DraggingConstrainedRectangleEdgeKeepsCoincidentCornersConnected()
    {
        var sequence = 0;
        var entities = SketchCreationEntityFactory.CreateEntitiesForTool(
            "twopointrectangle",
            new[] { new Point2(0, 0), new Point2(10, 5) },
            prefix => EntityId.Create($"{prefix}-{++sequence}"),
            isConstruction: false);
        var constraints = SketchCreationConstraintFactory.CreateConstraintsForTool(
            "twopointrectangle",
            entities,
            kind => $"constraint-{kind}-{Guid.NewGuid():N}");
        var document = new DrawingDocument(entities, Array.Empty<SketchDimension>(), constraints);

        var changed = SketchGeometryDragService.TryApplyDrag(
            document,
            "rect-3",
            new Point2(5, 5),
            new Point2(5, 7),
            false,
            out var next,
            out _);

        changed.Should().BeTrue();
        next.Entities.Should().Equal(
            new LineEntity(EntityId.Create("rect-1"), new Point2(0, 0), new Point2(10, 0)),
            new LineEntity(EntityId.Create("rect-2"), new Point2(10, 0), new Point2(10, 7)),
            new LineEntity(EntityId.Create("rect-3"), new Point2(10, 7), new Point2(0, 7)),
            new LineEntity(EntityId.Create("rect-4"), new Point2(0, 7), new Point2(0, 0)));
        next.Constraints.Should().OnlyContain(constraint => constraint.State == SketchConstraintState.Satisfied);
    }

    [Fact]
    public void DraggingConstrainedRectangleEdgeAlongItsLengthDoesNotMoveTheRectangle()
    {
        var sequence = 0;
        var entities = SketchCreationEntityFactory.CreateEntitiesForTool(
            "twopointrectangle",
            new[] { new Point2(0, 0), new Point2(10, 5) },
            prefix => EntityId.Create($"{prefix}-{++sequence}"),
            isConstruction: false);
        var constraints = SketchCreationConstraintFactory.CreateConstraintsForTool(
            "twopointrectangle",
            entities,
            kind => $"constraint-{kind}-{Guid.NewGuid():N}");
        var document = new DrawingDocument(entities, Array.Empty<SketchDimension>(), constraints);

        var changed = SketchGeometryDragService.TryApplyDrag(
            document,
            "rect-3",
            new Point2(5, 5),
            new Point2(8, 5),
            false,
            out var next,
            out _);

        changed.Should().BeFalse();
        next.Entities.Should().Equal(
            new LineEntity(EntityId.Create("rect-1"), new Point2(0, 0), new Point2(10, 0)),
            new LineEntity(EntityId.Create("rect-2"), new Point2(10, 0), new Point2(10, 5)),
            new LineEntity(EntityId.Create("rect-3"), new Point2(10, 5), new Point2(0, 5)),
            new LineEntity(EntityId.Create("rect-4"), new Point2(0, 5), new Point2(0, 0)));
        next.Constraints.Should().OnlyContain(constraint => constraint.State == SketchConstraintState.Satisfied);
    }

    [Fact]
    public void DraggingUndimensionedRectangleVertexResizesOnlyAdjacentEdges()
    {
        var sequence = 0;
        var entities = SketchCreationEntityFactory.CreateEntitiesForTool(
            "twopointrectangle",
            new[] { new Point2(0, 0), new Point2(10, 5) },
            prefix => EntityId.Create($"{prefix}-{++sequence}"),
            isConstruction: false);
        var constraints = SketchCreationConstraintFactory.CreateConstraintsForTool(
            "twopointrectangle",
            entities,
            kind => $"constraint-{kind}-{Guid.NewGuid():N}");
        var document = new DrawingDocument(entities, Array.Empty<SketchDimension>(), constraints);

        var changed = SketchGeometryDragService.TryApplyDrag(
            document,
            "rect-2|point|end|10|5",
            new Point2(10, 5),
            new Point2(12, 8),
            false,
            out var next,
            out _);

        changed.Should().BeTrue();
        next.Entities.Should().Equal(
            new LineEntity(EntityId.Create("rect-1"), new Point2(0, 0), new Point2(12, 0)),
            new LineEntity(EntityId.Create("rect-2"), new Point2(12, 0), new Point2(12, 8)),
            new LineEntity(EntityId.Create("rect-3"), new Point2(12, 8), new Point2(0, 8)),
            new LineEntity(EntityId.Create("rect-4"), new Point2(0, 8), new Point2(0, 0)));
        next.Constraints.Should().OnlyContain(constraint => constraint.State == SketchConstraintState.Satisfied);
    }

    [Fact]
    public void DraggingUndimensionedRectangleEdgeMidpointResizesOnlyThatSide()
    {
        var sequence = 0;
        var entities = SketchCreationEntityFactory.CreateEntitiesForTool(
            "twopointrectangle",
            new[] { new Point2(0, 0), new Point2(10, 5) },
            prefix => EntityId.Create($"{prefix}-{++sequence}"),
            isConstruction: false);
        var constraints = SketchCreationConstraintFactory.CreateConstraintsForTool(
            "twopointrectangle",
            entities,
            kind => $"constraint-{kind}-{Guid.NewGuid():N}");
        var document = new DrawingDocument(entities, Array.Empty<SketchDimension>(), constraints);

        var changed = SketchGeometryDragService.TryApplyDrag(
            document,
            "rect-4|point|mid|0|2.5",
            new Point2(0, 2.5),
            new Point2(2, 2.5),
            false,
            out var next,
            out _);

        changed.Should().BeTrue();
        next.Entities.Should().Equal(
            new LineEntity(EntityId.Create("rect-1"), new Point2(2, 0), new Point2(10, 0)),
            new LineEntity(EntityId.Create("rect-2"), new Point2(10, 0), new Point2(10, 5)),
            new LineEntity(EntityId.Create("rect-3"), new Point2(10, 5), new Point2(2, 5)),
            new LineEntity(EntityId.Create("rect-4"), new Point2(2, 5), new Point2(2, 0)));
        next.Constraints.Should().OnlyContain(constraint => constraint.State == SketchConstraintState.Satisfied);
    }

    [Fact]
    public void DraggingUndimensionedRectangleEdgeMidpointIgnoresAlongEdgeMotion()
    {
        var sequence = 0;
        var entities = SketchCreationEntityFactory.CreateEntitiesForTool(
            "twopointrectangle",
            new[] { new Point2(0, 0), new Point2(10, 5) },
            prefix => EntityId.Create($"{prefix}-{++sequence}"),
            isConstruction: false);
        var constraints = SketchCreationConstraintFactory.CreateConstraintsForTool(
            "twopointrectangle",
            entities,
            kind => $"constraint-{kind}-{Guid.NewGuid():N}");
        var document = new DrawingDocument(entities, Array.Empty<SketchDimension>(), constraints);

        var changed = SketchGeometryDragService.TryApplyDrag(
            document,
            "rect-4|point|mid|0|2.5",
            new Point2(0, 2.5),
            new Point2(2, 1.5),
            false,
            out var next,
            out _);

        changed.Should().BeTrue();
        next.Entities.Should().Equal(
            new LineEntity(EntityId.Create("rect-1"), new Point2(2, 0), new Point2(10, 0)),
            new LineEntity(EntityId.Create("rect-2"), new Point2(10, 0), new Point2(10, 5)),
            new LineEntity(EntityId.Create("rect-3"), new Point2(10, 5), new Point2(2, 5)),
            new LineEntity(EntityId.Create("rect-4"), new Point2(2, 5), new Point2(2, 0)));
        next.Constraints.Should().OnlyContain(constraint => constraint.State == SketchConstraintState.Satisfied);
    }

    [Fact]
    public void DraggingDimensionedRectangleEdgeTranslatesRectangleAndDimensionAnchors()
    {
        var sequence = 0;
        var dimensionSequence = 0;
        var entities = SketchCreationEntityFactory.CreateEntitiesForTool(
            "twopointrectangle",
            new[] { new Point2(0, 0), new Point2(10, 5) },
            prefix => EntityId.Create($"{prefix}-{++sequence}"),
            isConstruction: false,
            dimensionValues: new Dictionary<string, double> { ["width"] = 10, ["height"] = 5 });
        var constraints = SketchCreationConstraintFactory.CreateConstraintsForTool(
            "twopointrectangle",
            entities,
            kind => $"constraint-{kind}-{Guid.NewGuid():N}");
        var dimensions = SketchCreationDimensionFactory.CreateDimensionsForTool(
            "twopointrectangle",
            entities,
            new Dictionary<string, double> { ["width"] = 10, ["height"] = 5 },
            () => $"dim-{++dimensionSequence}");
        var document = new DrawingDocument(entities, dimensions, constraints);

        var changed = SketchGeometryDragService.TryApplyDrag(
            document,
            "rect-3",
            new Point2(5, 5),
            new Point2(7, 8),
            false,
            out var next,
            out _);

        changed.Should().BeTrue();
        next.Entities.Should().Equal(
            new LineEntity(EntityId.Create("rect-1"), new Point2(2, 3), new Point2(12, 3)),
            new LineEntity(EntityId.Create("rect-2"), new Point2(12, 3), new Point2(12, 8)),
            new LineEntity(EntityId.Create("rect-3"), new Point2(12, 8), new Point2(2, 8)),
            new LineEntity(EntityId.Create("rect-4"), new Point2(2, 8), new Point2(2, 3)));
        next.Dimensions.Select(dimension => dimension.Anchor).Should().Equal(new Point2(7, 4.2), new Point2(11.4, 5.5));
        next.Dimensions.Should().OnlyContain(dimension => SketchDimensionSolverService.IsDimensionSatisfied(next, dimension));
        next.Constraints.Should().OnlyContain(constraint => constraint.State == SketchConstraintState.Satisfied);
    }

    [Fact]
    public void DraggingDimensionedRectangleVertexTranslatesRectangleAndDimensionAnchors()
    {
        var sequence = 0;
        var dimensionSequence = 0;
        var entities = SketchCreationEntityFactory.CreateEntitiesForTool(
            "twopointrectangle",
            new[] { new Point2(0, 0), new Point2(10, 5) },
            prefix => EntityId.Create($"{prefix}-{++sequence}"),
            isConstruction: false,
            dimensionValues: new Dictionary<string, double> { ["width"] = 10, ["height"] = 5 });
        var constraints = SketchCreationConstraintFactory.CreateConstraintsForTool(
            "twopointrectangle",
            entities,
            kind => $"constraint-{kind}-{Guid.NewGuid():N}");
        var dimensions = SketchCreationDimensionFactory.CreateDimensionsForTool(
            "twopointrectangle",
            entities,
            new Dictionary<string, double> { ["width"] = 10, ["height"] = 5 },
            () => $"dim-{++dimensionSequence}");
        var document = new DrawingDocument(entities, dimensions, constraints);

        var changed = SketchGeometryDragService.TryApplyDrag(
            document,
            "rect-2|point|end|10|5",
            new Point2(10, 5),
            new Point2(12, 8),
            false,
            out var next,
            out _);

        changed.Should().BeTrue();
        next.Entities.Should().Equal(
            new LineEntity(EntityId.Create("rect-1"), new Point2(2, 3), new Point2(12, 3)),
            new LineEntity(EntityId.Create("rect-2"), new Point2(12, 3), new Point2(12, 8)),
            new LineEntity(EntityId.Create("rect-3"), new Point2(12, 8), new Point2(2, 8)),
            new LineEntity(EntityId.Create("rect-4"), new Point2(2, 8), new Point2(2, 3)));
        next.Dimensions.Select(dimension => dimension.Anchor).Should().Equal(new Point2(7, 4.2), new Point2(11.4, 5.5));
        next.Dimensions.Should().OnlyContain(dimension => SketchDimensionSolverService.IsDimensionSatisfied(next, dimension));
        next.Constraints.Should().OnlyContain(constraint => constraint.State == SketchConstraintState.Satisfied);
    }

    [Fact]
    public void DraggingPartiallyDimensionedRectangleEdgeInFreeDirectionResizesWithoutDetaching()
    {
        var sequence = 0;
        var dimensionSequence = 0;
        var entities = SketchCreationEntityFactory.CreateEntitiesForTool(
            "twopointrectangle",
            new[] { new Point2(0, 0), new Point2(10, 5) },
            prefix => EntityId.Create($"{prefix}-{++sequence}"),
            isConstruction: false,
            dimensionValues: new Dictionary<string, double> { ["width"] = 10 });
        var constraints = SketchCreationConstraintFactory.CreateConstraintsForTool(
            "twopointrectangle",
            entities,
            kind => $"constraint-{kind}-{Guid.NewGuid():N}");
        var dimensions = SketchCreationDimensionFactory.CreateDimensionsForTool(
            "twopointrectangle",
            entities,
            new Dictionary<string, double> { ["width"] = 10 },
            () => $"dim-{++dimensionSequence}");
        var document = new DrawingDocument(entities, dimensions, constraints);

        var changed = SketchGeometryDragService.TryApplyDrag(
            document,
            "rect-3",
            new Point2(5, 5),
            new Point2(2, 8),
            false,
            out var next,
            out _);

        changed.Should().BeTrue();
        next.Entities.Should().Equal(
            new LineEntity(EntityId.Create("rect-1"), new Point2(-3, 0), new Point2(7, 0)),
            new LineEntity(EntityId.Create("rect-2"), new Point2(7, 0), new Point2(7, 8)),
            new LineEntity(EntityId.Create("rect-3"), new Point2(7, 8), new Point2(-3, 8)),
            new LineEntity(EntityId.Create("rect-4"), new Point2(-3, 8), new Point2(-3, 0)));
        next.Dimensions.Should().ContainSingle().Which.Anchor.Should().Be(new Point2(2, 1.2));
        next.Dimensions.Should().OnlyContain(dimension => SketchDimensionSolverService.IsDimensionSatisfied(next, dimension));
        next.Constraints.Should().OnlyContain(constraint => constraint.State == SketchConstraintState.Satisfied);
    }

    [Fact]
    public void DraggingPartiallyDimensionedRectangleEdgeInConstrainedDirectionTranslatesWithDimensionAnchor()
    {
        var sequence = 0;
        var dimensionSequence = 0;
        var entities = SketchCreationEntityFactory.CreateEntitiesForTool(
            "twopointrectangle",
            new[] { new Point2(0, 0), new Point2(10, 5) },
            prefix => EntityId.Create($"{prefix}-{++sequence}"),
            isConstruction: false,
            dimensionValues: new Dictionary<string, double> { ["width"] = 10 });
        var constraints = SketchCreationConstraintFactory.CreateConstraintsForTool(
            "twopointrectangle",
            entities,
            kind => $"constraint-{kind}-{Guid.NewGuid():N}");
        var dimensions = SketchCreationDimensionFactory.CreateDimensionsForTool(
            "twopointrectangle",
            entities,
            new Dictionary<string, double> { ["width"] = 10 },
            () => $"dim-{++dimensionSequence}");
        var document = new DrawingDocument(entities, dimensions, constraints);

        var changed = SketchGeometryDragService.TryApplyDrag(
            document,
            "rect-4|point|mid|0|2.5",
            new Point2(0, 2.5),
            new Point2(-3, 2.5),
            false,
            out var next,
            out _);

        changed.Should().BeTrue();
        next.Entities.Should().Equal(
            new LineEntity(EntityId.Create("rect-1"), new Point2(-3, 0), new Point2(7, 0)),
            new LineEntity(EntityId.Create("rect-2"), new Point2(7, 0), new Point2(7, 5)),
            new LineEntity(EntityId.Create("rect-3"), new Point2(7, 5), new Point2(-3, 5)),
            new LineEntity(EntityId.Create("rect-4"), new Point2(-3, 5), new Point2(-3, 0)));
        next.Dimensions.Should().ContainSingle().Which.Anchor.Should().Be(new Point2(2, 1.2));
        next.Dimensions.Should().OnlyContain(dimension => SketchDimensionSolverService.IsDimensionSatisfied(next, dimension));
        next.Constraints.Should().OnlyContain(constraint => constraint.State == SketchConstraintState.Satisfied);
    }

    [Fact]
    public void DraggingPartiallyDimensionedRectangleVertexTranslatesConstrainedAxisAndResizesFreeAxis()
    {
        var sequence = 0;
        var dimensionSequence = 0;
        var entities = SketchCreationEntityFactory.CreateEntitiesForTool(
            "twopointrectangle",
            new[] { new Point2(0, 0), new Point2(10, 5) },
            prefix => EntityId.Create($"{prefix}-{++sequence}"),
            isConstruction: false,
            dimensionValues: new Dictionary<string, double> { ["width"] = 10 });
        var constraints = SketchCreationConstraintFactory.CreateConstraintsForTool(
            "twopointrectangle",
            entities,
            kind => $"constraint-{kind}-{Guid.NewGuid():N}");
        var dimensions = SketchCreationDimensionFactory.CreateDimensionsForTool(
            "twopointrectangle",
            entities,
            new Dictionary<string, double> { ["width"] = 10 },
            () => $"dim-{++dimensionSequence}");
        var document = new DrawingDocument(entities, dimensions, constraints);

        var changed = SketchGeometryDragService.TryApplyDrag(
            document,
            "rect-3|point|end|0|5",
            new Point2(0, 5),
            new Point2(-3, 8),
            false,
            out var next,
            out _);

        changed.Should().BeTrue();
        next.Entities.Should().Equal(
            new LineEntity(EntityId.Create("rect-1"), new Point2(-3, 0), new Point2(7, 0)),
            new LineEntity(EntityId.Create("rect-2"), new Point2(7, 0), new Point2(7, 8)),
            new LineEntity(EntityId.Create("rect-3"), new Point2(7, 8), new Point2(-3, 8)),
            new LineEntity(EntityId.Create("rect-4"), new Point2(-3, 8), new Point2(-3, 0)));
        next.Dimensions.Should().ContainSingle().Which.Anchor.Should().Be(new Point2(2, 1.2));
        next.Dimensions.Should().OnlyContain(dimension => SketchDimensionSolverService.IsDimensionSatisfied(next, dimension));
        next.Constraints.Should().OnlyContain(constraint => constraint.State == SketchConstraintState.Satisfied);
    }

    [Fact]
    public void DraggingLineEndpointMovesOnlyThatEndpoint()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("edge"), new Point2(0, 0), new Point2(10, 0))
        });

        var changed = SketchGeometryDragService.TryApplyDrag(
            document,
            "edge|point|start|0|0",
            new Point2(0, 0),
            new Point2(2, 3),
            false,
            out var next,
            out _);

        changed.Should().BeTrue();
        next.Entities.Should().ContainSingle().Which.Should().BeOfType<LineEntity>()
            .Which.Should().Be(new LineEntity(EntityId.Create("edge"), new Point2(2, 3), new Point2(10, 0)));
    }

    [Fact]
    public void DraggingLineEndpointOntoAnotherPointCreatesCoincidentConstraint()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("moving"), new Point2(0, 0), new Point2(1, 0)),
            new LineEntity(EntityId.Create("anchor"), new Point2(5, 5), new Point2(9, 5))
        });

        var changed = SketchGeometryDragService.TryApplyDrag(
            document,
            "moving|point|end|1|0",
            new Point2(1, 0),
            new Point2(5, 5),
            false,
            out var next,
            out var status);

        changed.Should().BeTrue();
        status.Should().Contain("coincident");
        next.Constraints.Should().ContainSingle(constraint =>
            constraint.Kind == SketchConstraintKind.Coincident
            && constraint.State == SketchConstraintState.Satisfied
            && constraint.ReferenceKeys.SequenceEqual(new[] { "anchor:start", "moving:end" }));
    }

    [Fact]
    public void DraggingPerpendicularLineEndpointKeepsRelationSatisfied()
    {
        var document = new DrawingDocument(
            new DrawingEntity[]
            {
                new LineEntity(EntityId.Create("base"), new Point2(0, 0), new Point2(10, 0)),
                new LineEntity(EntityId.Create("upright"), new Point2(10, 0), new Point2(10, 5))
            },
            Array.Empty<SketchDimension>(),
            new[]
            {
                new SketchConstraint("join", SketchConstraintKind.Coincident, new[] { "base:end", "upright:start" }),
                new SketchConstraint("square", SketchConstraintKind.Perpendicular, new[] { "base", "upright" })
            });

        var changed = SketchGeometryDragService.TryApplyDrag(
            document,
            "base|point|end|10|0",
            new Point2(10, 0),
            new Point2(10, 2),
            false,
            out var next,
            out _);

        changed.Should().BeTrue();
        next.Constraints.Should().OnlyContain(constraint => constraint.State == SketchConstraintState.Satisfied);
    }

    [Fact]
    public void DraggingChainedOrthogonalCoincidentEndpointDoesNotCollapseTheDraggedLine()
    {
        var document = new DrawingDocument(
            new DrawingEntity[]
            {
                new LineEntity(EntityId.Create("base"), new Point2(-1.333333, -0.823893), new Point2(7, -0.823893)),
                new LineEntity(EntityId.Create("upright"), new Point2(7, -0.823893), new Point2(7, 3.342774))
            },
            Array.Empty<SketchDimension>(),
            new[]
            {
                new SketchConstraint("base-horizontal", SketchConstraintKind.Horizontal, new[] { "base" }),
                new SketchConstraint("upright-vertical", SketchConstraintKind.Vertical, new[] { "upright" }),
                new SketchConstraint("join", SketchConstraintKind.Coincident, new[] { "base:end", "upright:start" }),
                new SketchConstraint("square", SketchConstraintKind.Perpendicular, new[] { "base", "upright" })
            });

        var changed = SketchGeometryDragService.TryApplyDrag(
            document,
            "base|point|end|7|-0.823893",
            new Point2(7, -0.823893),
            new Point2(9.083333, 0.842774),
            false,
            out var next,
            out _);

        changed.Should().BeTrue();
        next.Entities.Should().BeEquivalentTo(
            new DrawingEntity[]
            {
                new LineEntity(EntityId.Create("base"), new Point2(-1.333333, 0.842774), new Point2(9.083333, 0.842774)),
                new LineEntity(EntityId.Create("upright"), new Point2(9.083333, 0.842774), new Point2(9.083333, 3.342774))
            },
            options => options
                .WithStrictOrdering()
                .Using<double>(context => context.Subject.Should().BeApproximately(context.Expectation, 0.000001))
                .WhenTypeIs<double>());
        next.Constraints.Should().OnlyContain(constraint => constraint.State == SketchConstraintState.Satisfied);
    }

    [Fact]
    public void DraggingLineMidpointTranslatesLine()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("edge"), new Point2(0, 0), new Point2(10, 0))
        });

        SketchGeometryDragService.TryApplyDrag(
            document,
            "edge|point|mid|5|0",
            new Point2(5, 0),
            new Point2(7, 3),
            false,
            out var next,
            out _).Should().BeTrue();

        next.Entities.Should().ContainSingle().Which.Should().BeOfType<LineEntity>()
            .Which.Should().Be(new LineEntity(EntityId.Create("edge"), new Point2(2, 3), new Point2(12, 3)));
    }

    [Fact]
    public void DraggingLineMidpointTranslatesDimensionsReferencingThatLine()
    {
        var document = new DrawingDocument(
            new DrawingEntity[]
            {
                new LineEntity(EntityId.Create("edge"), new Point2(0, 0), new Point2(10, 0))
            },
            new[]
            {
                new SketchDimension(
                    "dim-edge",
                    SketchDimensionKind.LinearDistance,
                    new[] { "edge:start", "edge:end" },
                    10,
                    new Point2(5, 2))
            },
            Array.Empty<SketchConstraint>());

        SketchGeometryDragService.TryApplyDrag(
            document,
            "edge|point|mid|5|0",
            new Point2(5, 0),
            new Point2(7, 3),
            false,
            out var next,
            out _).Should().BeTrue();

        next.Dimensions.Should().ContainSingle().Which.Anchor.Should().Be(new Point2(7, 5));
    }

    [Fact]
    public void DraggingPointToLineDimensionedOffsetLineKeepsDistanceAndAppliesParallelComponent()
    {
        var document = new DrawingDocument(
            new DrawingEntity[]
            {
                new LineEntity(EntityId.Create("base"), new Point2(0, 0), new Point2(10, 0)),
                new LineEntity(EntityId.Create("offset"), new Point2(0, 5), new Point2(10, 5))
            },
            new[]
            {
                new SketchDimension(
                    "offset-distance",
                    SketchDimensionKind.PointToLineDistance,
                    new[] { "base", "offset" },
                    5,
                    new Point2(5, 2.5),
                    isDriving: true)
            },
            Array.Empty<SketchConstraint>());

        SketchGeometryDragService.TryApplyDrag(
            document,
            "offset|point|mid|5|5",
            new Point2(5, 5),
            new Point2(8, 7),
            false,
            out var next,
            out _).Should().BeTrue();

        next.Entities.Should().Equal(
            new LineEntity(EntityId.Create("base"), new Point2(0, 0), new Point2(10, 0)),
            new LineEntity(EntityId.Create("offset"), new Point2(3, 5), new Point2(13, 5)));
        next.Dimensions.Should().ContainSingle().Which.Anchor.Should().Be(new Point2(8, 2.5));
        next.Dimensions.Should().OnlyContain(dimension => SketchDimensionSolverService.IsDimensionSatisfied(next, dimension));
    }

    [Fact]
    public void DraggingSplineFitPointUpdatesFitPointSpline()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            SplineEntity.FromFitPoints(
                EntityId.Create("spline-a"),
                new[] { new Point2(0, 0), new Point2(4, 0), new Point2(8, 2) })
        });

        var changed = SketchGeometryDragService.TryApplyDrag(
            document,
            "spline-a|point|fit-1|4|0",
            new Point2(4, 0),
            new Point2(4, 2),
            false,
            out var next,
            out _);

        changed.Should().BeTrue();
        next.Entities.Should().ContainSingle().Which.Should().BeOfType<SplineEntity>()
            .Which.FitPoints.Should().Equal(new Point2(0, 0), new Point2(4, 2), new Point2(8, 2));
    }

    [Fact]
    public void DraggingSplineEndpointTangentHandleUpdatesHandleWithoutMovingFitPoint()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            SplineEntity.FromFitPoints(
                EntityId.Create("spline-a"),
                new[] { new Point2(0, 0), new Point2(4, 0), new Point2(8, 2) },
                startTangentHandle: new Point2(1, 0),
                endTangentHandle: new Point2(7, 1.5))
        });

        var changed = SketchGeometryDragService.TryApplyDrag(
            document,
            "spline-a|point|tangent-start|1|0",
            new Point2(1, 0),
            new Point2(1, 1),
            false,
            out var next,
            out _);

        changed.Should().BeTrue();
        var spline = next.Entities.Should().ContainSingle().Which.Should().BeOfType<SplineEntity>().Subject;
        spline.FitPoints.Should().Equal(new Point2(0, 0), new Point2(4, 0), new Point2(8, 2));
        spline.StartTangentHandle.Should().Be(new Point2(1, 1));
        spline.EndTangentHandle.Should().Be(new Point2(7, 1.5));
    }

    [Fact]
    public void DraggingSplineFitPointPreservesEndpointTangentHandles()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            SplineEntity.FromFitPoints(
                EntityId.Create("spline-a"),
                new[] { new Point2(0, 0), new Point2(4, 0), new Point2(8, 2) },
                startTangentHandle: new Point2(1, 0),
                endTangentHandle: new Point2(7, 1.5))
        });

        var changed = SketchGeometryDragService.TryApplyDrag(
            document,
            "spline-a|point|fit-1|4|0",
            new Point2(4, 0),
            new Point2(4, 2),
            false,
            out var next,
            out _);

        changed.Should().BeTrue();
        var spline = next.Entities.Should().ContainSingle().Which.Should().BeOfType<SplineEntity>().Subject;
        spline.FitPoints.Should().Equal(new Point2(0, 0), new Point2(4, 2), new Point2(8, 2));
        spline.StartTangentHandle.Should().Be(new Point2(1, 0));
        spline.EndTangentHandle.Should().Be(new Point2(7, 1.5));
    }

    [Fact]
    public void DraggingDimensionedLineEndpointTranslatesLineWhenLengthWouldChange()
    {
        var document = new DrawingDocument(
            new DrawingEntity[]
            {
                new LineEntity(EntityId.Create("edge"), new Point2(0, 0), new Point2(10, 0))
            },
            new[]
            {
                new SketchDimension(
                    "width",
                    SketchDimensionKind.LinearDistance,
                    new[] { "edge:start", "edge:end" },
                    10,
                    new Point2(5, 2),
                    isDriving: true)
            },
            Array.Empty<SketchConstraint>());

        var changed = SketchGeometryDragService.TryApplyDrag(
            document,
            "edge|point|end|10|0",
            new Point2(10, 0),
            new Point2(14, 0),
            false,
            out var next,
            out var status);

        changed.Should().BeTrue(status);
        var line = next.Entities.Should().ContainSingle().Which.Should().BeOfType<LineEntity>().Subject;
        line.Start.Should().Be(new Point2(4, 0));
        line.End.Should().Be(new Point2(14, 0));
        next.Dimensions.Should().ContainSingle().Which.Anchor.Should().Be(new Point2(9, 2));
        next.Dimensions.Should().OnlyContain(dimension => SketchDimensionSolverService.IsDimensionSatisfied(next, dimension));
    }

    [Fact]
    public void ShiftDraggingLineEndpointProjectsOntoCurrentLineVector()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("edge"), new Point2(0, 0), new Point2(10, 0))
        });

        SketchGeometryDragService.TryApplyDrag(
            document,
            "edge|point|end|10|0",
            new Point2(10, 0),
            new Point2(14, 5),
            true,
            out var next,
            out _).Should().BeTrue();

        next.Entities.Should().ContainSingle().Which.Should().BeOfType<LineEntity>()
            .Which.Should().Be(new LineEntity(EntityId.Create("edge"), new Point2(0, 0), new Point2(14, 0)));
    }

    [Fact]
    public void DraggingCircleCenterMovesCircle()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new CircleEntity(EntityId.Create("circle"), new Point2(4, 5), 3)
        });

        SketchGeometryDragService.TryApplyDrag(
            document,
            "circle|point|center|4|5",
            new Point2(4, 5),
            new Point2(6, 8),
            false,
            out var next,
            out _).Should().BeTrue();

        next.Entities.Should().ContainSingle().Which.Should().BeOfType<CircleEntity>()
            .Which.Should().Be(new CircleEntity(EntityId.Create("circle"), new Point2(6, 8), 3));
    }

    [Fact]
    public void DraggingCirclePerimeterChangesRadius()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new CircleEntity(EntityId.Create("circle"), new Point2(0, 0), 3)
        });

        SketchGeometryDragService.TryApplyDrag(
            document,
            "circle|point|quadrant-0|3|0",
            new Point2(3, 0),
            new Point2(5, 0),
            false,
            out var next,
            out _).Should().BeTrue();

        next.Entities.Should().ContainSingle().Which.Should().BeOfType<CircleEntity>()
            .Which.Radius.Should().Be(5);
    }

    [Fact]
    public void DraggingCirclePerimeterWithDrivingRadiusDimensionTranslatesCircleAndDimensionAnchor()
    {
        var document = new DrawingDocument(
            new DrawingEntity[]
            {
                new CircleEntity(EntityId.Create("circle"), new Point2(0, 0), 3)
            },
            new[]
            {
                new SketchDimension(
                    "circle-radius",
                    SketchDimensionKind.Radius,
                    new[] { "circle" },
                    3,
                    new Point2(3, 1),
                    isDriving: true)
            },
            Array.Empty<SketchConstraint>());

        SketchGeometryDragService.TryApplyDrag(
            document,
            "circle|point|quadrant-0|3|0",
            new Point2(3, 0),
            new Point2(5, 0),
            false,
            out var next,
            out _).Should().BeTrue();

        next.Entities.Should().ContainSingle().Which.Should().BeOfType<CircleEntity>()
            .Which.Should().Be(new CircleEntity(EntityId.Create("circle"), new Point2(2, 0), 3));
        next.Dimensions.Should().ContainSingle().Which.Anchor.Should().Be(new Point2(5, 1));
        next.Dimensions.Should().OnlyContain(dimension => SketchDimensionSolverService.IsDimensionSatisfied(next, dimension));
    }

    [Fact]
    public void DraggingEllipseCenterMovesEllipseAndDimensions()
    {
        var document = new DrawingDocument(
            new DrawingEntity[]
            {
                new EllipseEntity(EntityId.Create("ellipse"), new Point2(2, 3), new Point2(4, 0), 0.5)
            },
            new[]
            {
                new SketchDimension(
                    "ellipse-major",
                    SketchDimensionKind.LinearDistance,
                    new[] { "ellipse" },
                    8,
                    new Point2(2, 6))
            },
            Array.Empty<SketchConstraint>());

        SketchGeometryDragService.TryApplyDrag(
            document,
            "ellipse|point|center|2|3",
            new Point2(2, 3),
            new Point2(5, 7),
            false,
            out var next,
            out _).Should().BeTrue();

        var ellipse = next.Entities.Should().ContainSingle().Which.Should().BeOfType<EllipseEntity>().Subject;
        ellipse.Center.Should().Be(new Point2(5, 7));
        ellipse.MajorAxisEndPoint.Should().Be(new Point2(4, 0));
        next.Dimensions.Should().ContainSingle().Which.Anchor.Should().Be(new Point2(5, 10));
    }

    [Fact]
    public void DraggingEllipseMajorQuadrantChangesMajorAxis()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new EllipseEntity(EntityId.Create("ellipse"), new Point2(0, 0), new Point2(4, 0), 0.5)
        });

        SketchGeometryDragService.TryApplyDrag(
            document,
            "ellipse|point|quadrant-0|4|0",
            new Point2(4, 0),
            new Point2(6, 0),
            false,
            out var next,
            out _).Should().BeTrue();

        var ellipse = next.Entities.Should().ContainSingle().Which.Should().BeOfType<EllipseEntity>().Subject;
        ellipse.MajorAxisEndPoint.Should().Be(new Point2(6, 0));
        ellipse.MinorRadiusRatio.Should().Be(0.5);
    }

    [Fact]
    public void DraggingEllipseMajorQuadrantWithDrivingAxisDimensionTranslatesEllipseAndDimensionAnchor()
    {
        var document = new DrawingDocument(
            new DrawingEntity[]
            {
                new EllipseEntity(EntityId.Create("ellipse"), new Point2(0, 0), new Point2(4, 0), 0.5)
            },
            new[]
            {
                new SketchDimension(
                    "ellipse-major",
                    SketchDimensionKind.LinearDistance,
                    new[] { "ellipse|point|major-start|-4|0", "ellipse|point|major-end|4|0" },
                    8,
                    new Point2(0, 3),
                    isDriving: true)
            },
            Array.Empty<SketchConstraint>());

        SketchGeometryDragService.TryApplyDrag(
            document,
            "ellipse|point|quadrant-0|4|0",
            new Point2(4, 0),
            new Point2(6, 0),
            false,
            out var next,
            out _).Should().BeTrue();

        var ellipse = next.Entities.Should().ContainSingle().Which.Should().BeOfType<EllipseEntity>().Subject;
        ellipse.Center.Should().Be(new Point2(2, 0));
        ellipse.MajorAxisEndPoint.Should().Be(new Point2(4, 0));
        ellipse.MinorRadiusRatio.Should().Be(0.5);
        next.Dimensions.Should().ContainSingle().Which.Anchor.Should().Be(new Point2(2, 3));
        next.Dimensions.Should().OnlyContain(dimension => SketchDimensionSolverService.IsDimensionSatisfied(next, dimension));
    }

    [Fact]
    public void DraggingFullEllipseStartPointChangesMajorAxis()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new EllipseEntity(EntityId.Create("ellipse"), new Point2(0, 0), new Point2(4, 0), 0.5)
        });

        SketchGeometryDragService.TryApplyDrag(
            document,
            "ellipse|point|start|4|0",
            new Point2(4, 0),
            new Point2(6, 0),
            false,
            out var next,
            out _).Should().BeTrue();

        next.Entities.Should().ContainSingle().Which.Should().BeOfType<EllipseEntity>()
            .Which.MajorAxisEndPoint.Should().Be(new Point2(6, 0));
    }

    [Fact]
    public void DraggingEllipseMinorQuadrantChangesMinorRatio()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new EllipseEntity(EntityId.Create("ellipse"), new Point2(0, 0), new Point2(4, 0), 0.5)
        });

        SketchGeometryDragService.TryApplyDrag(
            document,
            "ellipse|point|quadrant-90|0|2",
            new Point2(0, 2),
            new Point2(0, 3),
            false,
            out var next,
            out _).Should().BeTrue();

        next.Entities.Should().ContainSingle().Which.Should().BeOfType<EllipseEntity>()
            .Which.MinorRadiusRatio.Should().BeApproximately(0.75, 0.000001);
    }

    [Fact]
    public void DraggingArcPerimeterChangesRadius()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new ArcEntity(EntityId.Create("arc"), new Point2(0, 0), 3, 0, 90)
        });

        SketchGeometryDragService.TryApplyDrag(
            document,
            "arc|point|mid|2.12132|2.12132",
            new Point2(2.12132, 2.12132),
            new Point2(4, 0),
            false,
            out var next,
            out _).Should().BeTrue();

        next.Entities.Should().ContainSingle().Which.Should().BeOfType<ArcEntity>()
            .Which.Radius.Should().Be(4);
    }

    [Fact]
    public void DraggingArcEndpointChangesSweepAndRadius()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new ArcEntity(EntityId.Create("arc"), new Point2(0, 0), 3, 0, 90)
        });

        SketchGeometryDragService.TryApplyDrag(
            document,
            "arc|point|end|0|3",
            new Point2(0, 3),
            new Point2(4, 4),
            false,
            out var next,
            out _).Should().BeTrue();

        var arc = next.Entities.Should().ContainSingle().Which.Should().BeOfType<ArcEntity>().Which;
        arc.Radius.Should().BeApproximately(Math.Sqrt(32), 0.000001);
        arc.EndAngleDegrees.Should().BeApproximately(45, 0.000001);
    }

    [Fact]
    public void ShiftDraggingArcEndpointOnlyChangesSweep()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new ArcEntity(EntityId.Create("arc"), new Point2(0, 0), 3, 0, 90)
        });

        SketchGeometryDragService.TryApplyDrag(
            document,
            "arc|point|end|0|3",
            new Point2(0, 3),
            new Point2(4, 4),
            true,
            out var next,
            out _).Should().BeTrue();

        var arc = next.Entities.Should().ContainSingle().Which.Should().BeOfType<ArcEntity>().Which;
        arc.Radius.Should().Be(3);
        arc.EndAngleDegrees.Should().BeApproximately(45, 0.000001);
    }

    [Fact]
    public void DraggingArcEndpointWithDrivingSweepDimensionTranslatesArcAndDimensionAnchor()
    {
        var document = new DrawingDocument(
            new DrawingEntity[]
            {
                new ArcEntity(EntityId.Create("arc"), new Point2(0, 0), 3, 0, 90)
            },
            new[]
            {
                new SketchDimension(
                    "sweep",
                    SketchDimensionKind.Angle,
                    new[] { "arc" },
                    90,
                    new Point2(2, 2),
                    isDriving: true)
            },
            Array.Empty<SketchConstraint>());

        SketchGeometryDragService.TryApplyDrag(
            document,
            "arc|point|end|0|3",
            new Point2(0, 3),
            new Point2(1, 4),
            false,
            out var next,
            out _).Should().BeTrue();

        var arc = next.Entities.Should().ContainSingle().Which.Should().BeOfType<ArcEntity>().Subject;
        arc.Center.Should().Be(new Point2(1, 1));
        arc.Radius.Should().Be(3);
        arc.StartAngleDegrees.Should().Be(0);
        arc.EndAngleDegrees.Should().Be(90);
        next.Dimensions.Should().ContainSingle().Which.Anchor.Should().Be(new Point2(3, 3));
        next.Dimensions.Should().OnlyContain(dimension => SketchDimensionSolverService.IsDimensionSatisfied(next, dimension));
    }

    [Fact]
    public void DraggingPolylineVertexMovesOnlyThatVertex()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new PolylineEntity(
                EntityId.Create("poly"),
                new[] { new Point2(0, 0), new Point2(10, 0), new Point2(20, 0) })
        });

        SketchGeometryDragService.TryApplyDrag(
            document,
            "poly|point|vertex-1|10|0",
            new Point2(10, 0),
            new Point2(10, 4),
            false,
            out var next,
            out _).Should().BeTrue();

        next.Entities.Should().ContainSingle().Which.Should().BeOfType<PolylineEntity>()
            .Which.Vertices.Should().Equal(new Point2(0, 0), new Point2(10, 4), new Point2(20, 0));
    }

    [Fact]
    public void DraggingWholePolygonTranslatesCenterAndDimensionAnchors()
    {
        var document = new DrawingDocument(
            new DrawingEntity[]
            {
                new PolygonEntity(EntityId.Create("polygon"), new Point2(0, 0), 5, 30, 6)
            },
            new[]
            {
                new SketchDimension(
                    "polygon-radius",
                    SketchDimensionKind.Radius,
                    new[] { "polygon" },
                    5,
                    new Point2(5, 0)),
                new SketchDimension(
                    "polygon-count",
                    SketchDimensionKind.Count,
                    new[] { "polygon" },
                    6,
                    new Point2(0, -3))
            },
            Array.Empty<SketchConstraint>());

        SketchGeometryDragService.TryApplyDrag(
            document,
            "polygon",
            new Point2(5, 0),
            new Point2(8, 4),
            false,
            out var next,
            out _).Should().BeTrue();

        var polygon = next.Entities.Should().ContainSingle().Which.Should().BeOfType<PolygonEntity>().Subject;
        polygon.Center.Should().Be(new Point2(3, 4));
        polygon.Radius.Should().Be(5);
        polygon.RotationAngleDegrees.Should().Be(30);
        polygon.NormalizedSideCount.Should().Be(6);
        next.Dimensions.Select(dimension => dimension.Anchor).Should().Equal(new Point2(8, 4), new Point2(3, 1));
    }

    [Fact]
    public void DraggingPolygonCenterTranslatesCenterAndDimensionAnchors()
    {
        var document = new DrawingDocument(
            new DrawingEntity[]
            {
                new PolygonEntity(EntityId.Create("polygon"), new Point2(0, 0), 5, 30, 6)
            },
            new[]
            {
                new SketchDimension(
                    "polygon-radius",
                    SketchDimensionKind.Radius,
                    new[] { "polygon" },
                    5,
                    new Point2(5, 0))
            },
            Array.Empty<SketchConstraint>());

        SketchGeometryDragService.TryApplyDrag(
            document,
            "polygon|point|center|0|0",
            new Point2(0, 0),
            new Point2(3, 4),
            false,
            out var next,
            out _).Should().BeTrue();

        var polygon = next.Entities.Should().ContainSingle().Which.Should().BeOfType<PolygonEntity>().Subject;
        polygon.Center.Should().Be(new Point2(3, 4));
        polygon.Radius.Should().Be(5);
        polygon.RotationAngleDegrees.Should().Be(30);
        next.Dimensions.Should().ContainSingle().Which.Anchor.Should().Be(new Point2(8, 4));
    }

    [Fact]
    public void DraggingPolygonMidpointTranslatesCenterAndDimensionAnchors()
    {
        var document = new DrawingDocument(
            new DrawingEntity[]
            {
                new PolygonEntity(EntityId.Create("polygon"), new Point2(0, 0), 5, 30, 6)
            },
            new[]
            {
                new SketchDimension(
                    "polygon-radius",
                    SketchDimensionKind.Radius,
                    new[] { "polygon" },
                    5,
                    new Point2(5, 0))
            },
            Array.Empty<SketchConstraint>());

        SketchGeometryDragService.TryApplyDrag(
            document,
            "polygon|point|mid-0|3.75|2.165064",
            new Point2(3.75, 2.165064),
            new Point2(6.75, 6.165064),
            false,
            out var next,
            out _).Should().BeTrue();

        var polygon = next.Entities.Should().ContainSingle().Which.Should().BeOfType<PolygonEntity>().Subject;
        polygon.Center.Should().Be(new Point2(3, 4));
        polygon.Radius.Should().Be(5);
        polygon.RotationAngleDegrees.Should().Be(30);
        next.Dimensions.Should().ContainSingle().Which.Anchor.Should().Be(new Point2(8, 4));
    }

    [Fact]
    public void DraggingEqualLineEndpointUpdatesPeerLineLength()
    {
        var document = new DrawingDocument(
            new DrawingEntity[]
            {
                new LineEntity(EntityId.Create("anchor"), new Point2(0, 0), new Point2(5, 0)),
                new LineEntity(EntityId.Create("peer"), new Point2(10, 0), new Point2(15, 0))
            },
            Array.Empty<SketchDimension>(),
            new[]
            {
                new SketchConstraint("equal-lines", SketchConstraintKind.Equal, new[] { "anchor", "peer" }, SketchConstraintState.Satisfied)
            });

        var changed = SketchGeometryDragService.TryApplyDrag(
            document,
            "anchor|point|end|5|0",
            new Point2(5, 0),
            new Point2(8, 0),
            false,
            out var next,
            out _);

        changed.Should().BeTrue();
        next.Entities.Should().Equal(
            new LineEntity(EntityId.Create("anchor"), new Point2(0, 0), new Point2(8, 0)),
            new LineEntity(EntityId.Create("peer"), new Point2(10, 0), new Point2(18, 0)));
        next.Constraints.Should().ContainSingle().Which.State.Should().Be(SketchConstraintState.Satisfied);
    }

    [Fact]
    public void DraggingEqualCircleRadiusUpdatesPeerRadius()
    {
        var document = new DrawingDocument(
            new DrawingEntity[]
            {
                new CircleEntity(EntityId.Create("anchor"), new Point2(0, 0), 3),
                new CircleEntity(EntityId.Create("peer"), new Point2(10, 0), 3)
            },
            Array.Empty<SketchDimension>(),
            new[]
            {
                new SketchConstraint("equal-radii", SketchConstraintKind.Equal, new[] { "anchor", "peer" }, SketchConstraintState.Satisfied)
            });

        SketchGeometryDragService.TryApplyDrag(
            document,
            "anchor|point|quadrant-0|3|0",
            new Point2(3, 0),
            new Point2(5, 0),
            false,
            out var next,
            out _).Should().BeTrue();

        next.Entities.Should().Equal(
            new CircleEntity(EntityId.Create("anchor"), new Point2(0, 0), 5),
            new CircleEntity(EntityId.Create("peer"), new Point2(10, 0), 5));
        next.Constraints.Should().ContainSingle().Which.State.Should().Be(SketchConstraintState.Satisfied);
    }

    [Fact]
    public void DraggingConcentricCircleCenterMovesPeerCenter()
    {
        var document = new DrawingDocument(
            new DrawingEntity[]
            {
                new CircleEntity(EntityId.Create("anchor"), new Point2(0, 0), 3),
                new CircleEntity(EntityId.Create("peer"), new Point2(0, 0), 5)
            },
            Array.Empty<SketchDimension>(),
            new[]
            {
                new SketchConstraint("concentric", SketchConstraintKind.Concentric, new[] { "anchor", "peer" }, SketchConstraintState.Satisfied)
            });

        SketchGeometryDragService.TryApplyDrag(
            document,
            "anchor|point|center|0|0",
            new Point2(0, 0),
            new Point2(2, 3),
            false,
            out var next,
            out _).Should().BeTrue();

        next.Entities.Should().Equal(
            new CircleEntity(EntityId.Create("anchor"), new Point2(2, 3), 3),
            new CircleEntity(EntityId.Create("peer"), new Point2(2, 3), 5));
        next.Constraints.Should().ContainSingle().Which.State.Should().Be(SketchConstraintState.Satisfied);
    }

    [Fact]
    public void DraggingLineWithMidpointConstraintMovesMidpointReference()
    {
        var document = new DrawingDocument(
            new DrawingEntity[]
            {
                new LineEntity(EntityId.Create("baseline"), new Point2(0, 0), new Point2(10, 0)),
                new PointEntity(EntityId.Create("marker"), new Point2(5, 0))
            },
            Array.Empty<SketchDimension>(),
            new[]
            {
                new SketchConstraint("midpoint", SketchConstraintKind.Midpoint, new[] { "baseline", "marker" }, SketchConstraintState.Satisfied)
            });

        SketchGeometryDragService.TryApplyDrag(
            document,
            "baseline|point|end|10|0",
            new Point2(10, 0),
            new Point2(12, 4),
            false,
            out var next,
            out _).Should().BeTrue();

        next.Entities.Should().Equal(
            new LineEntity(EntityId.Create("baseline"), new Point2(0, 0), new Point2(12, 4)),
            new PointEntity(EntityId.Create("marker"), new Point2(6, 2)));
        next.Constraints.Should().ContainSingle().Which.State.Should().Be(SketchConstraintState.Satisfied);
    }

    [Fact]
    public void DraggingTangentCircleRadiusMovesPeerLine()
    {
        var document = new DrawingDocument(
            new DrawingEntity[]
            {
                new LineEntity(EntityId.Create("edge"), new Point2(-5, 3), new Point2(5, 3)),
                new CircleEntity(EntityId.Create("circle"), new Point2(0, 0), 3)
            },
            Array.Empty<SketchDimension>(),
            new[]
            {
                new SketchConstraint("tangent", SketchConstraintKind.Tangent, new[] { "edge", "circle" }, SketchConstraintState.Satisfied)
            });

        SketchGeometryDragService.TryApplyDrag(
            document,
            "circle|point|quadrant-0|3|0",
            new Point2(3, 0),
            new Point2(5, 0),
            false,
            out var next,
            out _).Should().BeTrue();

        next.Entities.Should().Equal(
            new LineEntity(EntityId.Create("edge"), new Point2(-5, 5), new Point2(5, 5)),
            new CircleEntity(EntityId.Create("circle"), new Point2(0, 0), 5));
        next.Constraints.Should().ContainSingle().Which.State.Should().Be(SketchConstraintState.Satisfied);
    }

    [Fact]
    public void FixedLineDoesNotDrag()
    {
        var document = new DrawingDocument(
            new DrawingEntity[]
            {
                new LineEntity(EntityId.Create("edge"), new Point2(0, 0), new Point2(10, 0))
            },
            Array.Empty<SketchDimension>(),
            new[]
            {
                new SketchConstraint("fix-edge", SketchConstraintKind.Fix, new[] { "edge" })
            });

        var changed = SketchGeometryDragService.TryApplyDrag(
            document,
            "edge|point|mid|5|0",
            new Point2(5, 0),
            new Point2(5, 4),
            false,
            out var next,
            out var status);

        changed.Should().BeFalse();
        next.Should().BeSameAs(document);
        status.Should().Contain("constrained");
    }
}

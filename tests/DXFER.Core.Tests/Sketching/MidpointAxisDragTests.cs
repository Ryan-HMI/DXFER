using DXFER.Blazor.Sketching;
using DXFER.CadIO;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Operations;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class MidpointAxisDragTests
{
    public static IEnumerable<object[]> PickCases()
    {
        foreach (var polyline in new[] { false, true })
        foreach (var reverse in new[] { false, true })
        foreach (var raw in new[] { false, true })
            yield return [polyline, reverse, raw];
    }

    public static IEnumerable<object[]> DragCases()
    {
        foreach (var pick in PickCases())
        foreach (var diagonal in new[] { false, true })
        foreach (var reopen in new[] { false, true })
        foreach (var diameter in new[] { false, true })
            yield return pick.Concat(new object[] { diagonal, reopen, diameter }).ToArray();
    }

    public static IEnumerable<object[]> FixedCases()
    {
        foreach (var pick in PickCases())
        foreach (var whole in new[] { false, true })
            yield return pick.Append((object)whole).ToArray();
    }

    [Theory]
    [MemberData(nameof(DragCases))]
    public void HorizontalCircleDragPreservesReadOnlyMidpointAndProjectsPointer(
        bool polyline, bool reverse, bool raw, bool diagonal, bool reopen, bool diameter)
    {
        var document = Create(polyline, reverse, raw, diameter);
        if (reopen)
            document = DxfDocumentReader.Read(DxfDocumentWriter.Write(document, new(IncludeNativeDocument: true)));
        var snapshot = DxfDocumentWriter.Write(document, new(IncludeNativeDocument: true));

        var accepted = SketchGeometryDragService.TryApplyDrag(document, "circle|point|center|8|5",
            new(8, 5), new(11, diagonal ? 9 : 5), false, out var result, out var status, false);

        accepted.Should().BeTrue(status);
        result.Entities.Single(e => e.Id.Value == "source").Should().Be(document.Entities.Single(e => e.Id.Value == "source"));
        var circle = result.Entities.OfType<CircleEntity>().Single();
        circle.Center.Should().Be(new Point2(11, 5));
        circle.Radius.Should().Be(3);
        result.Constraints.Should().BeEquivalentTo(document.Constraints);
        result.Constraints.Should().OnlyContain(c => SketchConstraintService.ValidateConstraint(result, c).State == SketchConstraintState.Satisfied);
        var dimension = result.Dimensions.Single();
        dimension.Id.Should().Be("size");
        dimension.Kind.Should().Be(diameter ? SketchDimensionKind.Diameter : SketchDimensionKind.Radius);
        dimension.Value.Should().Be(diameter ? 6 : 3);
        dimension.IsDriving.Should().BeTrue();
        dimension.ReferenceKeys.Should().Equal("circle");
        SketchDimensionSolverService.IsDimensionSatisfied(result, dimension).Should().BeTrue();
        DxfDocumentWriter.Write(document, new(IncludeNativeDocument: true)).Should().Be(snapshot, "drag must not mutate the history snapshot");
        var persisted = DxfDocumentReader.Read(DxfDocumentWriter.Write(result, new(IncludeNativeDocument: true)));
        persisted.Entities.Should().BeEquivalentTo(result.Entities);
        persisted.Constraints.Should().BeEquivalentTo(result.Constraints);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void FixedSourceDoesNotBlockFreeCircleSlidingAlongHorizontalAxis(bool polyline, bool diagonal)
    {
        var original = Create(polyline, false, true);
        var document = new DrawingDocument(original.Entities, original.Dimensions,
            original.Constraints.Append(new SketchConstraint("fixed-source", SketchConstraintKind.Fix,
                ["source"], SketchConstraintState.Satisfied)), original.Metadata);

        SketchGeometryDragService.TryApplyDrag(document, "circle|point|center|8|5", new(8, 5),
            new(11, diagonal ? 9 : 5), false, out var result, out var status, false).Should().BeTrue(status);

        result.Entities.Single(e => e.Id.Value == "source").Should().Be(document.Entities.Single(e => e.Id.Value == "source"));
        var circle = result.Entities.OfType<CircleEntity>().Single();
        circle.Center.Should().Be(new Point2(11, 5));
        circle.Radius.Should().Be(3);
        result.Constraints.Should().BeEquivalentTo(document.Constraints);
        result.Constraints.Should().OnlyContain(c => SketchConstraintService.ValidateConstraint(result, c).State == SketchConstraintState.Satisfied);
        SketchDimensionSolverService.IsDimensionSatisfied(result, result.Dimensions.Single()).Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(FixedCases))]
    public void FixedCircleCenterOrWholeCircleRefusesWithoutMovingMidpointSource(
        bool polyline, bool reverse, bool raw, bool whole)
    {
        var original = Create(polyline, reverse, raw);
        var document = new DrawingDocument(original.Entities, original.Dimensions,
            original.Constraints.Append(new SketchConstraint("fixed", SketchConstraintKind.Fix,
                [whole ? "circle" : "circle:center"], SketchConstraintState.Satisfied)), original.Metadata);

        SketchGeometryDragService.TryApplyDrag(document, "circle|point|center|8|5", new(8, 5), new(11, 5),
            false, out var result, out _, false).Should().BeFalse();

        result.Entities.Should().Equal(document.Entities);
        result.Constraints.Should().Equal(document.Constraints);
        result.Dimensions.Should().Equal(document.Dimensions);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void VerticalCircleDragKeepsXAndProjectsDiagonalPointer(bool polyline, bool diagonal)
    {
        var document = Create(polyline, true, true, diameter: true, vertical: true);
        SketchGeometryDragService.TryApplyDrag(document, "circle|point|center|2|12", new(2, 12),
            new(diagonal ? 6 : 2, 15), false, out var result, out var status, false).Should().BeTrue(status);
        result.Entities.Single(e => e.Id.Value == "source").Should().Be(document.Entities.Single(e => e.Id.Value == "source"));
        var circle = result.Entities.OfType<CircleEntity>().Single();
        circle.Center.Should().Be(new Point2(2, 15));
        circle.Radius.Should().Be(3);
        result.Constraints.Should().BeEquivalentTo(document.Constraints);
        SketchDimensionSolverService.IsDimensionSatisfied(result, result.Dimensions.Single()).Should().BeTrue();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void PureBlockedAxisMotionRefusesAndReturnsOriginalHistoryDocument(bool polyline, bool vertical)
    {
        var document = Create(polyline, false, false, vertical: vertical);
        var from = vertical ? new Point2(2, 12) : new Point2(8, 5);
        var to = vertical ? new Point2(6, 12) : new Point2(8, 9);
        var key = vertical ? "circle|point|center|2|12" : "circle|point|center|8|5";
        AssertRefusedUnchanged(document, key, from, to);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FreeRadiusGripStillResizesRatherThanTranslatingCenter(bool polyline)
    {
        var original = Create(polyline, false, true);
        var document = new DrawingDocument(original.Entities, [], original.Constraints, original.Metadata);
        SketchGeometryDragService.TryApplyDrag(document, "circle|point|quadrant-0|11|5", new(11, 5), new(12, 5),
            false, out var result, out var status, false).Should().BeTrue(status);
        var circle = result.Entities.OfType<CircleEntity>().Single();
        circle.Center.Should().Be(new Point2(8, 5));
        circle.Radius.Should().Be(4);
        result.Entities.Single(e => e.Id.Value == "source").Should().Be(document.Entities.Single(e => e.Id.Value == "source"));
        result.Constraints.Should().BeEquivalentTo(document.Constraints);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BothAxisConstraintsRefuseDiagonalDragInsteadOfDroppingOne(bool polyline)
    {
        var original = Create(polyline, false, false);
        var midpoint = polyline ? "source|segment|0:mid" : "source:mid";
        var entities = original.Entities.Select(e => e is CircleEntity circle ? circle with { Center = new(2, 5) } : e);
        var document = new DrawingDocument(entities, original.Dimensions,
            original.Constraints.Append(new SketchConstraint("vertical", SketchConstraintKind.Vertical,
                ["circle:center", midpoint], SketchConstraintState.Satisfied)), original.Metadata);
        document.Constraints.Should().OnlyContain(c => SketchConstraintService.ValidateConstraint(document, c).State == SketchConstraintState.Satisfied);
        AssertRefusedUnchanged(document, "circle|point|center|2|5", new(2, 5), new(6, 9));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DrivingXDistanceRefusesOtherwiseFreeHorizontalTranslation(bool polyline)
    {
        var original = Create(polyline, false, false);
        var midpoint = polyline ? "source|segment|0:mid" : "source:mid";
        var x = new SketchDimension("x", SketchDimensionKind.HorizontalDistance, [midpoint, "circle:center"], 6, new(5, 3), true);
        var document = new DrawingDocument(original.Entities, original.Dimensions.Append(x), original.Constraints, original.Metadata);
        SketchDimensionSolverService.IsDimensionSatisfied(document, x).Should().BeTrue();
        AssertRefusedUnchanged(document, "circle|point|center|8|5", new(8, 5), new(11, 9));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FixedCenterRefusesProjectedDiagonalDrag(bool vertical)
    {
        var original = Create(false, false, false, vertical: vertical);
        var document = new DrawingDocument(original.Entities, original.Dimensions,
            original.Constraints.Append(new SketchConstraint("fixed", SketchConstraintKind.Fix,
                ["circle:center"], SketchConstraintState.Satisfied)), original.Metadata);
        AssertRefusedUnchanged(document, vertical ? "circle|point|center|2|12" : "circle|point|center|8|5",
            vertical ? new(2, 12) : new(8, 5), vertical ? new(6, 15) : new(11, 9));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LockedCircleIsRejectedAtProtectedGeometryDocumentBoundary(bool vertical)
    {
        var original = Create(false, false, false, vertical: vertical);
        var document = new DrawingDocument(original.Entities, original.Dimensions, original.Constraints, original.Metadata with
        {
            EntityStyles = new Dictionary<string, DxfEntityStyle> { ["circle"] = new("Locked") },
            Layers = new Dictionary<string, DxfEntityStyle> { ["Locked"] = new("Locked", IsLocked: true) }
        });
        var before = DxfDocumentWriter.Write(document, new(IncludeNativeDocument: true));
        SketchGeometryDragService.TryApplyDrag(document, vertical ? "circle|point|center|2|12" : "circle|point|center|8|5",
            vertical ? new(2, 12) : new(8, 5), vertical ? new(6, 15) : new(11, 9), false,
            out var candidate, out var status, false).Should().BeTrue(status);
        DrawingLayerService.PreservesProtectedGeometry(document, candidate).Should().BeFalse();
        DxfDocumentWriter.Write(document, new(IncludeNativeDocument: true)).Should().Be(before);
    }

    private static void AssertRefusedUnchanged(DrawingDocument document, string key, Point2 from, Point2 to)
    {
        var before = DxfDocumentWriter.Write(document, new(IncludeNativeDocument: true));
        SketchGeometryDragService.TryApplyDrag(document, key, from, to, false, out var result, out _, false).Should().BeFalse();
        result.Should().BeSameAs(document, "refusal must not create an accepted history entry");
        DxfDocumentWriter.Write(result, new(IncludeNativeDocument: true)).Should().Be(before);
    }

    private static DrawingDocument Create(bool polyline, bool reverse, bool raw, bool diameter = false, bool vertical = false)
    {
        DrawingEntity source = polyline
            ? new PolylineEntity(EntityId.Create("source"), [new(2, 0), new(2, 10), new(9, 10)])
            : new LineEntity(EntityId.Create("source"), new(2, 0), new(2, 10));
        var document = new DrawingDocument([source, new CircleEntity(EntityId.Create("circle"), vertical ? new(2, 12) : new(8, 5), 3)],
            [new SketchDimension("size", diameter ? SketchDimensionKind.Diameter : SketchDimensionKind.Radius,
                ["circle"], diameter ? 6 : 3, new(12, 4), true)], []);
        var midpoint = polyline ? "source|segment|0:mid" : "source:mid";
        var picks = raw
            ? new[] { polyline ? "source|point|mid-0|999|999" : "source|point|mid|999|999", "circle|point|center|999|999" }
            : new[] { midpoint, "circle:center" };
        if (reverse) Array.Reverse(picks);
        SketchCommandFactory.TryBuildConstraint(document, picks, vertical ? SketchConstraintKind.Vertical : SketchConstraintKind.Horizontal, "axis",
            out var axis, out var reason).Should().BeTrue(reason);
        axis.ReferenceKeys.Should().Contain(midpoint);
        var solved = new LegacySketchSolverAdapter().Solve(new SketchSolveRequest(document, [axis], []));
        solved.Status.Should().Be(SketchSolveStatus.Solved);
        solved.Document.Entities.Should().Equal(document.Entities);
        return solved.Document;
    }
}

using DXFER.Blazor.Sketching;
using DXFER.CadIO;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Operations;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class MidpointOriginAxisTests
{
    public static IEnumerable<object[]> CreationCases()
    {
        foreach (var rectangle in new[] { false, true })
        foreach (var vertical in new[] { false, true })
        foreach (var reverse in new[] { false, true })
        foreach (var raw in new[] { false, true })
            yield return [rectangle, vertical, reverse, raw];
    }

    [Theory]
    [MemberData(nameof(CreationCases))]
    public void MidpointOriginAxisTranslatesLineOrJoinedRectangleWithoutChangingShape(
        bool rectangle, bool vertical, bool reverse, bool raw)
    {
        var document = Create(rectangle);
        var snapshot = DxfDocumentWriter.Write(document, new(IncludeNativeDocument: true));
        var axis = Build(document, vertical, reverse, raw);
        var result = Solve(document, axis);

        result.Status.Should().Be(SketchSolveStatus.Solved);
        AssertTranslation(document, result.Document, vertical ? new(-2, 0) : new(0, -5));
        AssertValid(result.Document);
        result.Document.Constraints.Single(c => c.Id == "origin-axis").ReferenceKeys.Should().Equal(axis.ReferenceKeys);
        DxfDocumentWriter.Write(document, new(IncludeNativeDocument: true)).Should().Be(snapshot);
        var reopened = DxfDocumentReader.Read(DxfDocumentWriter.Write(result.Document, new(IncludeNativeDocument: true)));
        reopened.Entities.Should().BeEquivalentTo(result.Document.Entities);
        reopened.Constraints.Should().BeEquivalentTo(result.Document.Constraints);
        AssertValid(reopened);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public void FixedWholeEdgeOrEndpointRefusesIncompatibleOriginAxis(bool rectangle, bool vertical, bool endpoint)
    {
        var original = Create(rectangle);
        var document = new DrawingDocument(original.Entities, original.Dimensions,
            original.Constraints.Append(Relation("fixed", SketchConstraintKind.Fix, endpoint ? "edge:start" : "edge")));
        var result = Solve(document, Build(document, vertical, true, true));
        result.Status.Should().NotBe(SketchSolveStatus.Solved);
        result.Document.Entities.Should().Equal(document.Entities);
        result.Document.Dimensions.Should().Equal(document.Dimensions);
        result.Document.Constraints.Single(c => c.Id == "origin-axis").State.Should().Be(SketchConstraintState.Unsatisfied);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void DrivingMidpointCoordinateCannotBeSilentlyOverridden(bool rectangle, bool vertical)
    {
        var original = Create(rectangle);
        var position = new SketchDimension("position", vertical ? SketchDimensionKind.HorizontalDistance : SketchDimensionKind.VerticalDistance,
            ["$origin:center", "edge:mid"], vertical ? 2 : 5, null, true);
        var document = new DrawingDocument(original.Entities, original.Dimensions.Append(position), original.Constraints);
        SketchDimensionSolverService.IsDimensionSatisfied(document, position).Should().BeTrue();
        var result = Solve(document, Build(document, vertical, false, false));
        result.Status.Should().NotBe(SketchSolveStatus.Solved);
        result.Document.Entities.Should().Equal(document.Entities);
        result.Document.Dimensions.Should().Equal(document.Dimensions);
        result.Document.Constraints.Single(c => c.Id == "origin-axis").State.Should().Be(SketchConstraintState.Unsatisfied);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void AlignedRelationSurvivesNativeReopenAndAlongAxisDrag(bool rectangle, bool vertical)
    {
        var original = Create(rectangle, vertical ? new(-2, 0) : new(0, -5));
        var solved = Solve(original, Build(original, vertical, false, true));
        solved.Status.Should().Be(SketchSolveStatus.Solved);
        var document = DxfDocumentReader.Read(DxfDocumentWriter.Write(solved.Document, new(IncludeNativeDocument: true)));
        var edge = document.Entities.OfType<LineEntity>().Single(e => e.Id.Value == "edge");
        var start = new Point2((edge.Start.X + edge.End.X) / 2, (edge.Start.Y + edge.End.Y) / 2);
        var delta = vertical ? new Point2(0, 3) : new Point2(3, 0);
        SketchGeometryDragService.TryApplyDrag(document, "edge", start, Add(start, delta), false,
            out var moved, out var status, false).Should().BeTrue(status);
        AssertTranslation(document, moved, delta);
        AssertValid(moved);
        document.Entities.Should().BeEquivalentTo(solved.Document.Entities);
    }

    [Fact]
    public void DimensionedRectangleLateralEdgeDragCurrentlyRefusesWithoutMutation()
    {
        // Characterize the separate drag limitation without invoking axis creation.
        var original = Create(true, new(0, -5));
        var aligned = new DrawingDocument(original.Entities, original.Dimensions,
            original.Constraints.Append(Relation("origin-axis", SketchConstraintKind.Horizontal, "edge:mid", "$origin:center")));
        var document = DxfDocumentReader.Read(DxfDocumentWriter.Write(aligned, new(IncludeNativeDocument: true)));
        AssertValid(document);
        var snapshot = DxfDocumentWriter.Write(document, new(IncludeNativeDocument: true));

        SketchGeometryDragService.TryApplyDrag(document, "edge", new(2, 0), new(5, 0), false,
            out var rejected, out var status, false).Should().BeFalse();

        status.Should().Contain("driving dimensions").And.Contain("Geometry unchanged");
        rejected.Should().BeSameAs(document);
        DxfDocumentWriter.Write(rejected, new(IncludeNativeDocument: true)).Should().Be(snapshot);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ExplicitMidpointFixRefusesSourceTranslation(bool rectangle, bool vertical)
    {
        var original = Create(rectangle);
        var document = new DrawingDocument(original.Entities, original.Dimensions,
            original.Constraints.Append(Relation("fixed-midpoint", SketchConstraintKind.Fix, "edge:mid")));
        AssertProtectedRefusal(document, Build(document, vertical, true, true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FixedOppositeRectangleEdgeRefusesInsteadOfBreakingWeldsOrSize(bool vertical)
    {
        var original = Create(true);
        var document = new DrawingDocument(original.Entities, original.Dimensions,
            original.Constraints.Append(Relation("fixed-opposite", SketchConstraintKind.Fix, "right")));
        AssertProtectedRefusal(document, Build(document, vertical, false, false));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LockedSourceCannotBeAcceptedByDocumentProtectionBoundary(bool vertical)
    {
        var original = Create(false);
        var document = new DrawingDocument(original.Entities, original.Dimensions, original.Constraints, original.Metadata with
        {
            EntityStyles = new Dictionary<string, DxfEntityStyle> { ["edge"] = new("Locked") },
            Layers = new Dictionary<string, DxfEntityStyle> { ["Locked"] = new("Locked", IsLocked: true) }
        });
        var snapshot = DxfDocumentWriter.Write(document, new(IncludeNativeDocument: true));
        var result = Solve(document, Build(document, vertical, false, true));
        if (result.Status == SketchSolveStatus.Solved)
            DrawingLayerService.PreservesProtectedGeometry(document, result.Document).Should().BeFalse();
        else
            result.Document.Entities.Should().Equal(document.Entities);
        DxfDocumentWriter.Write(document, new(IncludeNativeDocument: true)).Should().Be(snapshot);
    }

    public static IEnumerable<object[]> PolylineFixedCases()
    {
        foreach (var vertical in new[] { false, true })
        foreach (var key in new[] { "poly", "poly|segment|0", "poly|segment|0:mid", "poly|segment|0:start", "poly|segment|3:end" })
            yield return [vertical, key];
    }

    [Theory]
    [MemberData(nameof(PolylineFixedCases))]
    public void ClosedPolylineWholeSegmentMidpointAndClosingAliasFixesRefuse(bool vertical, string fixedKey)
    {
        var polyline = ClosedPolyline();
        var document = new DrawingDocument([polyline], [], [Relation("fixed", SketchConstraintKind.Fix, fixedKey)]);
        var axis = new SketchConstraint("origin-axis", vertical ? SketchConstraintKind.Vertical : SketchConstraintKind.Horizontal,
            ["poly|segment|0:mid", "$origin:center"]);
        AssertProtectedRefusal(document, axis);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FreeClosedPolylineSegmentTranslationPreservesClosureAndUnrelatedVertices(bool vertical)
    {
        var polyline = ClosedPolyline();
        var document = new DrawingDocument([polyline]);
        var axis = new SketchConstraint("origin-axis", vertical ? SketchConstraintKind.Vertical : SketchConstraintKind.Horizontal,
            ["$origin:center", "poly|segment|0:mid"]);
        var result = Solve(document, axis);
        result.Status.Should().Be(SketchSolveStatus.Solved);
        var changed = result.Document.Entities.Should().ContainSingle().Which.Should().BeOfType<PolylineEntity>().Which;
        var delta = vertical ? new Point2(-2, 0) : new Point2(0, -5);
        changed.Id.Should().Be(polyline.Id);
        changed.IsConstruction.Should().Be(polyline.IsConstruction);
        changed.Vertices.Should().Equal(Add(polyline.Vertices[0], delta), Add(polyline.Vertices[1], delta),
            polyline.Vertices[2], polyline.Vertices[3], Add(polyline.Vertices[0], delta));
        AssertValid(result.Document);
        document.Entities.Single().Should().Be(polyline);
    }

    private static PolylineEntity ClosedPolyline() => new(EntityId.Create("poly"),
        [new(2, 2), new(2, 8), new(10, 8), new(10, 2), new(2, 2)], true);

    [Theory]
    [InlineData("poly|segment|0:end")]
    [InlineData("poly|segment|0")]
    public void InteriorEndpointAliasAndAdjacentSegmentFixRefuseMidpointTranslation(string fixedKey)
    {
        var polyline = new PolylineEntity(EntityId.Create("poly"), [new(0, 0), new(2, 0), new(2, 2)]);
        var document = new DrawingDocument([polyline], [], [Relation("fixed", SketchConstraintKind.Fix, fixedKey)]);
        var axis = new SketchConstraint("origin-axis", SketchConstraintKind.Horizontal,
            ["poly|segment|1:mid", "$origin:center"]);
        SketchReferenceResolver.TryGetPoint(document, "poly|segment|0:end", out var before).Should().BeTrue();

        AssertProtectedRefusal(document, axis);

        var result = Solve(document, axis);
        SketchReferenceResolver.TryGetPoint(result.Document, "poly|segment|0:end", out var after).Should().BeTrue();
        after.Should().Be(before);
        ((PolylineEntity)result.Document.Entities.Single()).Vertices.Should().Equal(polyline.Vertices);
    }

    private static void AssertProtectedRefusal(DrawingDocument document, SketchConstraint axis)
    {
        var result = Solve(document, axis);
        result.Status.Should().NotBe(SketchSolveStatus.Solved);
        result.Document.Entities.Should().Equal(document.Entities);
        result.Document.Dimensions.Should().Equal(document.Dimensions);
        foreach (var original in document.Constraints)
            result.Document.Constraints.Single(c => c.Id == original.Id).Should().BeEquivalentTo(original);
        result.Document.Constraints.Single(c => c.Id == "origin-axis").State.Should().Be(SketchConstraintState.Unsatisfied);
    }

    private static SketchConstraint Build(DrawingDocument document, bool vertical, bool reverse, bool raw)
    {
        var keys = raw ? new[] { "edge|point|mid|999|999", "$origin|point|center|999|999" }
            : new[] { "edge:mid", "$origin:center" };
        if (reverse) Array.Reverse(keys);
        SketchCommandFactory.TryBuildConstraint(document, keys, vertical ? SketchConstraintKind.Vertical : SketchConstraintKind.Horizontal,
            "origin-axis", out var axis, out var reason).Should().BeTrue(reason);
        axis.ReferenceKeys.Should().BeEquivalentTo(["edge:mid", "$origin:center"]);
        return axis;
    }

    private static SketchSolveResult Solve(DrawingDocument document, SketchConstraint axis) =>
        new LegacySketchSolverAdapter().Solve(new SketchSolveRequest(document, [axis], []));

    private static DrawingDocument Create(bool rectangle, Point2 shift = default)
    {
        var points = new[] { new Point2(2, 2), new Point2(2, 8), new Point2(10, 8), new Point2(10, 2) };
        var ids = new[] { "edge", "top", "right", "bottom" };
        var entities = Enumerable.Range(0, rectangle ? 4 : 1).Select(i => (DrawingEntity)new LineEntity(EntityId.Create(ids[i]),
            Add(points[i], shift), Add(points[(i + 1) % 4], shift))).ToArray();
        var constraints = new List<SketchConstraint>();
        if (rectangle)
            for (var i = 0; i < 4; i++)
            {
                constraints.Add(Relation($"weld-{i}", SketchConstraintKind.Coincident, ids[i] + ":end", ids[(i + 1) % 4] + ":start"));
                constraints.Add(Relation($"axis-{i}", i % 2 == 0 ? SketchConstraintKind.Vertical : SketchConstraintKind.Horizontal, ids[i]));
            }
        var dimensions = new List<SketchDimension>
        {
            new("height", SketchDimensionKind.LinearDistance, ["edge:start", "edge:end"], 6, null, true)
        };
        if (rectangle) dimensions.Add(new("width", SketchDimensionKind.LinearDistance, ["top:start", "top:end"], 8, null, true));
        return new(entities, dimensions, constraints);
    }

    private static SketchConstraint Relation(string id, SketchConstraintKind kind, params string[] keys) =>
        new(id, kind, keys, SketchConstraintState.Satisfied);
    private static Point2 Add(Point2 a, Point2 b) => new(a.X + b.X, a.Y + b.Y);
    private static void AssertTranslation(DrawingDocument before, DrawingDocument after, Point2 delta)
    {
        after.Entities.Should().HaveCount(before.Entities.Count);
        foreach (var line in before.Entities.OfType<LineEntity>())
            after.Entities.Single(e => e.Id == line.Id).Should().Be(line with { Start = Add(line.Start, delta), End = Add(line.End, delta) });
        after.Dimensions.Should().Equal(before.Dimensions);
    }
    private static void AssertValid(DrawingDocument document)
    {
        document.Constraints.Should().OnlyContain(c => SketchConstraintService.ValidateConstraint(document, c).State == SketchConstraintState.Satisfied);
        foreach (var dimension in document.Dimensions)
            SketchDimensionSolverService.IsDimensionSatisfied(document, dimension).Should().BeTrue();
        SketchReferenceResolver.TryGetPoint(document, "$origin:center", out var origin).Should().BeTrue();
        origin.Should().Be(new Point2(0, 0));
    }
}

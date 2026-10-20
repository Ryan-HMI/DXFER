using DXFER.Blazor.Sketching;
using DXFER.CadIO;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class ThreePointMidpointTests
{
    public static IEnumerable<object[]> FactoryPermutations()
    {
        foreach (var first in Enumerable.Range(0, 3))
        foreach (var second in Enumerable.Range(0, 3).Where(index => index != first))
        foreach (var raw in new[] { false, true })
            yield return [first, second, 3 - first - second, raw];
    }

    [Theory]
    [MemberData(nameof(FactoryPermutations))]
    public void FactoryChoosesSameMiddleAcrossAllSixPickOrders(int first, int second, int third, bool raw)
    {
        var document = Create(0);
        var snapshot = DxfDocumentWriter.Write(document, new(IncludeNativeDocument: true));
        var keys = new[] { "m", "a", "b" }.Select(id => raw ? CanvasKey(0, id, new(999, 999)) : Key(0, id)).ToArray();
        SketchCommandFactory.TryBuildConstraint(document, [keys[first], keys[second], keys[third]], SketchConstraintKind.Midpoint,
            "midpoint", out var relation, out var reason).Should().BeTrue(reason);
        relation.Kind.Should().Be(SketchConstraintKind.Midpoint);
        relation.ReferenceKeys.Should().HaveCount(3);
        relation.ReferenceKeys[0].Should().Be(Key(0, "m"));
        relation.ReferenceKeys.Skip(1).Should().BeEquivalentTo([Key(0, "a"), Key(0, "b")]);
        DxfDocumentWriter.Write(document, new(IncludeNativeDocument: true)).Should().Be(snapshot);

        var result = Solve(document, relation);
        result.Status.Should().Be(SketchSolveStatus.Solved);
        AssertPoints(result.Document, 0, new(5, 0), new(0, 0), new(10, 0));
        result.Document.Constraints.Single().ReferenceKeys.Should().Equal(relation.ReferenceKeys);
        AssertUnrelatedGeometry(document, result.Document);
        AssertValid(result.Document);
    }

    public static IEnumerable<object[]> AmbiguousPermutations()
    {
        foreach (var order in FactoryPermutations().Where(order => !(bool)order[3]))
        foreach (var shape in new[] { "equilateral", "symmetric", "coincident" })
            yield return [shape, order[0], order[1], order[2]];
    }

    [Theory]
    [MemberData(nameof(AmbiguousPermutations))]
    public void FactoryAmbiguousOrZeroScalePointsRefuseWithoutDocumentChange(string shape, int first, int second, int third)
    {
        var points = shape switch
        {
            "equilateral" => new Point2[] { new(0, 0), new(2, 0), new(1, Math.Sqrt(3)) },
            "symmetric" => new Point2[] { new(0, 0), new(2, 0), new(1, 3) },
            _ => new Point2[] { new(4, 7), new(4, 7), new(4, 7) }
        };
        var document = new DrawingDocument(points.Select((point, index) => new PointEntity(EntityId.Create($"p{index}"), point)));
        var keys = new[] { $"p{first}:center", $"p{second}:center", $"p{third}:center" };
        var snapshot = DxfDocumentWriter.Write(document, new(IncludeNativeDocument: true));
        SketchCommandFactory.TryBuildConstraint(document, keys, SketchConstraintKind.Midpoint,
            "midpoint", out _, out var reason).Should().BeFalse();
        reason.Should().NotBeNullOrWhiteSpace();
        document.Constraints.Should().BeEmpty();
        DxfDocumentWriter.Write(document, new(IncludeNativeDocument: true)).Should().Be(snapshot);
    }

    [Theory]
    [InlineData("point-alias")]
    [InlineData("interior-alias")]
    [InlineData("closing-alias")]
    public void DuplicatePhysicalReferencesRefuseEvenWhenKeysDiffer(string kind)
    {
        var polyline = new PolylineEntity(EntityId.Create("poly"), [new(0, 0), new(2, 0), new(2, 2), new(0, 0)]);
        var document = new DrawingDocument([polyline, new PointEntity(EntityId.Create("point"), new(4, 5))]);
        var keys = kind switch
        {
            "point-alias" => new[] { "point", "point:center", "poly|segment|0:start" },
            "interior-alias" => new[] { "poly|segment|0:end", "poly|segment|1:start", "point:center" },
            _ => new[] { "poly|segment|0:start", "poly|segment|2:end", "point:center" }
        };
        var snapshot = DxfDocumentWriter.Write(document, new(IncludeNativeDocument: true));
        SketchCommandFactory.TryBuildConstraint(document, keys, SketchConstraintKind.Midpoint,
            "midpoint", out _, out _).Should().BeFalse();
        var relation = new SketchConstraint("midpoint", SketchConstraintKind.Midpoint, keys);
        AssertFailedUnchanged(document, Solve(document, relation));
        DxfDocumentWriter.Write(document, new(IncludeNativeDocument: true)).Should().Be(snapshot);
    }

    [Theory]
    [InlineData("missing:center")]
    [InlineData("b:mid")]
    [InlineData("a|segment|99:start")]
    [InlineData("a|segment|0:start")]
    [InlineData("unparseable|point|unknown|0|0")]
    public void UnsupportedOrMissingReferenceRefusesWithoutPartialApplication(string unsupported)
    {
        var document = Create(0);
        var keys = new[] { Key(0, "m"), Key(0, "a"), unsupported };
        var snapshot = DxfDocumentWriter.Write(document, new(IncludeNativeDocument: true));
        SketchCommandFactory.TryBuildConstraint(document, keys, SketchConstraintKind.Midpoint,
            "midpoint", out _, out _).Should().BeFalse();
        AssertFailedUnchanged(document, Solve(document, new SketchConstraint("midpoint", SketchConstraintKind.Midpoint, keys)));
        DxfDocumentWriter.Write(document, new(IncludeNativeDocument: true)).Should().Be(snapshot);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FactoryNormalizesRawOuterMidpointToLiveLineOrPolylineReference(bool polyline)
    {
        DrawingEntity source = polyline
            ? new PolylineEntity(EntityId.Create("source"), [new(0, -2), new(0, 2), new(-3, 2)])
            : new LineEntity(EntityId.Create("source"), new(0, -2), new(0, 2));
        var document = new DrawingDocument([source, new PointEntity(EntityId.Create("middle"), new(5, 1)),
            new CircleEntity(EntityId.Create("outer"), new(10, 0), 3)]);
        var rawMidpoint = polyline ? "source|point|mid-0|999|999" : "source|point|mid|999|999";
        var snapshot = DxfDocumentWriter.Write(document, new(IncludeNativeDocument: true));
        SketchCommandFactory.TryBuildConstraint(document, ["outer:center", rawMidpoint, "middle:center"], SketchConstraintKind.Midpoint,
            "midpoint", out var relation, out var reason).Should().BeTrue(reason);
        relation.ReferenceKeys[0].Should().Be("middle:center");
        relation.ReferenceKeys.Skip(1).Should().BeEquivalentTo(["outer:center", polyline ? "source|segment|0:mid" : "source:mid"]);
        DxfDocumentWriter.Write(document, new(IncludeNativeDocument: true)).Should().Be(snapshot);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public void FirstStoredReferenceIsMiddleAcrossPointEndpointAndCircleCenter(int layout, bool reverseOuters)
    {
        var document = Create(layout);
        var relation = Midpoint(layout, reverseOuters);
        var result = Solve(document, relation);

        result.Status.Should().Be(SketchSolveStatus.Solved);
        AssertPoints(result.Document, layout, new(5, 0), new(0, 0), new(10, 0));
        AssertUnrelatedGeometry(document, result.Document);
        AssertValid(result.Document);
        result.Document.Constraints.Single().ReferenceKeys.Should().Equal(relation.ReferenceKeys);
        var reopened = Reopen(result.Document);
        reopened.Entities.Should().Equal(result.Document.Entities);
        reopened.Constraints.Should().BeEquivalentTo(result.Document.Constraints);
        reopened.Dimensions.Should().BeEquivalentTo(result.Document.Dimensions);
        AssertValid(reopened);
        AssertPoints(document, layout, new(5, 3), new(0, 0), new(10, 0));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void FixedMiddleTranslatesBothFreeOuterPointsWithoutChangingTheirSeparation(int layout)
    {
        var document = WithFixed(Create(layout), Key(layout, "m"));
        var result = Solve(document, Midpoint(layout));
        result.Status.Should().Be(SketchSolveStatus.Solved);
        AssertPoints(result.Document, layout, new(5, 3), new(0, 3), new(10, 3));
        AssertUnrelatedGeometry(document, result.Document);
        AssertValid(result.Document);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public void FixedMiddleAndOneOuterReflectRemainingFreeOuter(int layout, bool fixedB)
    {
        var document = WithFixed(Create(layout), Key(layout, "m"), Key(layout, fixedB ? "b" : "a"));
        var result = Solve(document, Midpoint(layout, reverseOuters: fixedB));
        result.Status.Should().Be(SketchSolveStatus.Solved);
        AssertPoints(result.Document, layout, new(5, 3), fixedB ? new(0, 6) : new(0, 0), fixedB ? new(10, 0) : new(10, 6));
        AssertUnrelatedGeometry(document, result.Document);
        AssertValid(result.Document);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void BothOuterPointsFixedMovesOnlyFreeMiddle(int layout)
    {
        var document = WithFixed(Create(layout), Key(layout, "a"), Key(layout, "b"));
        var result = Solve(document, Midpoint(layout));
        result.Status.Should().Be(SketchSolveStatus.Solved);
        AssertPoints(result.Document, layout, new(5, 0), new(0, 0), new(10, 0));
        AssertUnrelatedGeometry(document, result.Document);
        AssertValid(result.Document);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void IncompatibleThreeFixedPointsRefuseAtomicallyAndRetainFailedIntent(int layout)
    {
        var document = WithFixed(Create(layout), Key(layout, "m"), Key(layout, "a"), Key(layout, "b"));
        AssertFailedUnchanged(document, Solve(document, Midpoint(layout)));
    }

    [Fact]
    public void ReflectionCannotBreakDrivingLengthOnFreeOuterEndpoint()
    {
        const int layout = 2;
        var original = WithFixed(Create(layout), Key(layout, "m"), Key(layout, "a"), "b:end");
        var length = new SketchDimension("length", SketchDimensionKind.LinearDistance, ["b:start", "b:end"], 4, null, true);
        var document = new DrawingDocument(original.Entities, original.Dimensions.Append(length), original.Constraints, original.Metadata);
        AssertFailedUnchanged(document, Solve(document, Midpoint(layout)));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public void OuterSourceDragMovesMiddleAndRetainsStoredOrderAfterNativeReopen(int layout, bool reverseOuters)
    {
        var original = Create(layout, aligned: true);
        var document = Reopen(WithRelation(original, Midpoint(layout, reverseOuters)));
        var snapshot = DxfDocumentWriter.Write(document, new(IncludeNativeDocument: true));
        var keys = document.Constraints.Single().ReferenceKeys.ToArray();

        SketchGeometryDragService.TryApplyDrag(document, CanvasKey(layout, "a", new(0, 0)), new(0, 0), new(2, 2), false,
            out var moved, out var status, false).Should().BeTrue(status);

        AssertPoints(moved, layout, new(6, 1), new(2, 2), new(10, 0));
        AssertUnrelatedGeometry(document, moved);
        AssertValid(moved);
        moved.Constraints.Single().ReferenceKeys.Should().Equal(keys);
        AssertPoints(Reopen(moved), layout, new(6, 1), new(2, 2), new(10, 0));
        DxfDocumentWriter.Write(document, new(IncludeNativeDocument: true)).Should().Be(snapshot, "the previous Undo document is immutable");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ExplicitMiddleDragTranslatesFreeOuterPair(int layout)
    {
        var document = WithRelation(Create(layout, aligned: true), Midpoint(layout));
        var snapshot = DxfDocumentWriter.Write(document, new(IncludeNativeDocument: true));
        SketchGeometryDragService.TryApplyDrag(document, CanvasKey(layout, "m", new(5, 0)), new(5, 0), new(6, 2), false,
            out var moved, out var status, false).Should().BeTrue(status);
        AssertPoints(moved, layout, new(6, 2), new(1, 2), new(11, 2));
        AssertUnrelatedGeometry(document, moved);
        AssertValid(moved);
        moved.Constraints.Single().ReferenceKeys.Should().Equal(document.Constraints.Single().ReferenceKeys);
        DxfDocumentWriter.Write(document, new(IncludeNativeDocument: true)).Should().Be(snapshot);
    }

    [Fact]
    public void FixedMiddleAndOtherOuterRejectSourceDragWithoutPartialMovement()
    {
        var original = WithFixed(Create(0, aligned: true), Key(0, "m"), Key(0, "b"));
        var document = WithRelation(original, Midpoint(0));
        SketchGeometryDragService.TryApplyDrag(document, CanvasKey(0, "a", new(0, 0)), new(0, 0), new(2, 2), false,
            out var rejected, out _, false).Should().BeFalse();
        rejected.Entities.Should().Equal(document.Entities);
        rejected.Dimensions.Should().Equal(document.Dimensions);
        rejected.Constraints.Should().Equal(document.Constraints);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LegacyTwoReferencePointAndLineMidpointRemainsUnchanged(bool reverse)
    {
        var line = new LineEntity(EntityId.Create("line"), new(1, 2), new(9, 2));
        var document = new DrawingDocument([line, new PointEntity(EntityId.Create("middle"), new(5, 3))]);
        var keys = reverse ? new[] { "middle:center", "line" } : new[] { "line", "middle:center" };
        SketchCommandFactory.TryBuildConstraint(document, keys, SketchConstraintKind.Midpoint, "midpoint",
            out var relation, out var reason).Should().BeTrue(reason);
        relation.ReferenceKeys.Should().Equal("line", "middle:center");
        var result = Solve(document, relation);
        result.Status.Should().Be(SketchSolveStatus.Solved);
        result.Document.Entities.Single(e => e.Id == line.Id).Should().Be(line);
        ((PointEntity)result.Document.Entities.Single(e => e.Id.Value == "middle")).Location.Should().Be(new Point2(5, 2));
        result.Document.Constraints.Single().ReferenceKeys.Should().Equal(relation.ReferenceKeys);
        AssertValid(result.Document);
    }

    private static DrawingDocument Create(int layout, bool aligned = false)
    {
        var locations = new[] { aligned ? new Point2(5, 0) : new Point2(5, 3), new Point2(0, 0), new Point2(10, 0) };
        var ids = new[] { "m", "a", "b" };
        var entities = ids.Select((id, index) => Entity(id, Type(layout, id), locations[index])).ToArray();
        var circle = entities.OfType<CircleEntity>().Single();
        return new(entities, [new SketchDimension("radius", SketchDimensionKind.Radius, [circle.Id.Value], 3, null, true)], []);
    }
    private static int Type(int layout, string id) => (layout + (id == "m" ? 0 : id == "a" ? 1 : 2)) % 3;
    private static DrawingEntity Entity(string id, int type, Point2 point) => type switch
    {
        0 => new PointEntity(EntityId.Create(id), point),
        1 => new LineEntity(EntityId.Create(id), point, new(point.X, point.Y + 4)),
        _ => new CircleEntity(EntityId.Create(id), point, 3)
    };
    private static string Key(int layout, string id) => id + (Type(layout, id) == 1 ? ":start" : ":center");
    private static string CanvasKey(int layout, string id, Point2 point) =>
        $"{id}|point|{(Type(layout, id) == 1 ? "start" : "center")}|{point.X}|{point.Y}";
    private static SketchConstraint Midpoint(int layout, bool reverseOuters = false) => new("midpoint", SketchConstraintKind.Midpoint,
        [Key(layout, "m"), Key(layout, reverseOuters ? "b" : "a"), Key(layout, reverseOuters ? "a" : "b")]);
    private static DrawingDocument WithFixed(DrawingDocument document, params string[] keys) =>
        new(document.Entities, document.Dimensions, document.Constraints.Concat(keys.Select((key, index) =>
            new SketchConstraint($"fix-{index}", SketchConstraintKind.Fix, [key], SketchConstraintState.Satisfied))), document.Metadata);
    private static DrawingDocument WithRelation(DrawingDocument document, SketchConstraint relation) =>
        new(document.Entities, document.Dimensions, document.Constraints.Append(
            new SketchConstraint(relation.Id, relation.Kind, relation.ReferenceKeys, SketchConstraintState.Satisfied)), document.Metadata);
    private static SketchSolveResult Solve(DrawingDocument document, SketchConstraint relation) =>
        new LegacySketchSolverAdapter().Solve(new SketchSolveRequest(document, [relation], []));
    private static DrawingDocument Reopen(DrawingDocument document) =>
        DxfDocumentReader.Read(DxfDocumentWriter.Write(document, new(IncludeNativeDocument: true)));
    private static void AssertPoints(DrawingDocument document, int layout, Point2 middle, Point2 a, Point2 b)
    {
        var expected = new[] { middle, a, b };
        var ids = new[] { "m", "a", "b" };
        for (var index = 0; index < ids.Length; index++)
        {
            SketchReferenceResolver.TryGetPoint(document, Key(layout, ids[index]), out var point).Should().BeTrue();
            point.Should().Be(expected[index]);
        }
    }
    private static void AssertUnrelatedGeometry(DrawingDocument before, DrawingDocument after)
    {
        after.Entities.Select(e => e.Id).Should().Equal(before.Entities.Select(e => e.Id));
        foreach (var line in before.Entities.OfType<LineEntity>())
            ((LineEntity)after.Entities.Single(e => e.Id == line.Id)).End.Should().Be(line.End);
        foreach (var circle in before.Entities.OfType<CircleEntity>())
            ((CircleEntity)after.Entities.Single(e => e.Id == circle.Id)).Radius.Should().Be(circle.Radius);
        after.Dimensions.Should().Equal(before.Dimensions);
    }
    private static void AssertValid(DrawingDocument document)
    {
        document.Constraints.Should().OnlyContain(c => SketchConstraintService.ValidateConstraint(document, c).State == SketchConstraintState.Satisfied);
        foreach (var dimension in document.Dimensions)
            SketchDimensionSolverService.IsDimensionSatisfied(document, dimension).Should().BeTrue();
    }
    private static void AssertFailedUnchanged(DrawingDocument document, SketchSolveResult result)
    {
        result.Status.Should().NotBe(SketchSolveStatus.Solved);
        result.Document.Entities.Should().Equal(document.Entities);
        result.Document.Dimensions.Should().Equal(document.Dimensions);
        foreach (var constraint in document.Constraints)
            result.Document.Constraints.Single(c => c.Id == constraint.Id).Should().BeEquivalentTo(constraint);
        result.Document.Constraints.Single(c => c.Id == "midpoint").State.Should().Be(SketchConstraintState.Unsatisfied);
    }
}

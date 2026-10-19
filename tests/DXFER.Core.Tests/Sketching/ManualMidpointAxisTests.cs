using DXFER.Blazor.Sketching;
using DXFER.CadIO;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class ManualMidpointAxisTests
{
    public static IEnumerable<object[]> AxisCases()
    {
        foreach (var polyline in new[] { false, true })
        foreach (var vertical in new[] { false, true })
        foreach (var reverse in new[] { false, true })
            yield return [polyline, vertical, reverse];
    }

    public static IEnumerable<object[]> FixedCases()
    {
        foreach (var values in AxisCases())
        foreach (var aligned in new[] { false, true })
            yield return values.Append((object)aligned).ToArray();
    }

    [Theory]
    [MemberData(nameof(AxisCases))]
    public void RawMidpointAxisMovesOnlyFreeCircleAndPersistsLiveReference(bool polyline, bool vertical, bool reverse)
    {
        var document = Create(polyline, vertical);
        var constraint = Build(document, polyline, vertical, reverse);
        constraint.ReferenceKeys.Should().Contain(CanonicalKey(polyline));
        constraint.ReferenceKeys.Select(Normalize).Should().BeEquivalentTo([CanonicalKey(polyline), "circle:center"]);

        var result = Solve(document, constraint);
        result.Status.Should().Be(SketchSolveStatus.Solved);
        result.Document.Entities[0].Should().BeSameAs(document.Entities[0], "the midpoint is a read-only source");
        var circle = result.Document.Entities.OfType<CircleEntity>().Single();
        circle.Center.Should().Be(ExpectedCenter(vertical));
        circle.Radius.Should().Be(3);
        result.Document.Dimensions.Should().Equal(document.Dimensions);
        result.Document.Constraints.Should().ContainSingle().Which.State.Should().Be(SketchConstraintState.Satisfied);

        var reopened = DxfDocumentReader.Read(DxfDocumentWriter.Write(result.Document, new(IncludeNativeDocument: true)));
        reopened.Constraints.Single().ReferenceKeys.Should().Equal(constraint.ReferenceKeys);
        reopened.Entities.OfType<CircleEntity>().Single().Should().Be(circle);
        SketchReferenceResolver.TryGetPoint(reopened, CanonicalKey(polyline), out var midpoint).Should().BeTrue();
        midpoint.Should().Be(new Point2(2, 5), "999 coordinates are stale canvas payload, not geometry");

        SketchGeometryDragService.TryApplyDrag(reopened,
            polyline ? "source|point|vertex-1|2|10" : "source|point|end|2|10",
            new(2, 10), new(6, 14), false, out var edited, out _, false).Should().BeTrue();
        edited.Entities.OfType<CircleEntity>().Single().Center.Should().Be(vertical ? new Point2(4, 12) : new Point2(8, 7));
        edited.Entities.OfType<CircleEntity>().Single().Radius.Should().Be(3);
        edited.Constraints.Should().OnlyContain(c => c.State == SketchConstraintState.Satisfied);
    }

    [Theory]
    [MemberData(nameof(FixedCases))]
    public void FixedCircleRefusesIncompatibleAxisButAcceptsAlreadyAligned(bool polyline, bool vertical, bool reverse, bool aligned)
    {
        var document = Create(polyline, vertical, aligned, fixedCircle: true);
        var constraint = Build(document, polyline, vertical, reverse);
        constraint.ReferenceKeys.Should().Contain(CanonicalKey(polyline));
        var result = Solve(document, constraint);
        result.Document.Entities.Should().Equal(document.Entities);
        result.Document.Dimensions.Should().Equal(document.Dimensions);
        if (aligned)
        {
            result.Status.Should().Be(SketchSolveStatus.Solved);
            result.Document.Constraints.Should().OnlyContain(c => c.State == SketchConstraintState.Satisfied);
        }
        else
        {
            result.Status.Should().NotBe(SketchSolveStatus.Solved);
            result.Document.Constraints.Should().Contain(c => c.Id == "axis" && c.State == SketchConstraintState.Unsatisfied);
        }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void TwoReadOnlyMidpointsAcceptAlignmentButNeverMoveGeometry(bool vertical, bool reverse, bool canonical)
    {
        var keys = canonical
            ? new[] { "source:mid", "other|segment|0:mid" }
            : new[] { "source|point|mid|999|999", "other|point|mid-0|999|999" };
        if (reverse) Array.Reverse(keys);
        foreach (var aligned in new[] { true, false })
        {
            var other = Source(true) with { Id = EntityId.Create("other") };
            if (!aligned) other = other.Transform(Transform2.Translation(3, 4));
            var document = new DrawingDocument([Source(false), other]);
            var built = SketchCommandFactory.TryBuildConstraint(document, keys, Kind(vertical), "axis", out var constraint, out var status);
            status.Should().NotBeNullOrWhiteSpace();
            if (!built)
            {
                aligned.Should().BeFalse("already aligned read-only midpoints can retain an H/V relation");
                continue;
            }
            var result = Solve(document, constraint);
            result.Document.Entities.Should().Equal(document.Entities);
            if (aligned) result.Status.Should().Be(SketchSolveStatus.Solved);
            else result.Status.Should().NotBe(SketchSolveStatus.Solved, "neither midpoint is writable");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitMidpointCommandStillUsesLineAndCenter(bool polyline)
    {
        var document = Create(polyline, false);
        var lineKey = polyline ? "source|segment|0" : "source";
        SketchCommandFactory.TryBuildConstraint(document, [lineKey, "circle|point|center|999|999"],
            SketchConstraintKind.Midpoint, "midpoint", out var constraint, out _).Should().BeTrue();
        constraint.Kind.Should().Be(SketchConstraintKind.Midpoint);
        constraint.ReferenceKeys.Select(Normalize).Should().Equal(lineKey, "circle:center");
        var result = Solve(document, constraint);
        result.Status.Should().Be(SketchSolveStatus.Solved);
        result.Document.Entities.OfType<CircleEntity>().Single().Center.Should().Be(new Point2(2, 5));
        result.Document.Entities.OfType<CircleEntity>().Single().Radius.Should().Be(3);
        result.Document.Entities[0].Should().BeSameAs(document.Entities[0]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AutomaticDirectMidpointStillCreatesMidpointNotCoincident(bool polyline)
    {
        var constraints = SketchCreationConstraintFactory.CreateConstraintsForInsertion("point", [Source(polyline)],
            [new PointEntity(EntityId.Create("created"), new(2, 5))], kind => kind.ToString());
        constraints.Should().Contain(c => c.Kind == SketchConstraintKind.Midpoint);
        constraints.Should().NotContain(c => c.Kind == SketchConstraintKind.Coincident);
    }

    private static SketchConstraint Build(DrawingDocument document, bool polyline, bool vertical, bool reverse)
    {
        var keys = new[] { polyline ? "source|point|mid-0|999|999" : "source|point|mid|999|999", "circle|point|center|999|999" };
        if (reverse) Array.Reverse(keys);
        SketchCommandFactory.TryBuildConstraint(document, keys, Kind(vertical), "axis", out var constraint, out _).Should().BeTrue();
        return constraint;
    }

    private static SketchSolveResult Solve(DrawingDocument document, SketchConstraint constraint) =>
        new LegacySketchSolverAdapter().Solve(new SketchSolveRequest(document, [constraint], []));
    private static string Normalize(string key)
    {
        SketchReference.TryNormalize(key, out var normalized).Should().BeTrue();
        return normalized;
    }
    private static SketchConstraintKind Kind(bool vertical) => vertical ? SketchConstraintKind.Vertical : SketchConstraintKind.Horizontal;
    private static string CanonicalKey(bool polyline) => polyline ? "source|segment|0:mid" : "source:mid";
    private static Point2 ExpectedCenter(bool vertical) => vertical ? new(2, 12) : new(8, 5);
    private static DrawingEntity Source(bool polyline) => polyline
        ? new PolylineEntity(EntityId.Create("source"), [new(2, 0), new(2, 10), new(9, 10)])
        : new LineEntity(EntityId.Create("source"), new(2, 0), new(2, 10));

    private static DrawingDocument Create(bool polyline, bool vertical, bool aligned = false, bool fixedCircle = false) =>
        new([Source(polyline), new CircleEntity(EntityId.Create("circle"), aligned ? ExpectedCenter(vertical) : new(8, 12), 3)],
            [new SketchDimension("radius", SketchDimensionKind.Radius, ["circle"], 3, new(12, 14), true)],
            fixedCircle ? [new SketchConstraint("fix", SketchConstraintKind.Fix, ["circle"], SketchConstraintState.Satisfied)] : []);
}

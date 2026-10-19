using DXFER.Blazor.Sketching;
using DXFER.CadIO;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Operations;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class PatternCenterConstraintTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CoincidentMovesFreePointToGeneratedCenterInEitherOrder(bool reverse, bool canvas)
    {
        var document = Create();
        var result = Constrain(document, SketchConstraintKind.Coincident, reverse, canvas);
        result.Status.Should().Be(SketchSolveStatus.Solved);
        Point(result.Document).Should().Be(MemberCircle(document).Center);
        Point(result.Document).X.Should().BeApproximately(0, 1e-9);
        Point(result.Document).Y.Should().BeApproximately(4, 1e-9);
        AssertPatternUnchanged(document, result.Document);
        AssertSatisfied(result.Document);
        var reopened = Reopen(result.Document);
        AssertPatternUnchanged(document, reopened);
        reopened.Constraints.Should().BeEquivalentTo(result.Document.Constraints);
        Point(reopened).Should().Be(Point(result.Document));
        AssertSatisfied(reopened);
        Point(document).Should().Be(new Point2(6, 6), "the original Undo snapshot must remain unchanged");
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
    public void AxisMovesOnlyFreePointCoordinateNotGeneratedCircle(bool vertical, bool reverse, bool canvas)
    {
        var document = Create();
        var result = Constrain(document, vertical ? SketchConstraintKind.Vertical : SketchConstraintKind.Horizontal, reverse, canvas);
        result.Status.Should().Be(SketchSolveStatus.Solved);
        var center = MemberCircle(document).Center;
        Point(result.Document).Should().Be(vertical ? new Point2(center.X, 6) : new Point2(6, center.Y));
        AssertPatternUnchanged(document, result.Document);
        AssertSatisfied(result.Document);
        var reopened = Reopen(result.Document);
        AssertPatternUnchanged(document, reopened);
        AssertSatisfied(reopened);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FixGeneratedCenterPersistsAndRefusesSourceTranslation(bool canvas)
    {
        var original = Create();
        var document = FixCenter(original, canvas);
        AssertPatternUnchanged(original, document);
        var fixedConstraint = document.Constraints.Single();
        fixedConstraint.Kind.Should().Be(SketchConstraintKind.Fix);
        fixedConstraint.ReferenceKeys.Select(Normalize).Should().Equal(MemberId(document) + ":center");
        var reopened = Reopen(document);
        reopened.Constraints.Should().BeEquivalentTo(document.Constraints);
        AssertPatternUnchanged(document, reopened);

        SketchGeometryDragService.TryApplyDrag(reopened, "seed|point|center|4|0", new(4, 0), new(5, 1), false,
            out var rejected, out var status, false).Should().BeFalse();
        rejected.Entities.Should().Equal(reopened.Entities);
        status.Should().NotBeNullOrWhiteSpace();

        var seed = reopened.Entities.OfType<CircleEntity>().Single(c => c.Id.Value == "seed");
        var candidate = new DrawingDocument(reopened.Entities.Select(e => e.Id == seed.Id ? seed with { Center = new Point2(5, 1) } : e),
            reopened.Dimensions, reopened.Constraints, reopened.Metadata);
        DrawingPatternService.TryReconcile(reopened, candidate, out var reconciled, out var reason).Should().BeFalse();
        reconciled.Entities.Should().Equal(reopened.Entities);
        reason.Should().Contain("Fixed");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FixedMemberCenterAllowsSourceRadiusEditWithoutFixingWholeMember(bool canvas)
    {
        var document = FixCenter(Create(), canvas);
        var radius = document.Dimensions.Single();
        var result = new LegacySketchSolverAdapter().Solve(new(document, [],
            [new SketchDimension(radius.Id, radius.Kind, radius.ReferenceKeys, 2, radius.Anchor, true)]));
        result.Status.Should().Be(SketchSolveStatus.Solved);
        result.Document.Entities.OfType<CircleEntity>().Should().OnlyContain(c => c.Radius == 2);
        foreach (var before in document.Entities.OfType<CircleEntity>())
            result.Document.Entities.OfType<CircleEntity>().Single(c => c.Id == before.Id).Center.Should().Be(before.Center);
        result.Document.Metadata.Patterns.Should().BeEquivalentTo(document.Metadata.Patterns);
        AssertSatisfied(result.Document);
        AssertSatisfied(Reopen(result.Document));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void FixedExternalPointConflictRefusesButAlreadyCoincidentPasses(bool reverse, bool aligned)
    {
        var original = Create();
        var document = new DrawingDocument(original.Entities.Select(e => e is PointEntity point && aligned
                ? point with { Location = MemberCircle(original).Center } : e), original.Dimensions,
            [new SketchConstraint("fix-point", SketchConstraintKind.Fix, ["point"], SketchConstraintState.Satisfied)], original.Metadata);
        var result = Constrain(document, SketchConstraintKind.Coincident, reverse, canvas: false);
        result.Document.Entities.Should().Equal(document.Entities);
        result.Document.Metadata.Patterns.Should().BeEquivalentTo(document.Metadata.Patterns);
        if (aligned)
        {
            result.Status.Should().Be(SketchSolveStatus.Solved);
            AssertSatisfied(result.Document);
        }
        else result.Status.Should().NotBe(SketchSolveStatus.Solved);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GeneratedWholeOrCenterDragStillRefusesIndependently(bool centerGrip)
    {
        var document = Create();
        var center = MemberCircle(document).Center;
        var key = centerGrip ? CenterKey(document, canvas: true) : MemberId(document);
        SketchGeometryDragService.TryApplyDrag(document, key, center, new(center.X + 2, center.Y + 1), false,
            out var rejected, out var status, false).Should().BeFalse();
        rejected.Entities.Should().Equal(document.Entities);
        rejected.Metadata.Patterns.Should().BeEquivalentTo(document.Metadata.Patterns);
        status.Should().Contain("Generated pattern members");
    }

    [Theory]
    [InlineData(SketchConstraintKind.Coincident)]
    [InlineData(SketchConstraintKind.Horizontal)]
    [InlineData(SketchConstraintKind.Vertical)]
    public void ExternalPeerFollowsRegeneratedCenterAfterSourceEdit(SketchConstraintKind kind)
    {
        var attached = Constrain(Create(), kind, reverse: false, canvas: false);
        attached.Status.Should().Be(SketchSolveStatus.Solved);
        var document = Reopen(attached.Document);
        SketchGeometryDragService.TryApplyDrag(document, "seed|point|center|4|0", new(4, 0), new(5, 1), false,
            out var moved, out var status, false).Should().BeTrue(status);
        moved.Entities.OfType<CircleEntity>().Single(c => c.Id.Value == "seed").Center.Should().Be(new Point2(5, 1));
        var center = MemberCircle(moved).Center;
        center.X.Should().BeApproximately(-1, 1e-9);
        center.Y.Should().BeApproximately(5, 1e-9);
        var expected = kind switch
        {
            SketchConstraintKind.Horizontal => new Point2(6, center.Y),
            SketchConstraintKind.Vertical => new Point2(center.X, 6),
            _ => center
        };
        Point(moved).Should().Be(expected);
        moved.Entities.OfType<CircleEntity>().Should().OnlyContain(c => c.Radius == 1);
        moved.Metadata.Patterns.Should().BeEquivalentTo(document.Metadata.Patterns);
        AssertSatisfied(moved);
        AssertSatisfied(Reopen(moved));
    }

    [Theory]
    [InlineData(SketchConstraintKind.Coincident)]
    [InlineData(SketchConstraintKind.Horizontal)]
    [InlineData(SketchConstraintKind.Vertical)]
    public void FixedExternalPeerRefusesSourceCenterEditWithoutPartialRegeneration(SketchConstraintKind kind)
    {
        var attached = Constrain(Create(), kind, reverse: false, canvas: false);
        attached.Status.Should().Be(SketchSolveStatus.Solved);
        var document = Reopen(new DrawingDocument(attached.Document.Entities, attached.Document.Dimensions,
            attached.Document.Constraints.Append(new SketchConstraint("fix-point", SketchConstraintKind.Fix,
                ["point"], SketchConstraintState.Satisfied)), attached.Document.Metadata));

        SketchGeometryDragService.TryApplyDrag(document, "seed|point|center|4|0", new(4, 0), new(5, 1), false,
            out var rejected, out var status, false).Should().BeFalse();
        status.Should().NotBeNullOrWhiteSpace();
        rejected.Entities.Should().Equal(document.Entities);
        rejected.Constraints.Should().Equal(document.Constraints);
        AssertPatternUnchanged(document, rejected);

        var seed = document.Entities.OfType<CircleEntity>().Single(c => c.Id.Value == "seed");
        var candidate = new DrawingDocument(document.Entities.Select(e => e.Id == seed.Id
                ? seed with { Center = new Point2(5, 1) } : e), document.Dimensions, document.Constraints, document.Metadata);
        DrawingPatternService.TryReconcile(document, candidate, out var reconciled, out var reason).Should().BeFalse();
        reason.Should().NotBeNullOrWhiteSpace();
        reconciled.Entities.Should().Equal(document.Entities);
        reconciled.Constraints.Should().Equal(document.Constraints);
        AssertPatternUnchanged(document, reconciled);
        AssertSatisfied(document);
    }

    private static DrawingDocument Create()
    {
        var document = new DrawingDocument([new CircleEntity(EntityId.Create("seed"), new(4, 0), 1),
                new PointEntity(EntityId.Create("point"), new(6, 6))],
            [new SketchDimension("seed-radius", SketchDimensionKind.Radius, ["seed"], 1, new(6, -1), true)], []);
        DrawingPatternService.TryCreate(document, new PatternDefinition
        {
            Id = "radial", Kind = PatternKind.Radial, SourceEntityIds = ["seed"], Count = 4,
            Center = new(0, 0), FullCircle = true
        }, out var result, out var reason).Should().BeTrue(reason);
        result.Metadata.Patterns.Single().Members.Should().HaveCount(3);
        MemberCircle(result).Center.X.Should().BeApproximately(0, 1e-9);
        MemberCircle(result).Center.Y.Should().BeApproximately(4, 1e-9);
        return result;
    }

    private static SketchSolveResult Constrain(DrawingDocument document, SketchConstraintKind kind, bool reverse, bool canvas)
    {
        var keys = new[] { CenterKey(document, canvas), "point" };
        if (reverse) Array.Reverse(keys);
        SketchCommandFactory.TryBuildConstraint(document, keys, kind, "relation", out var constraint, out var status).Should().BeTrue(status);
        constraint.Kind.Should().Be(kind);
        constraint.ReferenceKeys.Select(Normalize).Should().Contain(MemberId(document) + ":center");
        return new LegacySketchSolverAdapter().Solve(new(document, [constraint], []));
    }

    private static DrawingDocument FixCenter(DrawingDocument document, bool canvas)
    {
        SketchCommandFactory.TryBuildConstraint(document, [CenterKey(document, canvas)], SketchConstraintKind.Fix,
            "fixed-center", out var constraint, out var status).Should().BeTrue(status);
        var result = new LegacySketchSolverAdapter().Solve(new(document, [constraint], []));
        result.Status.Should().Be(SketchSolveStatus.Solved);
        return result.Document;
    }

    private static string MemberId(DrawingDocument document) => document.Metadata.Patterns.Single().Members.Single(m => m.Column == 1).EntityId;
    private static CircleEntity MemberCircle(DrawingDocument document) => document.Entities.OfType<CircleEntity>().Single(c => c.Id.Value == MemberId(document));
    private static Point2 Point(DrawingDocument document) => document.Entities.OfType<PointEntity>().Single().Location;
    private static string CenterKey(DrawingDocument document, bool canvas) => MemberId(document) + (canvas ? "|point|center|999|999" : ":center");
    private static DrawingDocument Reopen(DrawingDocument document) => DxfDocumentReader.Read(DxfDocumentWriter.Write(document, new(IncludeNativeDocument: true)));
    private static string Normalize(string key)
    {
        SketchReference.TryNormalize(key, out var normalized).Should().BeTrue();
        return normalized;
    }
    private static void AssertPatternUnchanged(DrawingDocument before, DrawingDocument after)
    {
        after.Entities.OfType<CircleEntity>().Should().Equal(before.Entities.OfType<CircleEntity>());
        after.Metadata.Patterns.Should().BeEquivalentTo(before.Metadata.Patterns);
        after.Metadata.Patterns.Single().Members.Should().Equal(before.Metadata.Patterns.Single().Members);
        after.Dimensions.Should().BeEquivalentTo(before.Dimensions);
        DrawingPatternService.TryValidate(after, out var reason).Should().BeTrue(reason);
    }
    private static void AssertSatisfied(DrawingDocument document)
    {
        document.Constraints.Select(c => SketchConstraintService.ValidateConstraint(document, c).State)
            .Should().OnlyContain(state => state == SketchConstraintState.Satisfied);
        document.Dimensions.Should().OnlyContain(d => SketchDimensionSolverService.IsDimensionSatisfied(document, d));
        DrawingPatternService.TryValidate(document, out var reason).Should().BeTrue(reason);
    }
}

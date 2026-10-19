using DXFER.Blazor.Sketching;
using DXFER.CadIO;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Operations;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class PointOnCircleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CanvasQuadrantPickMeansWholeSupportingCircleNotStoredPoint(bool reverse)
    {
        var document = Create();
        var keys = new[] { "moving|point|center|999|999", "support|point|quadrant-0|999|999" };
        if (reverse) Array.Reverse(keys);
        SketchCommandFactory.TryBuildConstraint(document, keys, SketchConstraintKind.Coincident, "incidence",
            out var constraint, out var status).Should().BeTrue(status);
        constraint.Kind.Should().Be(SketchConstraintKind.PointOnCircle);
        constraint.ReferenceKeys.Select(Normalize).Should().BeEquivalentTo(["moving:center", "support"]);
        var result = Solve(document, constraint);
        result.Status.Should().Be(SketchSolveStatus.Solved);
        Circle(result.Document, "moving").Center.Should().Be(new Point2(4, 3));
        Circle(result.Document, "support").Should().Be(Circle(document, "support"));
        AssertSatisfied(result.Document);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NewlyConcentricCentersUseDeterministicPositiveXFallback(bool reverse)
    {
        var original = Create();
        var document = new DrawingDocument(original.Entities.Select(e => e.Id.Value == "moving"
            ? Circle(original, "moving") with { Center = new Point2(0, 0) } : e), original.Dimensions, []);
        var result = Solve(document, Incidence(reverse));
        result.Status.Should().Be(SketchSolveStatus.Solved);
        Circle(result.Document, "moving").Center.Should().Be(new Point2(5, 0));
        Circle(result.Document, "support").Center.Should().Be(new Point2(0, 0));
        AssertRadii(result.Document);
        AssertSatisfied(result.Document);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PointDraggedOntoSupportingCenterRetainsOriginalRadialDirection(bool reverse)
    {
        var document = Solved(reverse);
        SketchGeometryDragService.TryApplyDrag(document, "moving|point|center|4|3", new(4, 3), new(0, 0), false,
            out var moved, out var status, false).Should().BeTrue(status);
        Circle(moved, "moving").Center.Should().Be(new Point2(0, 0));
        Circle(moved, "support").Center.Should().Be(new Point2(-4, -3));
        AssertRadii(moved);
        AssertSatisfied(moved);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1e-9)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidOrDegenerateSupportingRadiusRefusesAtomically(double radius)
    {
        var document = new DrawingDocument([new CircleEntity(EntityId.Create("support"), new(0, 0), radius),
            new CircleEntity(EntityId.Create("moving"), new(8, 6), 2)]);
        var result = Solve(document, Incidence());
        result.Status.Should().NotBe(SketchSolveStatus.Solved);
        result.Document.Entities.Should().Equal(document.Entities);
        result.Document.Constraints.Should().ContainSingle().Which.State.Should().Be(SketchConstraintState.Unsatisfied);
    }

    [Theory]
    [InlineData(false, double.NaN)]
    [InlineData(false, double.PositiveInfinity)]
    [InlineData(true, double.NaN)]
    [InlineData(true, double.NegativeInfinity)]
    public void NonfinitePointOrSupportingCenterRefusesAtomically(bool invalidSupport, double value)
    {
        var document = new DrawingDocument([
            new CircleEntity(EntityId.Create("support"), invalidSupport ? new(value, 0) : new(0, 0), 5),
            new CircleEntity(EntityId.Create("moving"), invalidSupport ? new(8, 6) : new(value, 6), 2)]);
        var result = Solve(document, Incidence());
        result.Status.Should().NotBe(SketchSolveStatus.Solved);
        result.Document.Entities.Should().Equal(document.Entities);
        result.Document.Constraints.Should().ContainSingle().Which.State.Should().Be(SketchConstraintState.Unsatisfied);
    }

    [Fact]
    public void DirectSolverRequestCannotBypassSelfRelationGuard()
    {
        var document = Create();
        var result = Solve(document, new SketchConstraint("self", SketchConstraintKind.PointOnCircle, ["support:center", "support"]));
        result.Status.Should().NotBe(SketchSolveStatus.Solved);
        result.Document.Entities.Should().Equal(document.Entities);
        result.Document.Dimensions.Should().Equal(document.Dimensions);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CenterAndWholeCircleCreateIncidenceInEitherOrder(bool reverse, bool canvasCenter)
    {
        var document = Create();
        var keys = new[] { canvasCenter ? "moving|point|center|999|999" : "moving:center", "support" };
        if (reverse) Array.Reverse(keys);
        SketchCommandFactory.TryBuildConstraint(document, keys, SketchConstraintKind.Coincident, "incidence",
            out var constraint, out var status).Should().BeTrue(status);
        constraint.Kind.Should().Be(SketchConstraintKind.PointOnCircle);
        constraint.ReferenceKeys.Select(Normalize).Should().BeEquivalentTo(["moving:center", "support"]);
        var solved = Solve(document, constraint);
        solved.Status.Should().Be(SketchSolveStatus.Solved);
        Circle(solved.Document, "support").Should().Be(Circle(document, "support"));
        Circle(solved.Document, "moving").Center.Should().Be(new Point2(4, 3));
        AssertRadii(solved.Document);
        solved.Document.Dimensions.Should().Equal(document.Dimensions);
        AssertSatisfied(solved.Document);

        var reopened = Reopen(solved.Document);
        reopened.Constraints.Should().BeEquivalentTo(solved.Document.Constraints);
        reopened.Dimensions.Should().BeEquivalentTo(solved.Document.Dimensions);
        reopened.Entities.Should().Equal(solved.Document.Entities);
        AssertSatisfied(reopened);
        Circle(document, "moving").Center.Should().Be(new Point2(8, 6), "the original Undo snapshot must remain unchanged");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TwoExplicitCentersRemainPointToPointCoincident(bool reverse)
    {
        var document = Create();
        var keys = new[] { "support|point|center|0|0", "moving|point|center|8|6" };
        if (reverse) Array.Reverse(keys);
        SketchCommandFactory.TryBuildConstraint(document, keys, SketchConstraintKind.Coincident, "centers",
            out var constraint, out _).Should().BeTrue();
        constraint.Kind.Should().Be(SketchConstraintKind.Coincident);
        var solved = Solve(document, constraint);
        solved.Status.Should().Be(SketchSolveStatus.Solved);
        Circle(solved.Document, "moving").Center.Should().Be(Circle(solved.Document, "support").Center);
        AssertRadii(solved.Document);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SameCircleCenterAndCircumferenceCannotCreateSelfRelation(bool reverse)
    {
        var keys = new[] { "support:center", "support" };
        if (reverse) Array.Reverse(keys);
        SketchCommandFactory.TryBuildConstraint(Create(), keys, SketchConstraintKind.Coincident, "self",
            out _, out var status).Should().BeFalse();
        status.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void TwoWholeCirclesDoNotImplicitlySelectTheirCenters()
    {
        SketchCommandFactory.TryBuildConstraint(Create(), ["support", "moving"], SketchConstraintKind.Coincident,
            "whole", out _, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ArcCircumferenceIsNotSilentlyTreatedAsFullCircle(bool reverse)
    {
        var document = new DrawingDocument([new ArcEntity(EntityId.Create("support"), new(0, 0), 5, 0, 90),
            new CircleEntity(EntityId.Create("moving"), new(8, 6), 2)]);
        var keys = new[] { "moving:center", "support" };
        if (reverse) Array.Reverse(keys);
        SketchCommandFactory.TryBuildConstraint(document, keys, SketchConstraintKind.Coincident,
            "arc", out _, out _).Should().BeFalse();
        var solved = Solve(document, Incidence(reverse));
        solved.Status.Should().NotBe(SketchSolveStatus.Solved);
        solved.Document.Entities.Should().Equal(document.Entities);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FixedPointTranslatesFreeSupportingCircleWithoutResizing(bool reverse)
    {
        var document = Create(fixMoving: true);
        var solved = Solve(document, Incidence(reverse));
        solved.Status.Should().Be(SketchSolveStatus.Solved);
        Circle(solved.Document, "moving").Should().Be(Circle(document, "moving"));
        Circle(solved.Document, "support").Center.Should().NotBe(new Point2(0, 0));
        AssertRadii(solved.Document);
        AssertSatisfied(solved.Document);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FixedSupportingCircleStillAllowsFreePointProjection(bool reverse)
    {
        var document = Create(fixSupport: true);
        var solved = Solve(document, Incidence(reverse));
        solved.Status.Should().Be(SketchSolveStatus.Solved);
        Circle(solved.Document, "support").Should().Be(Circle(document, "support"));
        Circle(solved.Document, "moving").Center.Should().Be(new Point2(4, 3));
        AssertRadii(solved.Document);
        AssertSatisfied(solved.Document);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void FixedConflictIsAtomicAndAlreadySatisfiedFixedPairPasses(bool reverse, bool aligned)
    {
        var document = Create(fixSupport: true, fixMoving: true, aligned: aligned);
        var solved = Solve(document, Incidence(reverse));
        solved.Document.Entities.Should().Equal(document.Entities);
        solved.Document.Dimensions.Should().Equal(document.Dimensions);
        if (aligned)
        {
            solved.Status.Should().Be(SketchSolveStatus.Solved);
            AssertSatisfied(solved.Document);
        }
        else
        {
            solved.Status.Should().NotBe(SketchSolveStatus.Solved);
            solved.Document.Constraints.Should().Contain(c => c.Kind == SketchConstraintKind.PointOnCircle
                && c.State == SketchConstraintState.Unsatisfied);
            var unfixed = new DrawingDocument(solved.Document.Entities, solved.Document.Dimensions,
                solved.Document.Constraints.Where(c => c.Kind != SketchConstraintKind.Fix));
            var recovered = Solve(unfixed, Incidence(reverse));
            recovered.Status.Should().Be(SketchSolveStatus.Solved);
            AssertSatisfied(recovered.Document);
        }
    }

    [Theory]
    [InlineData("support:center")]
    [InlineData("moving:center")]
    public void FixedCentersCannotBeEvadedByChangingDrivingRadii(string fixedCenter)
    {
        var original = Create();
        var other = fixedCenter == "support:center" ? "moving" : "support";
        var document = new DrawingDocument(original.Entities, original.Dimensions,
            [Fix(fixedCenter), Fix(other)]);
        var result = Solve(document, Incidence());
        result.Status.Should().NotBe(SketchSolveStatus.Solved);
        result.Document.Entities.Should().Equal(document.Entities);
        result.Document.Dimensions.Should().Equal(document.Dimensions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LockedGeometryCannotBeAcceptedThroughIncidence(bool lockSupport)
    {
        var original = Create(fixMoving: lockSupport);
        var lockedId = lockSupport ? "support" : "moving";
        var document = new DrawingDocument(original.Entities, original.Dimensions, original.Constraints, original.Metadata with
        {
            EntityStyles = new Dictionary<string, DxfEntityStyle> { [lockedId] = new("Locked") },
            Layers = new Dictionary<string, DxfEntityStyle> { ["Locked"] = new("Locked", IsLocked: true) }
        });
        var result = Solve(document, Incidence());
        if (result.Status == SketchSolveStatus.Solved)
            DrawingLayerService.PreservesProtectedGeometry(document, result.Document).Should().BeFalse("the existing acceptance guard must reject the candidate");
        else result.Document.Entities.Should().Equal(document.Entities);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SupportingCircleMoveAfterReopenUpdatesIncidenceAndLeavesHistoryIntact(bool reverse)
    {
        var document = Reopen(Solved(reverse));
        var snapshot = document.Entities.ToArray();
        SketchGeometryDragService.TryApplyDrag(document, "support|point|center|0|0", new(0, 0), new(2, 1), false,
            out var moved, out var status, false).Should().BeTrue(status);
        Circle(moved, "support").Center.Should().Be(new Point2(2, 1));
        AssertRadii(moved);
        AssertSatisfied(moved);
        document.Entities.Should().Equal(snapshot);
        AssertSatisfied(Reopen(moved));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SupportingRadiusEditKeepsPointOnCircumference(bool reverse)
    {
        var document = Solved(reverse);
        var radius = document.Dimensions.Single(d => d.Id == "support-radius");
        var solved = new LegacySketchSolverAdapter().Solve(new(document, [],
            [new SketchDimension(radius.Id, radius.Kind, radius.ReferenceKeys, 7, radius.Anchor, true)]));
        solved.Status.Should().Be(SketchSolveStatus.Solved);
        Circle(solved.Document, "support").Radius.Should().Be(7);
        Circle(solved.Document, "moving").Radius.Should().Be(2);
        AssertSatisfied(solved.Document);
        Circle(document, "support").Radius.Should().Be(5);
        AssertSatisfied(Reopen(solved.Document));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitPointDragRetainsRequestedPointAndTranslatesFreeSupport(bool reverse)
    {
        var document = Solved(reverse);
        var destination = new Point2(10, 3);
        SketchGeometryDragService.TryApplyDrag(document, "moving|point|center|4|3", new(4, 3), destination, false,
            out var moved, out var status, false).Should().BeTrue(status);
        Circle(moved, "moving").Center.Should().Be(destination);
        Circle(moved, "support").Center.Should().NotBe(Circle(document, "support").Center);
        AssertRadii(moved);
        AssertSatisfied(moved);
        Circle(document, "moving").Center.Should().Be(new Point2(4, 3));
    }

    [Fact]
    public void SuppressedRelationDoesNotMoveTheOtherCircle()
    {
        var original = Create(aligned: true);
        var relation = Incidence();
        var document = new DrawingDocument(original.Entities, original.Dimensions,
            [new SketchConstraint(relation.Id, relation.Kind, relation.ReferenceKeys, SketchConstraintState.Suppressed)]);
        SketchGeometryDragService.TryApplyDrag(document, "support|point|center|0|0", new(0, 0), new(2, 1), false,
            out var moved, out var status, false).Should().BeTrue(status);
        Circle(moved, "moving").Should().Be(Circle(document, "moving"));
        moved.Constraints.Single().State.Should().Be(SketchConstraintState.Suppressed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PointAndLineEndpointUseTheSameIncidenceWithoutMovingUnselectedEndpoint(bool endpoint)
    {
        DrawingEntity moving = endpoint
            ? new LineEntity(EntityId.Create("moving"), new(8, 6), new(11, 9))
            : new PointEntity(EntityId.Create("moving"), new(8, 6));
        var document = new DrawingDocument([new CircleEntity(EntityId.Create("support"), new(0, 0), 5), moving]);
        var key = endpoint ? "moving:start" : "moving";
        SketchCommandFactory.TryBuildConstraint(document, [key, "support"], SketchConstraintKind.Coincident, "incidence",
            out var constraint, out _).Should().BeTrue();
        constraint.Kind.Should().Be(SketchConstraintKind.PointOnCircle);
        var result = Solve(document, constraint);
        result.Status.Should().Be(SketchSolveStatus.Solved);
        SketchReferenceResolver.TryGetPoint(result.Document, key, out var point).Should().BeTrue();
        point.Should().Be(new Point2(4, 3));
        Circle(result.Document, "support").Should().Be(Circle(document, "support"),
            "a free PointEntity or endpoint projects onto the existing circle without moving or resizing it");
        if (endpoint) result.Document.Entities.OfType<LineEntity>().Single().End.Should().Be(new Point2(11, 9));
    }

    private static DrawingDocument Create(bool fixSupport = false, bool fixMoving = false, bool aligned = false) =>
        new([new CircleEntity(EntityId.Create("support"), new(0, 0), 5),
                new CircleEntity(EntityId.Create("moving"), aligned ? new(4, 3) : new(8, 6), 2)],
            [new SketchDimension("support-radius", SketchDimensionKind.Radius, ["support"], 5, new(6, -1), true),
                new SketchDimension("moving-radius", SketchDimensionKind.Radius, ["moving"], 2, new(11, 7), true)],
            (fixSupport ? new[] { Fix("support") } : []).Concat(fixMoving ? new[] { Fix("moving") } : []));

    private static SketchConstraint Fix(string key) => new("fix-" + key, SketchConstraintKind.Fix, [key], SketchConstraintState.Satisfied);
    private static SketchConstraint Incidence(bool reverse = false) => new("incidence", SketchConstraintKind.PointOnCircle,
        reverse ? ["support", "moving:center"] : ["moving:center", "support"]);
    private static SketchSolveResult Solve(DrawingDocument document, SketchConstraint constraint) =>
        new LegacySketchSolverAdapter().Solve(new(document, [constraint], []));
    private static DrawingDocument Solved(bool reverse)
    {
        var result = Solve(Create(), Incidence(reverse));
        result.Status.Should().Be(SketchSolveStatus.Solved);
        return result.Document;
    }
    private static DrawingDocument Reopen(DrawingDocument document) =>
        DxfDocumentReader.Read(DxfDocumentWriter.Write(document, new(IncludeNativeDocument: true)));
    private static CircleEntity Circle(DrawingDocument document, string id) => document.Entities.OfType<CircleEntity>().Single(c => c.Id.Value == id);
    private static string Normalize(string key)
    {
        SketchReference.TryNormalize(key, out var normalized).Should().BeTrue();
        return normalized;
    }
    private static void AssertRadii(DrawingDocument document)
    {
        Circle(document, "support").Radius.Should().Be(5);
        Circle(document, "moving").Radius.Should().Be(2);
    }
    private static void AssertSatisfied(DrawingDocument document)
    {
        document.Constraints.Select(c => SketchConstraintService.ValidateConstraint(document, c).State)
            .Should().OnlyContain(state => state == SketchConstraintState.Satisfied);
        document.Dimensions.Should().OnlyContain(d => SketchDimensionSolverService.IsDimensionSatisfied(document, d));
        var support = Circle(document, "support");
        var point = Circle(document, "moving").Center;
        Math.Sqrt(Math.Pow(point.X - support.Center.X, 2) + Math.Pow(point.Y - support.Center.Y, 2))
            .Should().BeApproximately(support.Radius, 1e-7);
    }
}

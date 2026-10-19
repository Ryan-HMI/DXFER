using DXFER.Blazor.Sketching;
using DXFER.CadIO;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Operations;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Operations;

public sealed class OffsetTangentTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void TangentTranslatesLinkedCirclesWithoutChangingRadiiOrGap(bool outer, bool rectangle, bool reverse)
    {
        var document = CreateCircleAndTarget(rectangle);
        document.Constraints.Should().NotContain(c => c.Kind == SketchConstraintKind.Fix);

        var solved = SolveTangent(document, outer ? "outer" : "inner", "bottom", reverse);

        AssertCircle(solved, "inner", new(15, outer ? 3 : 2), 2);
        AssertCircle(solved, "outer", new(15, outer ? 3 : 2), 3);
        solved.Entities.Where(e => e is LineEntity).Should().Equal(document.Entities.Where(e => e is LineEntity));
        AssertSatisfied(solved);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void TangentToLastCircleInChainTranslatesEveryLinkedCenter(bool reverse, bool reverseRelations)
    {
        var document = AddOffset(CreateCircleAndTarget(false), "outer", "last", new(19, 10));
        if (reverseRelations) document = new(document.Entities, document.Dimensions, document.Constraints.Reverse());

        var solved = SolveTangent(document, "last", "bottom", reverse);

        AssertCircle(solved, "inner", new(15, 4), 2);
        AssertCircle(solved, "outer", new(15, 4), 3);
        AssertCircle(solved, "last", new(15, 4), 4);
        solved.Entities.OfType<LineEntity>().Should().Equal(document.Entities.OfType<LineEntity>());
        solved.Dimensions.Should().HaveCount(2);
        AssertSatisfied(solved);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void TangentToOuterCircleAlsoTranslatesIndependentInsetSibling(bool reverse, bool reverseRelations)
    {
        var document = AddOffset(CreateCircleAndTarget(false), "inner", "sibling", new(16, 10));
        if (reverseRelations) document = new(document.Entities, document.Dimensions, document.Constraints.Reverse());

        var solved = SolveTangent(document, "outer", "bottom", reverse);

        AssertCircle(solved, "inner", new(15, 3), 2);
        AssertCircle(solved, "outer", new(15, 3), 3);
        AssertCircle(solved, "sibling", new(15, 3), 1);
        solved.Entities.OfType<LineEntity>().Should().Equal(document.Entities.OfType<LineEntity>());
        solved.Dimensions.Should().HaveCount(2);
        AssertSatisfied(solved);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TangentAndOffsetIntentSurviveNativeReopenAndSolverReplay(bool reverse)
    {
        var original = CreateCircleAndTarget(true);
        var reopened = Reopen(original);
        reopened.Constraints.Should().BeEquivalentTo(original.Constraints);
        reopened.Dimensions.Should().BeEquivalentTo(original.Dimensions);

        var solved = SolveTangent(reopened, "outer", "bottom", reverse);
        var saved = Reopen(solved);
        saved.Constraints.Should().BeEquivalentTo(solved.Constraints);
        saved.Dimensions.Should().BeEquivalentTo(solved.Dimensions);
        var replay = new LegacySketchSolverAdapter().Solve(new(saved, [], []));

        replay.Status.Should().Be(SketchSolveStatus.Solved, string.Join("; ", replay.Diagnostics));
        AssertCircle(replay.Document, "inner", new(15, 3), 2);
        AssertCircle(replay.Document, "outer", new(15, 3), 3);
        replay.Document.Entities.Should().BeEquivalentTo(solved.Entities);
        AssertSatisfied(replay.Document);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DerivedRectangleSegmentCanBeTheTangentTarget(bool reverse)
    {
        var document = CreateCircleAndTarget(true);
        SelectionOffsetService.TryOffset(document, ["bottom", "right", "top", "left"], new(15, 1),
            _ => EntityId.Create("rectangle-offset"), out document, out _, out var reason, out _, out _, 1,
            retainIntent: true).Should().BeTrue(reason);
        var contour = document.Entities.Single(e => e.Id.Value == "rectangle-offset").Should().BeOfType<PolylineEntity>().Subject;
        var segment = Enumerable.Range(0, contour.Vertices.Count - 1).Single(i =>
            Math.Abs(contour.Vertices[i].Y - 1) < 1e-7 && Math.Abs(contour.Vertices[i + 1].Y - 1) < 1e-7);

        var solved = SolveTangent(document, "outer", $"rectangle-offset|segment|{segment}", reverse);

        AssertCircle(solved, "inner", new(15, 4), 2);
        AssertCircle(solved, "outer", new(15, 4), 3);
        solved.Entities.Where(e => e is not CircleEntity).Should().BeEquivalentTo(document.Entities.Where(e => e is not CircleEntity));
        solved.Dimensions.Should().HaveCount(2);
        AssertSatisfied(solved);
    }

    [Theory]
    [InlineData("source", false)]
    [InlineData("source", true)]
    [InlineData("source-center", false)]
    [InlineData("source-center", true)]
    [InlineData("source-origin", false)]
    [InlineData("source-origin", true)]
    [InlineData("dependent", false)]
    [InlineData("dependent", true)]
    public void LockedCircleAndFixedTargetRefuseWithoutPartialGeometryChanges(string locked, bool reverse)
    {
        var document = CreateCircleAndTarget(false);
        if (locked == "source-origin")
            document = new(document.Entities.Select(e => e.Transform(Transform2.Translation(-15, -10))),
                document.Dimensions, document.Constraints);
        var anchor = locked == "source-origin"
            ? new SketchConstraint("anchor", SketchConstraintKind.Coincident, ["inner:center", "$origin:center"], SketchConstraintState.Satisfied)
            : new SketchConstraint("anchor", SketchConstraintKind.Fix,
                [locked == "dependent" ? "outer" : locked == "source-center" ? "inner:center" : "inner"], SketchConstraintState.Satisfied);
        document = new(document.Entities, document.Dimensions, document.Constraints.Concat([
            anchor, new SketchConstraint("fixed-line", SketchConstraintKind.Fix, ["bottom"], SketchConstraintState.Satisfied)]));
        AssertSatisfied(document);

        var result = ApplyTangent(document, "outer", "bottom", reverse);

        result.Status.Should().NotBe(SketchSolveStatus.Solved);
        result.Document.Entities.Should().BeEquivalentTo(document.Entities);
        result.Document.Dimensions.Should().BeEquivalentTo(document.Dimensions);
        result.Document.Constraints.Should().ContainSingle(c => c.Id == "tangent" && c.State != SketchConstraintState.Satisfied);
        foreach (var constraint in document.Constraints)
            SketchConstraintService.ValidateConstraint(result.Document, constraint).State.Should().Be(SketchConstraintState.Satisfied);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void MixedNewTangentAndGapEditDoNotReverseTranslateOffsetFamily(bool siblingGap, bool reverse)
    {
        var document = CreateCircleAndTarget(false);
        if (siblingGap) document = AddOffset(document, "inner", "sibling", new(18, 10));
        var gap = document.Dimensions.Single(d => d.ReferenceKeys.Last() == (siblingGap ? "sibling" : "outer"));
        var updatedGap = new SketchDimension(gap.Id, gap.Kind, gap.ReferenceKeys, 2, gap.Anchor, true);
        var tangent = BuildTangent(document, "outer", "bottom", reverse);

        // A sibling gap edit would remain compatible with tangency if reverse translation leaked into this request.
        var result = new LegacySketchSolverAdapter().Solve(new(document, [tangent], [updatedGap]));

        result.Status.Should().NotBe(SketchSolveStatus.Solved);
        result.Document.Entities.Should().BeEquivalentTo(document.Entities);
        result.Document.Dimensions.Single(d => d.Id == gap.Id).Value.Should().Be(2);
        result.Document.Constraints.Should().ContainSingle(c => c.Id == tangent.Id && c.State == SketchConstraintState.Unsatisfied);
        foreach (var constraint in document.Constraints)
            SketchConstraintService.ValidateConstraint(result.Document, constraint).State.Should().Be(SketchConstraintState.Satisfied);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SavedFailedTangentDoesNotReverseTranslateUntilDeletedAndReapplied(bool explicitRetry, bool reverse)
    {
        var document = CreateCircleAndTarget(false);
        var tangent = BuildTangent(document, "outer", "bottom", reverse);
        var failed = new SketchConstraint(tangent.Id, tangent.Kind, tangent.ReferenceKeys, SketchConstraintState.Unsatisfied);
        var reopened = Reopen(new(document.Entities, document.Dimensions, document.Constraints.Append(failed)));

        var result = new LegacySketchSolverAdapter().Solve(new(reopened, explicitRetry ? [tangent] : [], []));

        result.Status.Should().NotBe(SketchSolveStatus.Solved);
        result.Document.Entities.Should().BeEquivalentTo(reopened.Entities);
        result.Document.Dimensions.Should().BeEquivalentTo(reopened.Dimensions);
        result.Document.Constraints.Should().ContainSingle(c => c.Id == tangent.Id && c.State == SketchConstraintState.Unsatisfied);
        var withoutFailed = new DrawingDocument(result.Document.Entities, result.Document.Dimensions,
            result.Document.Constraints.Where(c => c.Id != tangent.Id), result.Document.Metadata);
        var reapplied = SolveTangent(withoutFailed, "outer", "bottom", reverse);
        AssertCircle(reapplied, "inner", new(15, 3), 2);
        AssertCircle(reapplied, "outer", new(15, 3), 3);
        AssertSatisfied(reapplied);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ProtectedSiblingPreventsCommittingFamilyTranslation(bool layerLocked, bool reverse)
    {
        var document = AddOffset(CreateCircleAndTarget(false), "inner", "sibling", new(16, 10));
        if (layerLocked)
            document = DrawingLayerService.WithMetadata(document, document.Metadata with
            {
                EntityStyles = new Dictionary<string, DxfEntityStyle> { ["sibling"] = new("Locked") },
                Layers = new Dictionary<string, DxfEntityStyle> { ["Locked"] = new("Locked", IsLocked: true) }
            });
        else
            document = new(document.Entities, document.Dimensions, document.Constraints.Append(
                new SketchConstraint("fixed-sibling", SketchConstraintKind.Fix, ["sibling"], SketchConstraintState.Satisfied)));
        AssertSatisfied(document);

        var result = ApplyTangent(document, "outer", "bottom", reverse);

        if (layerLocked && result.Status == SketchSolveStatus.Solved)
        {
            // Layer protection is enforced by the commit guard, not the sketch solver.
            DrawingLayerService.PreservesProtectedGeometry(document, result.Document).Should().BeFalse();
            AssertSatisfied(result.Document);
        }
        else
        {
            result.Status.Should().NotBe(SketchSolveStatus.Solved);
            result.Document.Entities.Should().BeEquivalentTo(document.Entities);
            result.Document.Dimensions.Should().BeEquivalentTo(document.Dimensions);
        }
        AssertCircle(document, "inner", new(15, 10), 2);
        AssertCircle(document, "outer", new(15, 10), 3);
        AssertCircle(document, "sibling", new(15, 10), 1);
    }

    private static DrawingDocument CreateCircleAndTarget(bool rectangle)
    {
        DrawingEntity[] lines = rectangle
            ? [new LineEntity(EntityId.Create("bottom"), new(0, 0), new(30, 0)),
                new LineEntity(EntityId.Create("right"), new(30, 0), new(30, -6)),
                new LineEntity(EntityId.Create("top"), new(30, -6), new(0, -6)),
                new LineEntity(EntityId.Create("left"), new(0, -6), new(0, 0))]
            : [new LineEntity(EntityId.Create("bottom"), new(0, 0), new(30, 0))];
        var sequence = 0;
        var constraints = rectangle
            ? SketchCreationConstraintFactory.CreateConstraintsForTool("twopointrectangle", lines, k => $"{k}-{++sequence}")
            : [new SketchConstraint("horizontal", SketchConstraintKind.Horizontal, ["bottom"], SketchConstraintState.Satisfied)];
        var document = new DrawingDocument(lines.Prepend(new CircleEntity(EntityId.Create("inner"), new(15, 10), 2)), [], constraints);
        return AddOffset(document, "inner", "outer", new(18, 10));
    }

    private static DrawingDocument AddOffset(DrawingDocument document, string source, string id, Point2 side)
    {
        SelectionOffsetService.TryOffset(document, [source], side, _ => EntityId.Create(id),
            out var result, out _, out var reason, out _, out _, 1, retainIntent: true).Should().BeTrue(reason);
        return result;
    }

    private static DrawingDocument Reopen(DrawingDocument document) =>
        DxfDocumentReader.Read(DxfDocumentWriter.Write(document, new(IncludeNativeDocument: true)));

    private static SketchConstraint BuildTangent(DrawingDocument document, string circle, string line, bool reverse)
    {
        string[] keys = reverse ? [line, circle] : [circle, line];
        SketchCommandFactory.TryBuildConstraint(document, keys, SketchConstraintKind.Tangent, "tangent",
            out var constraint, out var status).Should().BeTrue(status);
        constraint.Kind.Should().Be(SketchConstraintKind.Tangent);
        return constraint;
    }

    private static SketchSolveResult ApplyTangent(DrawingDocument document, string circle, string line, bool reverse) =>
        new LegacySketchSolverAdapter().Solve(new(document, [BuildTangent(document, circle, line, reverse)], []));

    private static DrawingDocument SolveTangent(DrawingDocument document, string circle, string line, bool reverse)
    {
        var result = ApplyTangent(document, circle, line, reverse);
        result.Status.Should().Be(SketchSolveStatus.Solved, string.Join("; ", result.Diagnostics));
        return result.Document;
    }

    private static void AssertCircle(DrawingDocument document, string id, Point2 center, double radius)
    {
        var circle = document.Entities.Single(e => e.Id.Value == id).Should().BeOfType<CircleEntity>().Subject;
        circle.Center.X.Should().BeApproximately(center.X, 1e-7);
        circle.Center.Y.Should().BeApproximately(center.Y, 1e-7);
        circle.Radius.Should().BeApproximately(radius, 1e-7);
    }

    private static void AssertSatisfied(DrawingDocument document)
    {
        document.Constraints.Should().OnlyContain(c => c.State == SketchConstraintState.Satisfied);
        foreach (var constraint in document.Constraints)
            SketchConstraintService.ValidateConstraint(document, constraint).State.Should().Be(SketchConstraintState.Satisfied);
        foreach (var dimension in document.Dimensions)
        {
            dimension.Kind.Should().Be(SketchDimensionKind.Offset);
            dimension.Value.Should().BeApproximately(1, 1e-7);
            SketchDimensionSolverService.IsDimensionSatisfied(document, dimension).Should().BeTrue();
        }
    }
}

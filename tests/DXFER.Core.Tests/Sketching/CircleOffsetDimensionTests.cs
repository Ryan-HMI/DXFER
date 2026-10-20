using DXFER.Blazor.Sketching;
using DXFER.CadIO;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class CircleOffsetDimensionTests
{
    public static IEnumerable<object[]> PickCases()
    {
        foreach (var sourcePick in new[] { "body", "perimeter", "quadrant" })
            foreach (var dependentPick in new[] { "body", "perimeter", "quadrant" })
                foreach (var reverse in new[] { false, true })
                    foreach (var existing in new[] { false, true })
                        yield return [sourcePick, dependentPick, reverse, existing];
    }

    [Theory]
    [MemberData(nameof(PickCases))]
    public void LinkedCirclePicksCreateOrReuseGapAndEditAfterNativeReopen(
        string sourcePick, string dependentPick, bool reverse, bool existing)
    {
        var document = Create(existing);
        var keys = new[] { Pick("source", sourcePick), Pick("dependent", dependentPick) };
        if (reverse) Array.Reverse(keys);
        SketchCommandFactory.TryBuildDimension(document, keys, "new-gap", out var dimension, out var status,
            anchorOverride: new(7, -2)).Should().BeTrue(status);
        dimension.Kind.Should().Be(SketchDimensionKind.Offset);
        dimension.Id.Should().Be(existing ? "existing-gap" : "new-gap");
        dimension.ReferenceKeys.Should().Equal("source", "dependent");
        dimension.Value.Should().Be(1);
        dimension.IsDriving.Should().BeTrue();
        dimension.Anchor.Should().Be(new Point2(7, -2));

        var placed = Solve(document, dimension);
        placed.Status.Should().Be(SketchSolveStatus.Solved);
        placed.Document.Entities.Should().Equal(document.Entities);
        placed.Document.Dimensions.Count(d => d.Kind == SketchDimensionKind.Offset).Should().Be(1);
        placed.Document.Metadata.Should().BeSameAs(document.Metadata);
        var reopened = Reopen(placed.Document);
        reopened.Constraints.Should().BeEquivalentTo(placed.Document.Constraints);
        reopened.Dimensions.Should().BeEquivalentTo(placed.Document.Dimensions);
        var edited = Solve(reopened, WithValue(dimension, 2));
        edited.Status.Should().Be(SketchSolveStatus.Solved);
        Circle(edited.Document, "source").Should().Be(Circle(document, "source"));
        Circle(edited.Document, "dependent").Center.Should().Be(new Point2(0, 0));
        Circle(edited.Document, "dependent").Radius.Should().Be(5);
        AssertSatisfied(edited.Document);
        document.Entities.Should().Equal(placed.Document.Entities, "placing and editing must not mutate the old history snapshot");
        Circle(reopened, "dependent").Radius.Should().Be(4);

        var final = Reopen(edited.Document);
        SketchCommandFactory.TryBuildDimension(final, keys, "another-gap", out var reused, out _).Should().BeTrue();
        reused.Id.Should().Be(dimension.Id);
        reused.Value.Should().Be(2);
        var replaced = Solve(final, reused);
        replaced.Status.Should().Be(SketchSolveStatus.Solved);
        replaced.Document.Dimensions.Count(d => d.Kind == SketchDimensionKind.Offset).Should().Be(1);
    }

    [Theory]
    [InlineData("source:center", "dependent:center", false)]
    [InlineData("source:center", "dependent:center", true)]
    [InlineData("source|point|center|0|0", "dependent|point|quadrant-0|4|0", false)]
    [InlineData("source|point|center|0|0", "dependent|point|quadrant-0|4|0", true)]
    [InlineData("source|point|perimeter|3|0", "dependent:center", false)]
    [InlineData("source|point|perimeter|3|0", "dependent:center", true)]
    public void CenterSelectionsNeverMeanOffsetGap(string first, string second, bool reverse)
    {
        var keys = reverse ? new[] { second, first } : new[] { first, second };
        var document = Create(existing: true);
        if (SketchCommandFactory.TryBuildDimension(document, keys, "not-gap", out var dimension, out _))
        {
            dimension.Kind.Should().NotBe(SketchDimensionKind.Offset);
            dimension.Id.Should().NotBe("existing-gap");
        }
        document.Dimensions.Single(d => d.Kind == SketchDimensionKind.Offset).Value.Should().Be(1);
    }

    [Theory]
    [InlineData("body")]
    [InlineData("perimeter")]
    [InlineData("quadrant")]
    public void IndependentConcentricCirclesAreNotSilentlyLinked(string pick)
    {
        var linked = Create();
        var document = new DrawingDocument(linked.Entities, linked.Dimensions, []);
        SketchCommandFactory.TryBuildDimension(document, [Pick("source", pick), Pick("dependent", pick)],
            "not-gap", out var gap, out var status).Should().BeTrue(status);
        gap.Kind.Should().Be(SketchDimensionKind.PerimeterDistance);
        gap.Value.Should().Be(1);
        document.Constraints.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false, "body")]
    [InlineData(false, "perimeter")]
    [InlineData(false, "quadrant")]
    [InlineData(true, "body")]
    [InlineData(true, "perimeter")]
    [InlineData(true, "quadrant")]
    public void SuppressedOrAmbiguousOffsetRelationNeverCreatesOrReusesOffsetGap(bool ambiguous, string pick)
    {
        var original = Create(existing: true);
        var relation = original.Constraints.Single();
        var relations = ambiguous
            ? new[] { relation, new SketchConstraint("second-offset", relation.Kind, relation.ReferenceKeys, SketchConstraintState.Satisfied) }
            : new[] { new SketchConstraint(relation.Id, relation.Kind, relation.ReferenceKeys, SketchConstraintState.Suppressed) };
        var document = new DrawingDocument(original.Entities, original.Dimensions, relations);
        foreach (var reverse in new[] { false, true })
        {
            var keys = new[] { Pick("source", pick), Pick("dependent", pick) };
            if (reverse) Array.Reverse(keys);
            SketchCommandFactory.TryBuildDimension(document, keys, "not-gap", out _, out var status).Should().BeFalse();
            status.Should().Contain("No single editable Offset relationship");
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void InwardGapRemainsPositiveAndResizesOnlyInnerDependent(bool existing, bool reverse)
    {
        var document = Create(existing, inward: true);
        var keys = new[] { "source", "dependent" };
        if (reverse) Array.Reverse(keys);
        SketchCommandFactory.TryBuildDimension(document, keys, "new-gap", out var gap, out _).Should().BeTrue();
        gap.Value.Should().Be(1);
        gap.ReferenceKeys.Should().Equal("source", "dependent");
        var result = Solve(document, WithValue(gap, 2));
        result.Status.Should().Be(SketchSolveStatus.Solved);
        Circle(result.Document, "source").Should().Be(Circle(document, "source"));
        Circle(result.Document, "dependent").Radius.Should().Be(3);
        Circle(result.Document, "dependent").Center.Should().Be(new Point2(0, 0));
        AssertSatisfied(result.Document);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(5)]
    [InlineData(6)]
    public void InvalidOrCollapsingInwardGapRetainsFailedIntentWithoutMovingGeometry(double value)
    {
        var document = Create(existing: true, inward: true);
        var gap = document.Dimensions.Single(d => d.Kind == SketchDimensionKind.Offset);
        var result = Solve(document, WithValue(gap, value));
        result.Status.Should().NotBe(SketchSolveStatus.Solved);
        result.Document.Entities.Should().Equal(document.Entities);
        result.Document.Dimensions.Single(d => d.Kind == SketchDimensionKind.Offset).Value.Should().Be(value);
        result.Document.Dimensions.Single(d => d.Id == "source-radius").Should().Be(document.Dimensions.Single(d => d.Id == "source-radius"));
    }

    [Fact]
    public void FixedDependentRefusesGapEditWithoutChangingSourceOrCenters()
    {
        var original = Create(existing: true);
        var document = new DrawingDocument(original.Entities, original.Dimensions,
            original.Constraints.Append(new SketchConstraint("fix", SketchConstraintKind.Fix, ["dependent"], SketchConstraintState.Satisfied)));
        var result = Solve(document, WithValue(document.Dimensions.Single(d => d.Kind == SketchDimensionKind.Offset), 2));
        result.Status.Should().NotBe(SketchSolveStatus.Solved);
        result.Document.Entities.Should().Equal(document.Entities);
        result.Document.Dimensions.Single(d => d.Kind == SketchDimensionKind.Offset).Value.Should().Be(2);
    }

    [Fact]
    public void FixedDependentCenterDoesNotPreventRadiusOnlyGapEdit()
    {
        var original = Create(existing: true);
        var document = new DrawingDocument(original.Entities, original.Dimensions,
            original.Constraints.Append(new SketchConstraint("fix", SketchConstraintKind.Fix, ["dependent:center"], SketchConstraintState.Satisfied)));
        var result = Solve(document, WithValue(document.Dimensions.Single(d => d.Kind == SketchDimensionKind.Offset), 2));
        result.Status.Should().Be(SketchSolveStatus.Solved);
        Circle(result.Document, "source").Should().Be(Circle(document, "source"));
        Circle(result.Document, "dependent").Center.Should().Be(new Point2(0, 0));
        Circle(result.Document, "dependent").Radius.Should().Be(5);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DrivingDependentRadiusConflictRefusesRegardlessOfDimensionOrder(bool reverse)
    {
        var original = Create(existing: true);
        var dimensions = original.Dimensions.Append(new SketchDimension("dependent-radius", SketchDimensionKind.Radius,
            ["dependent"], 4, new(6, 3), true));
        if (reverse) dimensions = dimensions.Reverse();
        var document = new DrawingDocument(original.Entities, dimensions, original.Constraints);
        var result = Solve(document, WithValue(document.Dimensions.Single(d => d.Kind == SketchDimensionKind.Offset), 2));
        result.Status.Should().NotBe(SketchSolveStatus.Solved);
        result.Document.Entities.Should().Equal(document.Entities);
        result.Document.Dimensions.Single(d => d.Id == "dependent-radius").Value.Should().Be(4);
        result.Document.Dimensions.Single(d => d.Id == "source-radius").Value.Should().Be(3);
        result.Document.Dimensions.Single(d => d.Kind == SketchDimensionKind.Offset).Value.Should().Be(2);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    public void ReusePreservesReferenceModeOrFailedDrivingIntent(bool driving, double value)
    {
        var original = Create();
        var previous = new SketchDimension("existing-gap", SketchDimensionKind.Offset, ["source", "dependent"], value, new(6, -2), driving);
        var document = new DrawingDocument(original.Entities, original.Dimensions.Append(previous), original.Constraints);
        SketchCommandFactory.TryBuildDimension(document, [Pick("source", "quadrant"), Pick("dependent", "perimeter")],
            "new-gap", out var reused, out _).Should().BeTrue();
        reused.Should().BeEquivalentTo(previous);
        document.Entities.Should().Equal(original.Entities);
    }

    private static string Pick(string id, string kind) => kind switch
    {
        "perimeter" => $"{id}|point|perimeter|999|999",
        "quadrant" => $"{id}|point|quadrant-0|999|999",
        _ => id
    };

    private static DrawingDocument Create(bool existing = false, bool inward = false)
    {
        var sourceRadius = inward ? 5 : 3;
        var dimensions = new List<SketchDimension>
        {
            new("source-radius", SketchDimensionKind.Radius, ["source"], sourceRadius, new(5, -3), true)
        };
        if (existing) dimensions.Add(new("existing-gap", SketchDimensionKind.Offset, ["source", "dependent"], 1, new(6, -2), true));
        return new([new CircleEntity(EntityId.Create("source"), new(0, 0), sourceRadius),
            new CircleEntity(EntityId.Create("dependent"), new(0, 0), 4)], dimensions,
            [new SketchConstraint("offset", SketchConstraintKind.Offset, ["source", "dependent"], SketchConstraintState.Satisfied)],
            new DrawingDocumentMetadata { SourceFileName = "circle-offset-fixture.dxf" });
    }

    private static SketchDimension WithValue(SketchDimension dimension, double value) =>
        new(dimension.Id, dimension.Kind, dimension.ReferenceKeys, value, dimension.Anchor, true);
    private static SketchSolveResult Solve(DrawingDocument document, SketchDimension dimension) =>
        new LegacySketchSolverAdapter().Solve(new(document, [], [dimension]));
    private static CircleEntity Circle(DrawingDocument document, string id) => document.Entities.OfType<CircleEntity>().Single(c => c.Id.Value == id);
    private static DrawingDocument Reopen(DrawingDocument document) => DxfDocumentReader.Read(DxfDocumentWriter.Write(document, new(IncludeNativeDocument: true)));
    private static void AssertSatisfied(DrawingDocument document)
    {
        document.Constraints.Select(c => SketchConstraintService.ValidateConstraint(document, c).State).Should().OnlyContain(s => s == SketchConstraintState.Satisfied);
        document.Dimensions.Should().OnlyContain(d => SketchDimensionSolverService.IsDimensionSatisfied(document, d));
    }
}

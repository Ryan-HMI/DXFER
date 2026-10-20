using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Operations;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class SketchDirectEditValidationServiceTests
{
    [Theory]
    [InlineData("fixed")]
    [InlineData("driving")]
    [InlineData("failed")]
    [InlineData("suppressed")]
    public void UnrelatedTransformPreservesExistingIntentAndStates(string intent)
    {
        var original = Document(intent);
        var candidate = DrawingPrepService.TransformSelected(original, ["free"], Transform2.Translation(3, 4));
        SketchDirectEditValidationService.TryValidate(original, candidate, out var reason).Should().BeTrue(reason);
        candidate.Constraints.Should().Equal(original.Constraints);
        candidate.Dimensions.Should().Equal(original.Dimensions);
        candidate.Entities[0].Should().BeSameAs(original.Entities[0]);
    }

    [Fact]
    public void RigidTransformOfDrivingGeometryIsAcceptedWithoutChangingItsValue()
    {
        var original = Document("driving");
        var candidate = DrawingPrepService.TransformSelected(original, ["edge"],
            Transform2.RotationDegreesAbout(90, new(0, 0)));
        SketchDirectEditValidationService.TryValidate(original, candidate, out var reason).Should().BeTrue(reason);
        candidate.Dimensions.Single().Value.Should().Be(10);
    }

    [Theory]
    [InlineData("edge")]
    [InlineData("edge:start")]
    [InlineData("edge:mid")]
    public void OriginalFixedReferenceCannotMove(string fixedKey)
    {
        var original = new DrawingDocument(Entities(), [],
            [new SketchConstraint("fixed", SketchConstraintKind.Fix, [fixedKey], SketchConstraintState.Satisfied)]);
        var candidate = DrawingPrepService.TransformSelected(original, ["edge"], Transform2.Translation(3, 4));
        SketchDirectEditValidationService.TryValidate(original, candidate, out var reason).Should().BeFalse();
        reason.Should().Contain("original fixed reference");
    }

    [Fact]
    public void FixedStartAllowsRotationAboutThatSameStart()
    {
        var original = new DrawingDocument(Entities(), [],
            [new SketchConstraint("fixed", SketchConstraintKind.Fix, ["edge:start"], SketchConstraintState.Satisfied)]);
        var candidate = DrawingPrepService.TransformSelected(original, ["edge"],
            Transform2.RotationDegreesAbout(90, new(0, 0)));
        SketchDirectEditValidationService.TryValidate(original, candidate, out var reason).Should().BeTrue(reason);
    }

    [Theory]
    [InlineData("horizontal")]
    [InlineData("driving")]
    public void InvalidResidualIsRefusedWithoutRepairingTheCandidate(string intent)
    {
        var original = Document(intent);
        var changed = new LineEntity(EntityId.Create("edge"), new(0, 0), new(0, 20));
        var candidate = new DrawingDocument([changed, original.Entities[1]], original.Dimensions,
            original.Constraints, original.Metadata);
        SketchDirectEditValidationService.TryValidate(original, candidate, out _).Should().BeFalse();
        candidate.Entities[0].Should().BeSameAs(changed);
        original.Entities[0].Should().Be(new LineEntity(EntityId.Create("edge"), new(0, 0), new(10, 0)));
    }

    [Theory]
    [InlineData("drop")]
    [InlineData("id")]
    [InlineData("state")]
    [InlineData("kind")]
    [InlineData("reference")]
    public void ConstraintIdentityAndStateCannotBeRewrittenByADirectEdit(string change)
    {
        var original = Document("fixed");
        var constraint = original.Constraints.Single();
        IEnumerable<string> references = change == "reference" ? new[] { "free" } : constraint.ReferenceKeys;
        var candidate = new DrawingDocument(original.Entities, [], change == "drop" ? [] :
            [new SketchConstraint(change == "id" ? "renamed" : constraint.Id,
                change == "kind" ? SketchConstraintKind.Horizontal : constraint.Kind,
                references,
                change == "state" ? SketchConstraintState.Suppressed : constraint.State)]);
        SketchDirectEditValidationService.TryValidate(original, candidate, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("value")]
    [InlineData("id")]
    [InlineData("driven")]
    [InlineData("reference")]
    public void DrivingIntentCannotBeRewrittenToMakeACandidateValid(string change)
    {
        var original = Document("driving");
        var candidate = new DrawingDocument(original.Entities,
            [new SketchDimension(change == "id" ? "renamed" : "length", SketchDimensionKind.LinearDistance,
                change == "reference" ? ["free:start", "free:end"] : ["edge:start", "edge:end"],
                change == "value" ? 20 : 10, new(5, -2), isDriving: change != "driven")], []);
        SketchDirectEditValidationService.TryValidate(original, candidate, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TopologyEditsRequireUnreferencedAffectedGeometry(bool attached)
    {
        var original = Document("horizontal");
        var id = attached ? "edge" : "free";
        var old = (LineEntity)original.Entities.Single(e => e.Id.Value == id);
        var midpoint = new Point2((old.Start.X + old.End.X) / 2, (old.Start.Y + old.End.Y) / 2);
        var candidate = new DrawingDocument(original.Entities.Select(e => e.Id.Value == id
                ? old with { End = midpoint } : e)
            .Append(new LineEntity(EntityId.Create("fragment"), midpoint, old.End)),
            original.Dimensions, original.Constraints, original.Metadata);
        SketchDirectEditValidationService.TryValidate(original, candidate, out var reason, topologyChange: true)
            .Should().Be(!attached, reason);
        if (attached) reason.Should().Contain("Topology reference lineage");
    }

    [Fact]
    public void ProtectedGeometryCannotMoveEvenWithUnlockedCandidateMetadata()
    {
        var metadata = new DrawingDocumentMetadata
        {
            Layers = new Dictionary<string, DxfEntityStyle> { ["locked"] = new("locked", IsLocked: true) },
            EntityStyles = new Dictionary<string, DxfEntityStyle> { ["edge"] = new("locked") }
        };
        var original = new DrawingDocument(Entities(), [], [], metadata);
        var moved = DrawingPrepService.TransformSelected(original, ["edge"], Transform2.Translation(3, 4));
        SketchDirectEditValidationService.TryValidate(original, moved, out _).Should().BeFalse();
        var unprotected = new DrawingDocument(moved.Entities, [], [], DrawingDocumentMetadata.Empty);
        SketchDirectEditValidationService.TryValidate(original, unprotected, out _).Should().BeFalse();
    }

    [Fact]
    public void ProtectedDimensionLabelCannotBeMovedByAnUnrelatedDirectEdit()
    {
        var baseDocument = Document("driving");
        var original = new DrawingDocument(baseDocument.Entities, baseDocument.Dimensions, [],
            new DrawingDocumentMetadata
            {
                Layers = new Dictionary<string, DxfEntityStyle> { ["locked"] = new("locked", IsLocked: true) },
                EntityStyles = new Dictionary<string, DxfEntityStyle> { ["edge"] = new("locked") }
            });
        var moved = DrawingPrepService.TransformSelected(original, ["free"], Transform2.Translation(3, 4));
        var candidate = new DrawingDocument(moved.Entities,
            [new SketchDimension("length", SketchDimensionKind.LinearDistance, ["edge:start", "edge:end"],
                10, new(50, 50), isDriving: true)], [], original.Metadata);
        SketchDirectEditValidationService.TryValidate(original, candidate, out var reason).Should().BeFalse();
        reason.Should().Contain("protected dimension");
    }

    [Fact]
    public void OriginalOriginRelationCannotBeTranslatedAway()
    {
        var original = new DrawingDocument(Entities(), [],
            [new SketchConstraint("origin", SketchConstraintKind.Coincident,
                ["edge:start", SketchReference.OriginKey], SketchConstraintState.Satisfied)]);
        var candidate = DrawingPrepService.TransformSelected(original, ["edge"], Transform2.Translation(3, 4));
        SketchDirectEditValidationService.TryValidate(original, candidate, out var reason).Should().BeFalse();
        reason.Should().Contain("constraint 'origin'");
    }

    [Fact]
    public void ReadOnlyOriginalCannotBeMadeEditableByCandidateMetadata()
    {
        var original = new DrawingDocument(Entities(), [], [],
            DrawingDocumentMetadata.Empty with { Mode = DrawingDocumentMode.ReferenceOnly });
        var candidate = new DrawingDocument(original.Entities);
        SketchDirectEditValidationService.TryValidate(original, candidate, out var reason).Should().BeFalse();
        reason.Should().Contain("Reference-only");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnrelatedPatternEditsAreAllowedButStaleGeneratedMembersAreNot(bool moveSource)
    {
        var seed = new DrawingDocument(Entities());
        DrawingPatternService.TryCreate(seed, new PatternDefinition
        {
            Id = "pattern", Kind = PatternKind.Rectangular, SourceEntityIds = ["edge"],
            Rows = 1, Columns = 2, XPitch = 20
        }, out var original, out var creationReason).Should().BeTrue(creationReason);
        var candidate = DrawingPrepService.TransformSelected(original,
            [moveSource ? "edge" : "free"], Transform2.Translation(3, 4));
        SketchDirectEditValidationService.TryValidate(original, candidate, out var reason)
            .Should().Be(!moveSource, reason);
        if (moveSource) reason.Should().Contain("pattern dependencies");
    }

    [Fact]
    public void FailedIntentCannotBeChangedButDoesNotBlockUnrelatedEdits()
    {
        var original = Document("failed");
        var candidate = DrawingPrepService.TransformSelected(original, ["edge"], Transform2.Translation(3, 4));
        SketchDirectEditValidationService.TryValidate(original, candidate, out var reason).Should().BeFalse();
        reason.Should().Contain("failed intent");
        original.Constraints.Single().State.Should().Be(SketchConstraintState.Unsatisfied);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RetainedFailedIntentDoesNotExcuseANewViolation(bool driving)
    {
        var failed = new SketchConstraint("already-failed", SketchConstraintKind.Vertical,
            ["edge"], SketchConstraintState.Unsatisfied);
        var original = new DrawingDocument(Entities(),
            driving ? [new SketchDimension("free-length", SketchDimensionKind.LinearDistance,
                ["free:start", "free:end"], 10, new(25, -2), isDriving: true)] : [],
            driving ? [failed] : [failed, new SketchConstraint("previously-valid",
                SketchConstraintKind.Horizontal, ["free"], SketchConstraintState.Satisfied)]);
        var candidate = new DrawingDocument(
            [original.Entities[0], new LineEntity(EntityId.Create("free"), new(20, 0), new(20, 20))],
            original.Dimensions, original.Constraints, original.Metadata);

        SketchDirectEditValidationService.TryValidate(original, candidate, out var reason).Should().BeFalse();
        reason.Should().Contain(driving ? "free-length" : "previously-valid");
        candidate.Constraints[0].Should().BeSameAs(failed);
        failed.State.Should().Be(SketchConstraintState.Unsatisfied);
    }

    [Fact]
    public void NonFiniteCandidateIsExplicitlyRefused()
    {
        var original = new DrawingDocument(Entities());
        var candidate = new DrawingDocument(
            [new LineEntity(EntityId.Create("edge"), new(double.NaN, 0), new(10, 0)), original.Entities[1]]);
        SketchDirectEditValidationService.TryValidate(original, candidate, out var reason).Should().BeFalse();
        reason.Should().Contain("could not be validated");
    }

    private static DrawingEntity[] Entities() =>
    [
        new LineEntity(EntityId.Create("edge"), new(0, 0), new(10, 0)),
        new LineEntity(EntityId.Create("free"), new(20, 0), new(30, 0))
    ];

    private static DrawingDocument Document(string intent) => new(Entities(),
        intent == "driving"
            ? [new SketchDimension("length", SketchDimensionKind.LinearDistance, ["edge:start", "edge:end"],
                10, new(5, -2), isDriving: true)] : [],
        intent == "driving" ? [] :
        [new SketchConstraint("intent", intent switch
        {
            "fixed" => SketchConstraintKind.Fix,
            "failed" => SketchConstraintKind.Vertical,
            _ => SketchConstraintKind.Horizontal
        }, ["edge"], intent switch
        {
            "failed" => SketchConstraintState.Unsatisfied,
            "suppressed" => SketchConstraintState.Suppressed,
            _ => SketchConstraintState.Satisfied
        })]);
}

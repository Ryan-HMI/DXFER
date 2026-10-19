using DXFER.Blazor.Selection;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Selection;

public sealed class AxisConstraintRecoveryTests
{
    private static DrawingDocument Fixture(bool vertical = true, IEnumerable<SketchConstraint>? extra = null,
        IEnumerable<SketchDimension>? dimensions = null, DrawingDocumentMetadata? metadata = null) => new(
        new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("edge"), new Point2(0, 0), vertical ? new Point2(0, 10) : new Point2(10, 0)),
            new CircleEntity(EntityId.Create("other"), new Point2(30, 30), 3)
        }, dimensions ?? Array.Empty<SketchDimension>(),
        new[]
        {
            new SketchConstraint("old", vertical ? SketchConstraintKind.Vertical : SketchConstraintKind.Horizontal, new[] { "edge" }, SketchConstraintState.Satisfied),
            new SketchConstraint("pending", vertical ? SketchConstraintKind.Horizontal : SketchConstraintKind.Vertical, new[] { "edge" }, SketchConstraintState.Unsatisfied)
        }.Concat(extra ?? Array.Empty<SketchConstraint>()), metadata ?? DrawingDocumentMetadata.Empty);

    private static SelectionDeleteResult Delete(DrawingDocument before, params string[] keys) =>
        SelectionDeleteResolver.RecoverAxisConstraints(before, SelectionDeleteResolver.DeleteSelection(before, keys));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DeletingOppositeAxisRecoversPendingIntentOnTheSameLine(bool vertical)
    {
        var before = Fixture(vertical);
        var deletionOnly = SelectionDeleteResolver.DeleteSelection(before, new[] { "constraint:old" });
        deletionOnly.Document.Entities.Should().Equal(before.Entities, "selection availability must not solve");
        var result = SelectionDeleteResolver.RecoverAxisConstraints(before, deletionOnly);
        result.DeletedConstraints.Should().Be(1);
        result.Document.Constraints.Should().ContainSingle().Which.State.Should().Be(SketchConstraintState.Satisfied);
        result.Document.Constraints[0].Id.Should().Be("pending");
        var line = result.Document.Entities[0].Should().BeOfType<LineEntity>().Subject;
        line.Start.Should().Be(new Point2(0, 0));
        line.End.Should().Be(vertical ? new Point2(10, 0) : new Point2(0, 10));
        result.Document.Entities[1].Should().BeSameAs(before.Entities[1]);
        before.Constraints.Should().HaveCount(2);
    }

    [Fact]
    public void FailedRecoveryRetainsDeletionAndOriginalGeometry()
    {
        var before = Fixture(extra: new[] { new SketchConstraint("fix", SketchConstraintKind.Fix, new[] { "edge" }) });
        var result = Delete(before, "constraint:old");
        result.Document.Entities.Should().Equal(before.Entities);
        result.Document.Constraints.Should().NotContain(c => c.Id == "old");
        result.Document.Constraints.Single(c => c.Id == "pending").State.Should().Be(SketchConstraintState.Unsatisfied);
        result.Document.Constraints.Single(c => c.Id == "fix").Should().BeSameAs(before.Constraints.Single(c => c.Id == "fix"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void HiddenOrLockedGeometryCannotMoveDuringRecovery(bool locked)
    {
        var metadata = DrawingDocumentMetadata.Empty with
        {
            Layers = new Dictionary<string, DxfEntityStyle> { ["0"] = new("0", IsLocked: locked, IsHidden: !locked) }
        };
        var before = Fixture(metadata: metadata);
        var result = Delete(before, "constraint:old");
        result.Document.Entities.Should().Equal(before.Entities);
        result.Document.Constraints.Should().ContainSingle().Which.State.Should().Be(SketchConstraintState.Unsatisfied);
        result.Document.Metadata.Should().BeSameAs(metadata);
    }

    [Fact]
    public void DrivingConflictIsPreservedRatherThanRewrittenToFitTheAxis()
    {
        var dimension = new SketchDimension("rise", SketchDimensionKind.VerticalDistance,
            new[] { "edge:start", "edge:end" }, 10, new Point2(2, 5), true);
        var before = Fixture(dimensions: new[] { dimension });
        var result = Delete(before, "constraint:old");
        result.Document.Entities.Should().Equal(before.Entities);
        result.Document.Dimensions.Should().Equal(before.Dimensions);
        result.Document.Constraints.Should().ContainSingle().Which.State.Should().Be(SketchConstraintState.Unsatisfied);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SuppressedRemovedOrRemainingIntentDoesNotTriggerRecovery(bool suppressRemoved)
    {
        var original = Fixture();
        var before = new DrawingDocument(original.Entities, original.Dimensions,
            original.Constraints.Select(c => new SketchConstraint(c.Id, c.Kind, c.ReferenceKeys,
                c.Id == (suppressRemoved ? "old" : "pending") ? SketchConstraintState.Suppressed : c.State)), original.Metadata);
        var deletion = SelectionDeleteResolver.DeleteSelection(before, new[] { "constraint:old" });
        SelectionDeleteResolver.RecoverAxisConstraints(before, deletion).Should().Be(deletion);
    }

    [Fact]
    public void DeletingTheFailedIntentLeavesTheOriginalAxisAndGeometry()
    {
        var before = Fixture();
        var result = Delete(before, "constraint:pending");
        result.Document.Entities.Should().Equal(before.Entities);
        result.Document.Constraints.Should().Equal(before.Constraints.Where(c => c.Id == "old"));
    }

    [Fact]
    public void DifferentEntityOrGeometryDeletionDoesNotRetryPendingIntent()
    {
        var original = Fixture(extra: new[] { new SketchConstraint("unrelated", SketchConstraintKind.Vertical, new[] { "other" }) });
        foreach (var selection in new[] { new[] { "constraint:unrelated" }, new[] { "other", "constraint:old" } })
        {
            var deletion = SelectionDeleteResolver.DeleteSelection(original, selection);
            SelectionDeleteResolver.RecoverAxisConstraints(original, deletion).Should().Be(deletion);
        }
    }

    [Fact]
    public void UnrelatedFailedIntentIsNotRetriedAndPreventsPartialRecovery()
    {
        var original = Fixture();
        var other = new LineEntity(EntityId.Create("unrelated"), new Point2(20, 20), new Point2(25, 24));
        var before = new DrawingDocument(original.Entities.Append(other), original.Dimensions,
            original.Constraints.Append(new SketchConstraint("unrelated-h", SketchConstraintKind.Horizontal,
                new[] { "unrelated" }, SketchConstraintState.Unsatisfied)), original.Metadata);
        var result = Delete(before, "constraint:old");
        result.Document.Entities.Should().Equal(before.Entities);
        result.Document.Constraints.Should().NotContain(c => c.Id == "old");
        result.Document.Constraints.Single(c => c.Id == "unrelated-h").State.Should().Be(SketchConstraintState.Unsatisfied);
    }

    [Fact]
    public void UnrelatedUnmetDrivingValueIsNotAppliedByAxisRecovery()
    {
        var radius = new SketchDimension("old-radius", SketchDimensionKind.Radius, new[] { "other" }, 5,
            new Point2(35, 30), true);
        var before = Fixture(dimensions: new[] { radius });
        var result = Delete(before, "constraint:old");
        result.Document.Entities.Should().Equal(before.Entities);
        result.Document.Dimensions.Should().Equal(before.Dimensions);
        result.Document.Constraints.Should().ContainSingle().Which.State.Should().Be(SketchConstraintState.Unsatisfied);
    }
}

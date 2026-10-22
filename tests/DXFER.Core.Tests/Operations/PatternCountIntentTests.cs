using DXFER.CadIO;
using DXFER.Core.Documents;
using DXFER.Core.IO;
using DXFER.Core.Operations;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Operations;

public sealed class PatternCountIntentTests
{
    [Theory]
    [InlineData(PatternKind.Radial, 2)]
    [InlineData(PatternKind.Radial, 6)]
    [InlineData(PatternKind.Rectangular, 2)]
    [InlineData(PatternKind.Rectangular, 6)]
    [InlineData(PatternKind.Staggered, 2)]
    [InlineData(PatternKind.Staggered, 6)]
    public void ExplicitCountEditRemovesOnlyMemberIntentAndRoundTrips(PatternKind kind, int count)
    {
        var original = Fixture(kind);
        var snapshot = DxferDocumentSerializer.Write(original);
        if (Environment.GetEnvironmentVariable("DXFER_PATTERN_COUNT_FIXTURES") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, $"{kind}.dxf"), DxfDocumentWriter.Write(original, new(IncludeNativeDocument: true)));
        }
        var pattern = original.Metadata.Patterns.Single();
        var changed = pattern with { Count = count, Columns = count };
        DrawingPatternService.TryEditParameters(original, changed, out var edited, out var reason).Should().BeTrue(reason);
        edited.Dimensions.Select(d => d.Id).Should().Equal("source-radius", "other-radius");
        edited.Constraints.Select(c => c.Id).Should().Equal("source-fixed", "other-fixed", "center-fixed");
        edited.Entities.Single(e => e.Id.Value == "seed").Should().Be(original.Entities.Single(e => e.Id.Value == "seed"));
        edited.Entities.Single(e => e.Id.Value == "other").Should().Be(original.Entities.Single(e => e.Id.Value == "other"));
        DxferDocumentSerializer.Write(original).Should().Be(snapshot, "the returned document is a staged draft");
        var reopened = DxfDocumentReader.Read(DxfDocumentWriter.Write(edited, new(IncludeNativeDocument: true)), out var restored);
        restored.Should().BeTrue();
        DxferDocumentSerializer.Write(reopened).Should().Be(DxferDocumentSerializer.Write(edited));
        DrawingPatternService.TryEditParameters(reopened, changed with { Count = 3, Columns = 3 }, out var again, out reason)
            .Should().BeTrue(reason);
        again.Dimensions.Select(d => d.Id).Should().Equal(edited.Dimensions.Select(d => d.Id));
    }

    [Fact]
    public void NonCountEditDoesNotDetachIntent()
    {
        var original = Fixture(PatternKind.Radial);
        var pattern = original.Metadata.Patterns.Single();
        DrawingPatternService.TryEditParameters(original, pattern with { Center = new(1, 2) }, out var refused, out _).Should().BeFalse();
        refused.Should().BeSameAs(original);
        DrawingPatternService.TryEditParameters(original, pattern, out var unchanged, out var reason).Should().BeTrue(reason);
        unchanged.Dimensions.Should().Equal(original.Dimensions);
        unchanged.Constraints.Should().Equal(original.Constraints);
    }

    [Theory]
    [InlineData("locked")]
    [InlineData("hidden")]
    [InlineData("readonly")]
    [InlineData("invalid")]
    public void RejectedCountEditReturnsOriginalIncludingMemberIntent(string mode)
    {
        var original = Fixture(PatternKind.Radial);
        var member = original.Metadata.Patterns.Single().Members.First().EntityId;
        if (mode is "locked" or "hidden")
            original = DrawingLayerService.WithMetadata(original, original.Metadata with
            {
                EntityStyles = new Dictionary<string, DxfEntityStyle> { [member] = new("protected") },
                Layers = new Dictionary<string, DxfEntityStyle>
                { ["protected"] = new("protected", IsLocked: mode == "locked", IsHidden: mode == "hidden") }
            });
        if (mode == "readonly")
            original = DrawingLayerService.WithMetadata(original, original.Metadata with { Mode = DrawingDocumentMode.ReferenceOnly });
        DrawingPatternService.TryEditParameters(original, original.Metadata.Patterns.Single() with { Count = mode == "invalid" ? 0 : 6 },
            out var refused, out _).Should().BeFalse();
        refused.Should().BeSameAs(original);
        refused.Constraints.Should().Contain(c => c.Id == "member-fixed");
    }

    private static DrawingDocument Fixture(PatternKind kind)
    {
        var seed = new DrawingDocument([
            new CircleEntity(EntityId.Create("seed"), new(10, 0), 1),
            new CircleEntity(EntityId.Create("other"), new(30, 30), 2),
            new PointEntity(EntityId.Create("center"), new(0, 0), IsConstruction: true)
        ]);
        var definition = new PatternDefinition
        {
            Id = "pattern", Kind = kind, SourceEntityIds = ["seed"], Count = 4,
            Rows = 2, Columns = 4, XPitch = 3, YPitch = 3,
            CenterPointId = kind == PatternKind.Radial ? "center" : null
        };
        DrawingPatternService.TryCreate(seed, definition, out var document, out var reason).Should().BeTrue(reason);
        var members = document.Metadata.Patterns.Single().Members;
        return new DrawingDocument(document.Entities,
            [new SketchDimension("member-radius", SketchDimensionKind.Radius, [members[0].EntityId], 1),
             new SketchDimension("member-reference", SketchDimensionKind.Radius, [members[^1].EntityId], 1, isDriving: false),
             new SketchDimension("source-radius", SketchDimensionKind.Radius, ["seed"], 1, isDriving: false),
             new SketchDimension("other-radius", SketchDimensionKind.Radius, ["other"], 2, isDriving: false)],
            [new SketchConstraint("member-fixed", SketchConstraintKind.Fix, [members[^1].EntityId], SketchConstraintState.Satisfied),
             new SketchConstraint("member-suppressed", SketchConstraintKind.Equal, ["seed", members[^1].EntityId], SketchConstraintState.Suppressed),
             new SketchConstraint("source-fixed", SketchConstraintKind.Fix, ["seed"], SketchConstraintState.Satisfied),
             new SketchConstraint("other-fixed", SketchConstraintKind.Fix, ["other"], SketchConstraintState.Satisfied),
             new SketchConstraint("center-fixed", SketchConstraintKind.Fix, ["center"], SketchConstraintState.Satisfied)], document.Metadata);
    }
}

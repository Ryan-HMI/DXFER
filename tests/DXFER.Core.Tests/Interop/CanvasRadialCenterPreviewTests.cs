using System.Text.Json;
using DXFER.Blazor.Interop;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Operations;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Interop;

public sealed class CanvasRadialCenterPreviewTests
{
    private static DrawingDocument Pattern(bool exposed = false, bool line = false)
    {
        DrawingEntity seed = line
            ? new LineEntity(EntityId.Create("seed"), new(4, 2), new(6, 3))
            : new CircleEntity(EntityId.Create("seed"), new(4, 2), 1);
        var document = new DrawingDocument([seed, new LineEntity(EntityId.Create("guide"), new(10, 10), new(10, 15))]);
        DrawingPatternService.TryCreate(document, new PatternDefinition
        {
            Id = "radial", Kind = PatternKind.Radial, SourceEntityIds = ["seed"], Count = 4, Center = default
        }, out document, out var reason).Should().BeTrue(reason);
        if (exposed) DrawingPatternService.TryExposeCenter(document, "radial", out document, out reason).Should().BeTrue(reason);
        return document;
    }

    private static PatternDefinition Definition(DrawingDocument document) => document.Metadata.Patterns.Single(p => p.Id == "radial");
    private static string Member(DrawingDocument document) => Definition(document).Members[0].EntityId;
    private static CanvasRadialCenterPreviewDto Preview(DrawingDocument document) =>
        CanvasDocumentDto.FromDocument(document).RadialCenterPreviews.Should().ContainSingle().Subject;
    private static void Blocked(DrawingDocument document) => CanvasDocumentDto.FromDocument(document).RadialCenterPreviews.Should().BeEmpty();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SafeLegacyAndExposedPatternsHaveExactWireSchema(bool exposed)
    {
        var document = Pattern(exposed);
        var definition = Definition(document);
        var descriptor = Preview(document);
        descriptor.Id.Should().Be("radial");
        descriptor.Center.Should().Be(new CanvasPointDto(0, 0));
        descriptor.CenterPointId.Should().Be(definition.CenterPointId);
        descriptor.StepAngleDegrees.Should().Be(90);
        descriptor.Members.Select(m => m.EntityId).Should().Equal(definition.Members.Select(m => m.EntityId));
        descriptor.Members.Select(m => m.Column).Should().Equal(1, 2, 3);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(CanvasDocumentDto.FromDocument(document)));
        var wire = json.RootElement.GetProperty("radialCenterPreviews")[0];
        wire.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("id", "center", "centerPointId", "stepAngleDegrees", "members");
        wire.GetProperty("members")[0].EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("entityId", "column");
        if (!exposed) wire.GetProperty("centerPointId").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Theory]
    [InlineData(35)]
    [InlineData(-35)]
    public void PartialRadialPatternsExposeSignedEffectiveStep(double step)
    {
        var document = Pattern();
        DrawingPatternService.TryUpdate(document, Definition(document) with { FullCircle = false, StepAngleDegrees = step },
            out document, out var reason).Should().BeTrue(reason);
        Preview(document).StepAngleDegrees.Should().Be(step);
    }

    [Fact]
    public void MultipleSourcesExposeEveryGeneratedMemberButNeverSeeds()
    {
        var document = Pattern();
        DrawingPatternService.TryUpdate(document, Definition(document) with { SourceEntityIds = ["seed", "guide"] },
            out document, out var reason).Should().BeTrue(reason);
        var preview = Preview(document);
        preview.Members.Should().HaveCount(6);
        preview.Members.Select(m => m.EntityId).Should().NotContain("seed").And.NotContain("guide");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SourceOnlyFixedAndDimensionIntentRemainEligible(bool exposed)
    {
        var document = Pattern(exposed);
        document = new DrawingDocument(document.Entities,
            [new("source-radius", SketchDimensionKind.Radius, ["seed"], 1, isDriving: true)],
            [new("fixed-seed", SketchConstraintKind.Fix, ["seed"], SketchConstraintState.Satisfied)], document.Metadata);
        Preview(document).Id.Should().Be("radial");
    }

    [Theory]
    [InlineData(SketchConstraintState.Satisfied)]
    [InlineData(SketchConstraintState.Suppressed)]
    [InlineData(SketchConstraintState.Unsatisfied)]
    [InlineData(SketchConstraintState.Unknown)]
    public void AnyConstraintOnMembersOrCenterDisablesPreview(SketchConstraintState state)
    {
        var document = Pattern(true);
        foreach (var key in new[] { Member(document), Member(document) + ":center", Definition(document).CenterPointId!, Definition(document).CenterPointId + ":center" })
        {
            var constrained = new DrawingDocument(document.Entities, document.Dimensions,
                [new("protected", SketchConstraintKind.Fix, [key], state)], document.Metadata);
            Blocked(constrained);
        }
    }

    [Fact]
    public void OffsetsAndCanvasPointReferencesCannotBypassTheIntentGate()
    {
        var document = Pattern(true);
        foreach (var constraint in new[]
        {
            new SketchConstraint("offset", SketchConstraintKind.Offset, ["seed", Member(document)], SketchConstraintState.Satisfied),
            new SketchConstraint("canvas", SketchConstraintKind.Fix, [Member(document) + "|point|center|0|0"], SketchConstraintState.Suppressed)
        }) Blocked(new DrawingDocument(document.Entities, document.Dimensions, [constraint], document.Metadata));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DrivingAndReferenceDimensionsOnMembersOrCenterDisablePreview(bool driving)
    {
        var document = Pattern(true);
        foreach (var dimension in new[]
        {
            new SketchDimension("member-radius", SketchDimensionKind.Radius, [Member(document)], 1, isDriving: driving),
            new SketchDimension("center-position", SketchDimensionKind.HorizontalDistance,
                [Definition(document).CenterPointId!, SketchReference.OriginKey], 0, isDriving: driving)
        }) Blocked(new DrawingDocument(document.Entities, [dimension], document.Constraints, document.Metadata));
    }

    [Theory]
    [InlineData("seed", false)]
    [InlineData("seed", true)]
    [InlineData("member", false)]
    [InlineData("member", true)]
    [InlineData("center", false)]
    [InlineData("center", true)]
    public void HiddenOrLockedMovedGeometryAndSourcesDisablePreview(string target, bool hidden)
    {
        var document = Pattern(true);
        var id = target == "center" ? Definition(document).CenterPointId! : target == "member" ? Member(document) : "seed";
        var metadata = document.Metadata with
        {
            EntityStyles = new Dictionary<string, DxfEntityStyle>(document.Metadata.EntityStyles) { [id] = new("protected") },
            Layers = new Dictionary<string, DxfEntityStyle> { ["protected"] = new("protected", IsHidden: hidden, IsLocked: !hidden) }
        };
        Blocked(new DrawingDocument(document.Entities, document.Dimensions, document.Constraints, metadata));
    }

    [Fact]
    public void ReadOnlyAndNonRadialDocumentsHaveNoDescriptors()
    {
        var document = Pattern();
        Blocked(new DrawingDocument(document.Entities, document.Dimensions, document.Constraints,
            document.Metadata with { Mode = DrawingDocumentMode.ReferenceOnly }));
        DrawingPatternService.TryUpdate(document, Definition(document) with { Kind = PatternKind.Rectangular },
            out document, out var reason).Should().BeTrue(reason);
        Blocked(document);
        CanvasDocumentDto.Empty.RadialCenterPreviews.Should().BeEmpty();
        Blocked(new DrawingDocument([]));
    }

    [Fact]
    public void AnotherPatternUsingTheExposedCenterAsSourceDisablesPreview()
    {
        var document = Pattern(true);
        DrawingPatternService.TryCreate(document, new PatternDefinition
        {
            Id = "dependent", SourceEntityIds = [Definition(document).CenterPointId!], XPitch = 20
        }, out document, out var reason).Should().BeTrue(reason);
        DrawingPatternService.TryValidate(document, out reason).Should().BeTrue(reason);
        Blocked(document);
    }

    [Fact]
    public void SharedCenterDisablesBothRadialPreviews()
    {
        var document = Pattern(true);
        DrawingPatternService.TryCreate(document, Definition(document) with { Id = "shared", Count = 3, Members = [] },
            out document, out var reason).Should().BeTrue(reason);
        DrawingPatternService.TryValidate(document, out reason).Should().BeTrue(reason);
        Blocked(document);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnotherPatternDirectionReferencingMovedGeometryDisablesTargetPreview(bool uAxis)
    {
        var document = Pattern(line: true);
        var other = Definition(document) with
        {
            Id = "dependent", Count = 3, Members = [],
            UDirectionReference = uAxis ? Member(document) : null,
            VDirectionReference = uAxis ? null : Member(document)
        };
        // Inactive radial direction settings are persisted but must not be silently ignored by the safety gate.
        DrawingPatternService.TryCreate(document, other, out document, out var reason).Should().BeTrue(reason);
        DrawingPatternService.TryValidate(document, out reason).Should().BeTrue(reason);
        CanvasDocumentDto.FromDocument(document).RadialCenterPreviews.Should().NotContain(p => p.Id == "radial");
    }

    [Fact]
    public void IndependentPatternRemainsEligibleWhenAnotherHasMemberIntent()
    {
        var document = Pattern();
        DrawingPatternService.TryCreate(document, Definition(document) with { Id = "independent", Count = 3, Members = [] },
            out document, out var reason).Should().BeTrue(reason);
        document = new DrawingDocument(document.Entities, document.Dimensions,
            [new("member-fix", SketchConstraintKind.Fix, [Member(document)])], document.Metadata);
        Preview(document).Id.Should().Be("independent");
    }

    [Theory]
    [InlineData("count")]
    [InlineData("members-null")]
    [InlineData("member-null")]
    [InlineData("source-null")]
    [InlineData("member-missing")]
    [InlineData("center-missing")]
    [InlineData("stale-center")]
    [InlineData("angle")]
    [InlineData("pattern-null")]
    [InlineData("patterns-null")]
    public void MalformedPatternStateFailsClosed(string corruption)
    {
        var document = Pattern(true);
        var pattern = Definition(document);
        var broken = corruption switch
        {
            "count" => pattern with { Count = 1 },
            "members-null" => pattern with { Members = null! },
            "member-null" => pattern with { Members = new PatternMember[] { null! } },
            "source-null" => pattern with { SourceEntityIds = null! },
            "member-missing" => pattern with { Members = pattern.Members.Skip(1).ToArray() },
            "center-missing" => pattern with { CenterPointId = "missing" },
            "stale-center" => pattern with { Center = new(1, 2) },
            "angle" => pattern with { StepAngleDegrees = double.NaN },
            _ => pattern
        };
        var metadata = document.Metadata with
        {
            Patterns = corruption == "patterns-null" ? null! : corruption == "pattern-null" ? new PatternDefinition[] { null! } : [broken]
        };
        Blocked(new DrawingDocument(document.Entities, document.Dimensions, document.Constraints, metadata));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("seed:bogus")]
    [InlineData("guide|segment|99")]
    [InlineData("seed|segment|-1")]
    public void UnresolvableIntentFailsClosedEvenWhenSuppressed(string key)
    {
        var document = Pattern();
        Blocked(new DrawingDocument(document.Entities, document.Dimensions,
            [new("unknown", SketchConstraintKind.Fix, [key], SketchConstraintState.Suppressed)], document.Metadata));
    }

    [Fact]
    public void MissingOrIndependentlyMovedMemberFailsClosed()
    {
        var document = Pattern();
        var id = Member(document);
        Blocked(new DrawingDocument(document.Entities.Where(e => e.Id.Value != id), document.Dimensions, document.Constraints, document.Metadata));
        Blocked(new DrawingDocument(document.Entities.Select(e => e.Id.Value == id ? e.Transform(Transform2.Translation(1, 1)) : e),
            document.Dimensions, document.Constraints, document.Metadata));
    }
}

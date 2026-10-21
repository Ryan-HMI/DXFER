using System.Text.Json.Nodes;
using DXFER.CadIO;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.IO;
using DXFER.Core.Operations;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Operations;

public sealed class DrawingPatternDirectionTests
{
    private static PatternDefinition Definition() => new()
    {
        Id = "grid", SourceEntityIds = new[] { "seed" }, Rows = 3, Columns = 3,
        XPitch = 10, YPitch = 5, UDirectionReference = "u", VDirectionReference = "v|segment|1"
    };

    private static DrawingDocument Seed() => new(new DrawingEntity[]
    {
        new LineEntity(EntityId.Create("seed"), new Point2(1, 2), new Point2(3, 4)),
        new LineEntity(EntityId.Create("u"), new Point2(10, 10), new Point2(10, 14)),
        new PolylineEntity(EntityId.Create("v"), new[] { new Point2(-5, -5), new Point2(0, 0), new Point2(3, 4) }),
        new CircleEntity(EntityId.Create("curve"), new Point2(-10, 0), 2)
    });

    private static DrawingDocument Create(PatternDefinition? definition = null, DrawingDocument? seed = null)
    {
        DrawingPatternService.TryCreate(seed ?? Seed(), definition ?? Definition(), out var result, out var reason)
            .Should().BeTrue(reason);
        return result;
    }

    private static DrawingDocument Replace(DrawingDocument document, DrawingEntity replacement) =>
        new(document.Entities.Select(e => e.Id == replacement.Id ? replacement : e), document.Dimensions,
            document.Constraints, document.Metadata);

    private static LineEntity Member(DrawingDocument document, int row, int column)
    {
        var id = document.Metadata.Patterns.Single().Members.Single(m => m.Row == row && m.Column == column).EntityId;
        return document.Entities.OfType<LineEntity>().Single(e => e.Id.Value == id);
    }

    private static void AssertPoint(Point2 point, double x, double y)
    {
        point.X.Should().BeApproximately(x, 1e-9);
        point.Y.Should().BeApproximately(y, 1e-9);
    }

    [Theory]
    [InlineData(10, 5, 0.5)]
    [InlineData(-10, 5, 0.25)]
    [InlineData(10, -5, 0)]
    [InlineData(-10, -5, 0.75)]
    [InlineData(10, 5, 1)]
    public void SkewDirectionsSignedPitchAndStaggerTranslateWithoutRotatingSeed(double xPitch, double yPitch, double fraction)
    {
        var definition = Definition() with
        {
            Kind = PatternKind.Staggered, XPitch = xPitch, YPitch = yPitch,
            StaggerFraction = fraction, AngleDegrees = 37
        };
        var seed = Seed();
        DrawingPatternService.TryGetDirections(seed, definition, out var u, out var v, out var reason).Should().BeTrue(reason);
        u.Should().Be(new Point2(0, 1));
        AssertPoint(v, 0.6, 0.8);
        var document = Create(definition, seed);
        foreach (var member in document.Metadata.Patterns.Single().Members)
        {
            var line = Member(document, member.Row, member.Column);
            var du = (member.Column + (member.Row % 2 == 1 ? fraction : 0)) * xPitch;
            var dv = member.Row * yPitch;
            AssertPoint(line.Start, 1 + dv * 0.6, 2 + du + dv * 0.8);
            AssertPoint(line.End, 3 + dv * 0.6, 4 + du + dv * 0.8);
        }
        document.Entities.Single(e => e.Id.Value == "seed").Should().BeSameAs(seed.Entities[0]);
        DrawingPatternService.TryValidate(document, out reason).Should().BeTrue(reason);
    }

    [Fact]
    public void MissingAxisReferencesUseAngleOrPerpendicularToResolvedU()
    {
        DrawingPatternService.TryGetDirections(Seed(), Definition() with { VDirectionReference = null },
            out var u, out var v, out var reason).Should().BeTrue(reason);
        u.Should().Be(new Point2(0, 1));
        v.Should().Be(new Point2(-1, 0));
        DrawingPatternService.TryGetDirections(Seed(), Definition() with { UDirectionReference = null, AngleDegrees = 0 },
            out u, out v, out reason).Should().BeTrue(reason);
        u.Should().Be(new Point2(1, 0));
        AssertPoint(v, 0.6, 0.8);
        var noReferences = Definition() with { UDirectionReference = null, VDirectionReference = null, AngleDegrees = 90 };
        DrawingPatternService.TryGetDirections(Seed(), noReferences, out u, out v, out reason).Should().BeTrue(reason);
        AssertPoint(u, 0, 1);
        AssertPoint(v, -1, 0);
    }

    [Theory]
    [InlineData("u")]
    [InlineData("v")]
    public void DirectionSourceEditsRegenerateStableMembersAndRejectStaleNativeGeometry(string axis)
    {
        var document = Create();
        var before = DxferDocumentSerializer.Write(document);
        DrawingEntity replacement = axis == "u"
            ? new LineEntity(EntityId.Create("u"), new Point2(10, 10), new Point2(10, 6))
            : new PolylineEntity(EntityId.Create("v"), new[] { new Point2(-5, -5), new Point2(0, 0), new Point2(-3, 4) });
        var edited = Replace(document, replacement);
        DrawingPatternService.TryValidate(edited, out _).Should().BeFalse();
        DrawingPatternService.TryReconcile(document, edited, out var result, out var reason).Should().BeTrue(reason);
        result.Metadata.Patterns.Single().Members.Should().Equal(document.Metadata.Patterns.Single().Members);
        AssertPoint(Member(result, 1, 1).Start, axis == "u" ? 4 : -2, axis == "u" ? -4 : 16);
        DrawingPatternService.TryValidate(result, out reason).Should().BeTrue(reason);
        DrawingPatternService.TryReconcile(document, result, out var again, out reason).Should().BeTrue(reason);
        again.Should().BeSameAs(result);
        DxferDocumentSerializer.Write(document).Should().Be(before);
    }

    [Theory]
    [InlineData("u", false)]
    [InlineData("v", false)]
    [InlineData("u", true)]
    [InlineData("v", true)]
    public void DirectionDeletionOrDegenerationRefusesAtomically(string id, bool degenerate)
    {
        var document = Create();
        var candidate = degenerate
            ? Replace(document, id == "u"
                ? new LineEntity(EntityId.Create(id), default, default)
                : new PolylineEntity(EntityId.Create(id), new[] { new Point2(1, 1), default, default }))
            : new DrawingDocument(document.Entities.Where(e => e.Id.Value != id), document.Dimensions, document.Constraints, document.Metadata);
        DrawingPatternService.TryReconcile(document, candidate, out var result, out var reason).Should().BeFalse();
        result.Should().BeSameAs(document);
        reason.Should().Contain("direction");
    }

    [Theory]
    [InlineData("")]
    [InlineData("missing")]
    [InlineData("u:start")]
    [InlineData("u|segment|0")]
    [InlineData("v")]
    [InlineData("v|segment|-1")]
    [InlineData("v|segment|99")]
    [InlineData("v|segment|1:end")]
    [InlineData("curve")]
    [InlineData("curve|segment|0")]
    public void InvalidDirectionReferencesRefuseCreationAndUpdateAtomically(string key)
    {
        foreach (var uAxis in new[] { true, false })
        {
            var invalid = uAxis ? Definition() with { UDirectionReference = key } : Definition() with { VDirectionReference = key };
            var seed = Seed();
            DrawingPatternService.TryCreate(seed, invalid, out var result, out var reason).Should().BeFalse();
            result.Should().BeSameAs(seed);
            reason.Should().Contain("direction");
            var document = Create();
            DrawingPatternService.TryUpdate(document, invalid, out result, out reason).Should().BeFalse();
            result.Should().BeSameAs(document);
        }
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonfiniteStaggerRefusesAtomically(double fraction)
    {
        var document = Create();
        DrawingPatternService.TryUpdate(document, Definition() with { StaggerFraction = fraction }, out var result, out var reason)
            .Should().BeFalse();
        result.Should().BeSameAs(document);
        reason.Should().Contain("finite");
    }

    [Theory]
    [InlineData(-0.01, PatternKind.Rectangular)]
    [InlineData(1.01, PatternKind.Rectangular)]
    [InlineData(-0.01, PatternKind.Staggered)]
    [InlineData(1.01, PatternKind.Staggered)]
    [InlineData(double.MaxValue, PatternKind.Staggered)]
    public void OutOfRangeLinearStaggerRefusesAtomically(double fraction, PatternKind kind)
    {
        var document = Create();
        DrawingPatternService.TryUpdate(document, Definition() with { Kind = kind, StaggerFraction = fraction }, out var result, out var reason)
            .Should().BeFalse();
        result.Should().BeSameAs(document);
        reason.Should().Contain("between 0 and 1");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1e-8)]
    [InlineData(1e-7)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void DegenerateAndNonfiniteDirectionGeometryRefuses(double length)
    {
        var seed = Replace(Seed(), new LineEntity(EntityId.Create("u"), default, new Point2(0, length)));
        DrawingPatternService.TryCreate(seed, Definition(), out var result, out var reason).Should().BeFalse();
        result.Should().BeSameAs(seed);
        reason.Should().Contain("degenerate");
    }

    [Fact]
    public void UnchangedDirectionGeometryStillRejectsNewConflictingIntent()
    {
        var document = Create();
        var constraint = new SketchConstraint("horizontal-u", SketchConstraintKind.Horizontal, new[] { "u" });
        var candidate = new DrawingDocument(document.Entities, document.Dimensions, new[] { constraint }, document.Metadata);
        DrawingPatternService.TryReconcile(document, candidate, out var result, out var reason).Should().BeFalse();
        result.Should().BeSameAs(document);
        reason.Should().Contain("horizontal-u");
    }

    [Fact]
    public void ZeroStaggerAllowsRowsOnlyWithoutAnUnusedUPitch()
    {
        var document = Create(Definition() with { Kind = PatternKind.Staggered, Columns = 1, XPitch = 0, StaggerFraction = 0 });
        AssertPoint(Member(document, 1, 0).Start, 4, 6);
        DrawingPatternService.TryValidate(document, out var reason).Should().BeTrue(reason);
    }

    [Theory]
    [InlineData(2, 2, false)]
    [InlineData(1, 2, true)]
    [InlineData(2, 1, true)]
    public void CollinearDirectionsRefuseOnlyWhenBothAxesRepeat(int rows, int columns, bool accepted)
    {
        var definition = Definition() with { Rows = rows, Columns = columns, VDirectionReference = "u" };
        DrawingPatternService.TryCreate(Seed(), definition, out _, out var reason).Should().Be(accepted, reason);
        if (!accepted) reason.Should().Contain("collinear");
    }

    [Theory]
    [InlineData(0.00000001, false)]
    [InlineData(0.0000002, true)]
    public void NearlyCollinearAxesUseNormalizedTolerance(double x, bool accepted)
    {
        var seed = Replace(Seed(), new PolylineEntity(EntityId.Create("v"), new[] { new Point2(-1, 0), default, new Point2(x, 1) }));
        DrawingPatternService.TryGetDirections(seed, Definition(), out _, out _, out _).Should().Be(accepted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectionsCannotReferenceAnyGeneratedPatternMember(bool samePattern)
    {
        var document = Create();
        var key = Member(document, 0, 1).Id.Value;
        foreach (var uAxis in new[] { true, false })
        {
            var definition = Definition() with { Id = samePattern ? "grid" : "another" };
            definition = uAxis ? definition with { UDirectionReference = key } : definition with { VDirectionReference = key };
            DrawingPatternService.TryUpsert(document, definition, out var result, out var reason).Should().BeFalse();
            result.Should().BeSameAs(document);
            reason.Should().Contain("generated pattern member");
        }
    }

    [Theory]
    [InlineData("fixed-direction")]
    [InlineData("fixed-member")]
    [InlineData("locked-direction")]
    [InlineData("hidden-member")]
    [InlineData("driving-member")]
    public void DirectionRegenerationPreservesIntentAndLayerProtections(string protection)
    {
        var document = Create();
        var memberId = Member(document, 0, 1).Id.Value;
        var constraints = protection.StartsWith("fixed", StringComparison.Ordinal)
            ? new[] { new SketchConstraint("fix", SketchConstraintKind.Fix, new[] { protection == "fixed-direction" ? "u" : memberId }) }
            : Array.Empty<SketchConstraint>();
        var dimensions = protection == "driving-member"
            ? new[] { new SketchDimension("distance", SketchDimensionKind.HorizontalDistance, new[] { "seed:start", memberId + ":start" }, 0, isDriving: true) }
            : Array.Empty<SketchDimension>();
        var metadata = document.Metadata;
        if (protection is "locked-direction" or "hidden-member")
        {
            var id = protection == "locked-direction" ? "u" : memberId;
            metadata = metadata with
            {
                EntityStyles = new Dictionary<string, DxfEntityStyle>(metadata.EntityStyles) { [id] = new("protected") },
                Layers = new Dictionary<string, DxfEntityStyle> { ["protected"] = new("protected", IsLocked: protection == "locked-direction", IsHidden: protection == "hidden-member") }
            };
        }
        document = new DrawingDocument(document.Entities, dimensions, constraints, metadata);
        var candidate = Replace(document, new LineEntity(EntityId.Create("u"), default, new Point2(-4, 0)));
        DrawingPatternService.TryReconcile(document, candidate, out var result, out var reason).Should().BeFalse();
        result.Should().BeSameAs(document);
        reason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void NativeAndEmbeddedDxfRoundTripsRetainReferencesFlipsStaggerAndSubsequentEdits()
    {
        var document = Create(Definition() with { Kind = PatternKind.Staggered, XPitch = -10, YPitch = -5, StaggerFraction = 0.25 });
        var snapshot = DxferDocumentSerializer.Write(document);
        var reopened = DxferDocumentSerializer.Read(snapshot);
        DxferDocumentSerializer.Write(reopened).Should().Be(snapshot);
        var dxf = DxfDocumentWriter.Write(document, new(IncludeNativeDocument: true));
        var embedded = DxfDocumentReader.Read(dxf, out var restored);
        restored.Should().BeTrue();
        DxferDocumentSerializer.Write(embedded).Should().Be(snapshot);
        foreach (var saved in new[] { reopened, embedded })
        {
            var definition = saved.Metadata.Patterns.Single();
            definition.UDirectionReference.Should().Be("u");
            definition.VDirectionReference.Should().Be("v|segment|1");
            definition.StaggerFraction.Should().Be(0.25);
            var edited = Replace(saved, new LineEntity(EntityId.Create("u"), default, new Point2(0, -1)));
            DrawingPatternService.TryReconcile(saved, edited, out var result, out var reason).Should().BeTrue(reason);
            AssertPoint(Member(result, 1, 1).Start, -2, 10.5);
            DrawingPatternService.TryValidate(result, out reason).Should().BeTrue(reason);
        }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("curve")]
    [InlineData("v|segment|99")]
    [InlineData("member")]
    public void NativeReadRejectsForgedDirectionReferences(string key)
    {
        var document = Create();
        var json = JsonNode.Parse(DxferDocumentSerializer.Write(document))!;
        json["metadata"]!["patterns"]![0]!["uDirectionReference"] = key == "member" ? Member(document, 0, 1).Id.Value : key;
        Action read = () => DxferDocumentSerializer.Read(json.ToJsonString());
        read.Should().Throw<FormatException>();
    }

    [Fact]
    public void LegacyStaggerDefaultsToHalfPitchAndKeepsExistingGeometry()
    {
        var document = Create(Definition() with
        {
            Kind = PatternKind.Staggered, UDirectionReference = null, VDirectionReference = null, AngleDegrees = 30
        });
        var json = JsonNode.Parse(DxferDocumentSerializer.Write(document))!;
        var pattern = json["metadata"]!["patterns"]![0]!.AsObject();
        pattern.Remove("uDirectionReference");
        pattern.Remove("vDirectionReference");
        pattern.Remove("staggerFraction");
        var legacy = DxferDocumentSerializer.Read(json.ToJsonString());
        legacy.Metadata.Patterns.Single().StaggerFraction.Should().Be(0.5);
        legacy.Entities.Should().BeEquivalentTo(document.Entities, options => options.WithStrictOrdering());
        DrawingPatternService.TryValidate(legacy, out var reason).Should().BeTrue(reason);
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(-0.25)]
    [InlineData(1.25)]
    public void RadialPatternsIgnoreInactiveDirectionReferencesAndStaggerRange(double fraction)
    {
        var plain = Definition() with { Kind = PatternKind.Radial, Center = default, UDirectionReference = null, VDirectionReference = null };
        var stale = plain with { UDirectionReference = "missing", VDirectionReference = "curve", StaggerFraction = fraction };
        var expected = Create(plain);
        var actual = Create(stale);
        actual.Entities.Should().BeEquivalentTo(expected.Entities, options => options.WithStrictOrdering());
        var reopened = DxferDocumentSerializer.Read(DxferDocumentSerializer.Write(actual));
        DrawingPatternService.TryValidate(reopened, out var reason).Should().BeTrue(reason);
    }
}

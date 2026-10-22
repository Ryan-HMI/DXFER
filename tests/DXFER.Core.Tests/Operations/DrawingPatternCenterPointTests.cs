using System.Text.Json.Nodes;
using DXFER.CadIO;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.IO;
using DXFER.Core.Operations;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Operations;

public sealed class DrawingPatternCenterPointTests
{
    private static DrawingDocument Pattern(bool exposed = true)
    {
        var seed = new DrawingDocument([new CircleEntity(EntityId.Create("seed"), new(4, 2), 1)]);
        DrawingPatternService.TryCreate(seed, new PatternDefinition
        {
            Id = "radial", Kind = PatternKind.Radial, SourceEntityIds = ["seed"], Count = 4, Center = new(0, 0)
        }, out var document, out var reason).Should().BeTrue(reason);
        if (exposed)
            DrawingPatternService.TryExposeCenter(document, "radial", out document, out reason).Should().BeTrue(reason);
        return document;
    }

    private static PatternDefinition Definition(DrawingDocument document) => document.Metadata.Patterns.First(p => p.Id == "radial");
    private static PointEntity Center(DrawingDocument document) =>
        document.Entities.OfType<PointEntity>().Single(p => p.Id.Value == Definition(document).CenterPointId);

    private static DrawingDocument Replace(DrawingDocument document, DrawingEntity entity) =>
        new(document.Entities.Select(e => e.Id == entity.Id ? entity : e), document.Dimensions, document.Constraints, document.Metadata);

    private static DrawingDocument Move(DrawingDocument document, Point2 center)
    {
        DrawingPatternService.TryUpdate(document, Definition(document) with { Center = center }, out var result, out var reason)
            .Should().BeTrue(reason);
        return result;
    }

    [Fact]
    public void ExposeIsStableIdempotentAndDoesNotChangeGeometry()
    {
        var original = Pattern(false);
        var snapshot = DxferDocumentSerializer.Write(original);
        DrawingPatternService.TryExposeCenter(original, "radial", out var exposed, out var reason).Should().BeTrue(reason);
        var point = Center(exposed);
        point.Location.Should().Be(Definition(original).Center);
        point.IsConstruction.Should().BeTrue();
        exposed.Entities.Take(original.Entities.Count).Should().Equal(original.Entities);
        Definition(exposed).Members.Should().Equal(Definition(original).Members);
        DrawingPatternService.TryExposeCenter(exposed, "radial", out var again, out reason).Should().BeTrue(reason);
        again.Should().BeSameAs(exposed);
        DrawingPatternService.TryExposeCenter(original, "radial", out var replay, out reason).Should().BeTrue(reason);
        Center(replay).Id.Should().Be(point.Id);
        DxferDocumentSerializer.Write(original).Should().Be(snapshot);
    }

    [Fact]
    public void ExposeNeverReusesAnUnrelatedCollidingPointId()
    {
        var id = Center(Pattern()).Id;
        var original = Pattern(false);
        var unrelated = new PointEntity(id, new(30, 40), true);
        original = new DrawingDocument(original.Entities.Append(unrelated), original.Dimensions, original.Constraints, original.Metadata);
        DrawingPatternService.TryExposeCenter(original, "radial", out var result, out var reason).Should().BeTrue(reason);
        Center(result).Id.Should().NotBe(id);
        result.Entities.Single(e => e.Id == id).Should().BeSameAs(unrelated);
    }

    [Fact]
    public void ExposeUsesSeedLayerAndBypassesActiveLayerAssignment()
    {
        var original = Pattern(false);
        var metadata = original.Metadata with
        {
            ActiveLayerName = "locked",
            EntityStyles = new Dictionary<string, DxfEntityStyle>(original.Metadata.EntityStyles) { ["seed"] = new("seed-layer") },
            Layers = new Dictionary<string, DxfEntityStyle> { ["seed-layer"] = new("seed-layer"), ["locked"] = new("locked", IsLocked: true) }
        };
        original = new DrawingDocument(original.Entities, original.Dimensions, original.Constraints, metadata);
        DrawingPatternService.TryExposeCenter(original, "radial", out var exposed, out var reason).Should().BeTrue(reason);
        DrawingLayerService.LayerName(exposed, Center(exposed).Id.Value).Should().Be("seed-layer");
        DrawingLayerService.AssignNewEntities(original, exposed).Should().BeSameAs(exposed);
    }

    [Theory]
    [InlineData("locked-seed")]
    [InlineData("hidden-member")]
    [InlineData("locked-center")]
    [InlineData("reference")]
    public void ExposeRefusesProtectedGeometry(string protection)
    {
        var document = Pattern(protection == "locked-center");
        var id = protection == "locked-center" ? Center(document).Id.Value
            : protection == "hidden-member" ? Definition(document).Members[0].EntityId : "seed";
        var metadata = document.Metadata with
        {
            Mode = protection == "reference" ? DrawingDocumentMode.ReferenceOnly : DrawingDocumentMode.Editable,
            EntityStyles = new Dictionary<string, DxfEntityStyle>(document.Metadata.EntityStyles) { [id] = new("protected") },
            Layers = new Dictionary<string, DxfEntityStyle> { ["protected"] = new("protected", IsLocked: protection.StartsWith("locked", StringComparison.Ordinal), IsHidden: protection == "hidden-member") }
        };
        document = new DrawingDocument(document.Entities, document.Dimensions, document.Constraints, metadata);
        DrawingPatternService.TryExposeCenter(document, "radial", out var result, out _).Should().BeFalse();
        result.Should().BeSameAs(document);
    }

    [Fact]
    public void ExposeRejectsLinearAndMissingPatterns()
    {
        var document = Pattern(false);
        DrawingPatternService.TryUpdate(document, Definition(document) with { Kind = PatternKind.Rectangular }, out document, out var reason)
            .Should().BeTrue(reason);
        foreach (var id in new[] { "radial", "missing" })
        {
            DrawingPatternService.TryExposeCenter(document, id, out var result, out _).Should().BeFalse();
            result.Should().BeSameAs(document);
        }
    }

    [Fact]
    public void ExplicitCenterEditsMovePointAndRegenerateMembersAtomically()
    {
        var original = Pattern();
        var snapshot = DxferDocumentSerializer.Write(original);
        var moved = Move(original, new(1, 1));
        Center(moved).Location.Should().Be(new Point2(1, 1));
        Center(moved).Id.Should().Be(Center(original).Id);
        Definition(moved).Members.Should().Equal(Definition(original).Members);
        var first = moved.Entities.OfType<CircleEntity>().Single(e => e.Id.Value == Definition(moved).Members[0].EntityId);
        first.Center.X.Should().BeApproximately(0, 1e-9);
        first.Center.Y.Should().BeApproximately(4, 1e-9);
        DxferDocumentSerializer.Write(original).Should().Be(snapshot);
        DrawingPatternService.TryValidate(moved, out var reason).Should().BeTrue(reason);
    }

    [Theory]
    [InlineData("fix-point")]
    [InlineData("fix-center")]
    [InlineData("fix-member")]
    [InlineData("locked")]
    [InlineData("coincident")]
    [InlineData("driving")]
    public void ExplicitCenterEditsPreserveFixedLayersConstraintsAndDrivingDimensions(string protection)
    {
        var document = Protected(Pattern(), protection);
        DrawingPatternService.TryUpdate(document, Definition(document) with { Center = new(1, 1) }, out var result, out var reason)
            .Should().BeFalse();
        result.Should().BeSameAs(document);
        reason.Should().NotBeNullOrWhiteSpace();
    }

    private static DrawingDocument Protected(DrawingDocument document, string protection)
    {
        var id = Center(document).Id.Value;
        var constraints = protection switch
        {
            "fix-point" => new[] { new SketchConstraint("fix", SketchConstraintKind.Fix, [id]) },
            "fix-center" => new[] { new SketchConstraint("fix", SketchConstraintKind.Fix, [id + ":center"]) },
            "fix-member" => new[] { new SketchConstraint("fix", SketchConstraintKind.Fix, [Definition(document).Members[0].EntityId]) },
            "coincident" => new[] { new SketchConstraint("coincident", SketchConstraintKind.Coincident, [id + ":center", SketchReference.OriginKey]) },
            _ => Array.Empty<SketchConstraint>()
        };
        var dimensions = protection == "driving"
            ? new[] { new SketchDimension("distance", SketchDimensionKind.HorizontalDistance, [id + ":center", "seed:center"], 4, isDriving: true) }
            : Array.Empty<SketchDimension>();
        var metadata = protection != "locked" ? document.Metadata : document.Metadata with
        {
            EntityStyles = new Dictionary<string, DxfEntityStyle>(document.Metadata.EntityStyles) { [id] = new("locked") },
            Layers = new Dictionary<string, DxfEntityStyle> { ["locked"] = new("locked", IsLocked: true) }
        };
        return new DrawingDocument(document.Entities, dimensions, constraints, metadata);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReconcileAcceptsPointMovementAndAlreadySolvedCenterMetadata(bool alreadySolved)
    {
        var document = Pattern();
        var candidate = alreadySolved ? Move(document, new(1, 1)) : Replace(document, Center(document) with { Location = new(1, 1) });
        DrawingPatternService.TryReconcile(document, candidate, out var result, out var reason).Should().BeTrue(reason);
        Definition(result).Center.Should().Be(new Point2(1, 1));
        DrawingPatternService.TryValidate(result, out reason).Should().BeTrue(reason);
        if (alreadySolved) result.Should().BeSameAs(candidate);
    }

    [Fact]
    public void ReconcileAllowsCenterSyncAlongsideAnUnchangedLegacyPattern()
    {
        var document = Pattern();
        DrawingPatternService.TryCreate(document, new PatternDefinition
        {
            Id = "linear", SourceEntityIds = ["seed"], Kind = PatternKind.Rectangular, XPitch = 20
        }, out document, out var reason).Should().BeTrue(reason);
        var candidate = Move(document, new(1, 1));
        // TryUpdate appends its definition; ordinary reconciliation keeps metadata ordering fixed.
        candidate = new DrawingDocument(candidate.Entities, candidate.Dimensions, candidate.Constraints, candidate.Metadata with
        {
            Patterns = document.Metadata.Patterns.Select(p => candidate.Metadata.Patterns.Single(c => c.Id == p.Id)).ToArray()
        });
        DrawingPatternService.TryReconcile(document, candidate, out _, out reason).Should().BeTrue(reason);
    }

    [Theory]
    [InlineData("center-only")]
    [InlineData("count")]
    [InlineData("unlink")]
    public void ReconcileRejectsArbitraryMetadataEdits(string edit)
    {
        var document = Pattern();
        var pattern = Definition(document);
        var changed = edit switch
        {
            "center-only" => pattern with { Center = new(1, 1) },
            "count" => pattern with { Count = 8 },
            _ => pattern with { CenterPointId = null }
        };
        var candidate = new DrawingDocument(document.Entities, document.Dimensions, document.Constraints,
            document.Metadata with { Patterns = new[] { changed } });
        DrawingPatternService.TryReconcile(document, candidate, out var result, out _).Should().BeFalse();
        result.Should().BeSameAs(document);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("wrong-kind")]
    [InlineData("not-construction")]
    [InlineData("nonfinite")]
    public void MissingOrWrongPointRefusesReconciliation(string edit)
    {
        var document = Pattern();
        var point = Center(document);
        var candidate = edit switch
        {
            "missing" => new DrawingDocument(document.Entities.Where(e => e.Id != point.Id), document.Dimensions, document.Constraints, document.Metadata),
            "wrong-kind" => Replace(document, new CircleEntity(point.Id, point.Location, 1, true)),
            "not-construction" => Replace(document, point with { IsConstruction = false }),
            _ => Replace(document, point with { Location = new(double.NaN, 0) })
        };
        DrawingPatternService.TryReconcile(document, candidate, out var result, out _).Should().BeFalse();
        result.Should().BeSameAs(document);
    }

    [Fact]
    public void PatternUpdatesCannotSilentlyDetachOrReassignAnExposedCenter()
    {
        var document = Pattern();
        foreach (var id in new string?[] { null, "seed" })
        {
            DrawingPatternService.TryUpdate(document, Definition(document) with { CenterPointId = id }, out var result, out _).Should().BeFalse();
            result.Should().BeSameAs(document);
        }
    }

    [Fact]
    public void NativeAndEmbeddedDxfPreservePointGenericIntentAndUnlinkRetainsPoint()
    {
        var document = Protected(Pattern(), "coincident");
        var snapshot = DxferDocumentSerializer.Write(document);
        var native = DxferDocumentSerializer.Read(snapshot);
        var embedded = DxfDocumentReader.Read(DxfDocumentWriter.Write(document, new(IncludeNativeDocument: true)), out var restored);
        restored.Should().BeTrue();
        foreach (var reopened in new[] { native, embedded })
        {
            DxferDocumentSerializer.Write(reopened).Should().Be(snapshot);
            DrawingPatternService.TryExposeCenter(reopened, "radial", out var same, out var reason).Should().BeTrue(reason);
            same.Should().BeSameAs(reopened);
            DrawingPatternService.TryDissolve(reopened, "radial", out var unlinked, out reason).Should().BeTrue(reason);
            unlinked.Entities.Should().Equal(reopened.Entities);
            unlinked.Constraints.Should().Equal(reopened.Constraints);
            unlinked.Entities.Should().Contain(Center(reopened));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnlinkCannotDetachAProtectedCenterPoint(bool hidden)
    {
        var document = Pattern();
        var metadata = document.Metadata with
        {
            EntityStyles = new Dictionary<string, DxfEntityStyle>(document.Metadata.EntityStyles)
            {
                [Center(document).Id.Value] = new("protected")
            },
            Layers = new Dictionary<string, DxfEntityStyle>
            {
                ["protected"] = new("protected", IsHidden: hidden, IsLocked: !hidden)
            }
        };
        document = new DrawingDocument(document.Entities, document.Dimensions, document.Constraints, metadata);
        DrawingPatternService.TryDissolve(document, "radial", out var result, out var reason).Should().BeFalse();
        result.Should().BeSameAs(document);
        reason.Should().Contain("unlock");
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("seed")]
    [InlineData("")]
    public void NativeReadRejectsInvalidCenterReferences(string id)
    {
        var json = JsonNode.Parse(DxferDocumentSerializer.Write(Pattern()))!;
        json["metadata"]!["patterns"]![0]!["centerPointId"] = id;
        Action read = () => DxferDocumentSerializer.Read(json.ToJsonString());
        read.Should().Throw<FormatException>();
    }

    [Fact]
    public void NativeWriteRejectsUnsynchronizedCenterAndLegacyNullStaysUnchanged()
    {
        var original = Pattern();
        var changed = Replace(original, Center(original) with { Location = new(1, 1) });
        Action write = () => DxferDocumentSerializer.Write(changed);
        write.Should().Throw<FormatException>();
        var legacy = Pattern(false);
        var snapshot = DxferDocumentSerializer.Write(legacy);
        snapshot.Should().NotContain("centerPointId");
        DxferDocumentSerializer.Write(DxferDocumentSerializer.Read(snapshot)).Should().Be(snapshot);
        Definition(Move(legacy, new(1, 1))).CenterPointId.Should().BeNull();
    }

    [Fact]
    public void GeneratedConstructionPointsCannotBecomePatternCenters()
    {
        var original = Pattern(false);
        var seedPoint = new PointEntity(EntityId.Create("point-seed"), default, true);
        original = new DrawingDocument(original.Entities.Append(seedPoint), original.Dimensions, original.Constraints, original.Metadata);
        DrawingPatternService.TryCreate(original, new PatternDefinition
        {
            Id = "points", SourceEntityIds = ["point-seed"], XPitch = 10
        }, out var document, out var reason).Should().BeTrue(reason);
        var generatedId = document.Metadata.Patterns.Single(p => p.Id == "points").Members[0].EntityId;
        var definition = Definition(document) with { CenterPointId = generatedId, Center = new(10, 0) };
        DrawingPatternService.TryUpdate(document, definition, out var result, out reason).Should().BeFalse();
        result.Should().BeSameAs(document);
        reason.Should().Contain("generated");
    }

    [Fact]
    public void AdapterReadsSolvedPointAsCenterWithoutRepairingReturnedGeometry()
    {
        var document = Pattern();
        var solved = Move(document, new(1, 1));
        var runtime = new SnapshotRuntime(solved);
        var result = new PlaneGcsSketchSolverAdapter(runtime).Solve(new(document, [], []));
        result.Status.Should().Be(SketchSolveStatus.Solved, string.Join(" ", result.Diagnostics));
        Definition(result.Document).Center.Should().Be(Center(result.Document).Location);
        result.Document.Entities.Select(e => e.Id).Should().Equal(document.Entities.Select(e => e.Id));
        result.Document.Entities.Should().BeEquivalentTo(document.Entities.Select(e => solved.Entities.Single(s => s.Id == e.Id)),
            options => options.WithStrictOrdering());
        runtime.Request!.Document.Metadata.GetProperty("patterns")[0].GetProperty("centerPointId").GetString()
            .Should().Be(Center(document).Id.Value);
        Definition(document).Center.Should().Be(new Point2(0, 0));
    }

    [Fact]
    public void AdapterRefusesMovedCenterWithUnsolvedMembersInsteadOfRegeneratingThem()
    {
        var document = Pattern();
        var incomplete = Replace(document, Center(document) with { Location = new(1, 1) });
        var result = new PlaneGcsSketchSolverAdapter(new SnapshotRuntime(incomplete)).Solve(new(document, [], []));
        result.Status.Should().Be(SketchSolveStatus.Failed);
        result.Document.Should().BeSameAs(document);
        result.Diagnostics.Should().Contain(d => d.Contains("pattern", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("fix-point")]
    [InlineData("fix-center")]
    [InlineData("fix-member")]
    [InlineData("locked")]
    [InlineData("coincident")]
    [InlineData("driving")]
    public void AdapterRejectsSolvedCenterThatBreaksProtectedIntent(string protection)
    {
        var original = Pattern();
        var solved = Move(original, new(1, 1));
        var document = Protected(original, protection);
        var result = new PlaneGcsSketchSolverAdapter(new SnapshotRuntime(solved)).Solve(new(document, [], []));
        result.Status.Should().Be(SketchSolveStatus.Failed);
        result.Document.Should().BeSameAs(document);
    }

    private sealed class SnapshotRuntime(DrawingDocument result) : IPlaneGcsRuntime
    {
        public PlaneGcsRequest? Request { get; private set; }
        public PlaneGcsResponse Solve(PlaneGcsRequest request)
        {
            Request = request;
            var entities = request.Document.Entities.Select(wire => result.Entities.Single(e => e.Id.Value == wire.Id) switch
            {
                PointEntity point => wire with { Points = new[] { new PlaneGcsPoint(point.Location.X, point.Location.Y) } },
                CircleEntity circle => wire with { Center = new(circle.Center.X, circle.Center.Y), Radius = circle.Radius },
                _ => throw new InvalidOperationException("Unexpected fixture geometry.")
            }).ToArray();
            return new("solved", entities, [], "PlaneGCS", []);
        }
    }
}

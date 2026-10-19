using System.Text.Json;
using System.Text.Json.Nodes;
using DXFER.CadIO;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.IO;
using DXFER.Core.Operations;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Operations;

public sealed class DrawingPatternServiceTests
{
    private static readonly EntityId SourceId = EntityId.Create("source");
    private static PatternDefinition Definition(PatternKind kind = PatternKind.Rectangular) => new()
    {
        Id = "pattern-a", SourceEntityIds = new[] { "source" }, Kind = kind,
        Rows = 2, Columns = 3, XPitch = 10, YPitch = 20, Count = 4, Center = new Point2(1, 1)
    };
    private static DrawingDocument Seed() => new(new DrawingEntity[] { new CircleEntity(SourceId, new Point2(2, 1), 1) });
    private static DrawingDocument Create(DrawingDocument? document = null, PatternDefinition? definition = null)
    {
        DrawingPatternService.TryCreate(document ?? Seed(), definition ?? Definition(), out var result, out var reason).Should().BeTrue(reason);
        return result;
    }
    private static DrawingDocument Replace(DrawingDocument document, DrawingEntity replacement) =>
        new(document.Entities.Select(e => e.Id == replacement.Id ? replacement : e), document.Dimensions, document.Constraints, document.Metadata);
    private static string Geometry(DrawingEntity entity) => JsonSerializer.Serialize(entity, entity.GetType());
    private static PatternMember Member(DrawingDocument document, int row = 0, int column = 1) =>
        document.Metadata.Patterns.Single().Members.Single(m => m.Row == row && m.Column == column);
    private static DrawingEntity Entity(DrawingDocument document, string id) => document.Entities.Single(e => e.Id.Value == id);
    private static DrawingDocument WithIntent(DrawingDocument document, SketchConstraint? constraint = null, SketchDimension? dimension = null) =>
        new(document.Entities, dimension is null ? document.Dimensions : document.Dimensions.Append(dimension),
            constraint is null ? document.Constraints : document.Constraints.Append(constraint), document.Metadata);

    [Theory]
    [InlineData(PatternKind.Rectangular, 0, 1, 0, 10, 0)]
    [InlineData(PatternKind.Rectangular, 0, 1, 1, 10, 20)]
    [InlineData(PatternKind.Staggered, 0, 0, 1, 5, 20)]
    [InlineData(PatternKind.Staggered, 0, 2, 1, 25, 20)]
    [InlineData(PatternKind.Staggered, 90, 0, 1, -20, 5)]
    public void GridModesUsePitchBeforeRotation(PatternKind kind, double angle, int column, int row, double dx, double dy)
    {
        var document = Create(definition: Definition(kind) with { AngleDegrees = angle });
        document.Entities.Should().HaveCount(6);
        var circle = (CircleEntity)Entity(document, Member(document, row, column).EntityId);
        circle.Center.X.Should().BeApproximately(2 + dx, 1e-9);
        circle.Center.Y.Should().BeApproximately(1 + dy, 1e-9);
        document.Entities[0].Should().Be(Seed().Entities[0]);
    }

    [Theory]
    [InlineData(true, 90, 0, 1)]
    [InlineData(false, -30, 1.866025403784, 0.5)]
    public void RadialCountIncludesSeedAndSupportsFullCircleOrSignedStep(bool full, double step, double x, double y)
    {
        var document = Create(definition: Definition(PatternKind.Radial) with { FullCircle = full, StepAngleDegrees = step });
        document.Entities.Should().HaveCount(4);
        var first = (CircleEntity)Entity(document, Member(document).EntityId);
        first.Center.X.Should().BeApproximately(full ? 1 : x, 1e-9);
        first.Center.Y.Should().BeApproximately(full ? 2 : y, 1e-9);
        first.Radius.Should().Be(1);
    }

    public static IEnumerable<object[]> EntityKinds()
    {
        yield return new object[] { new LineEntity(SourceId, new Point2(2, 1), new Point2(4, 2)) };
        yield return new object[] { new PolylineEntity(SourceId, new[] { new Point2(2, 1), new Point2(4, 2), new Point2(3, 4) }) };
        yield return new object[] { new CircleEntity(SourceId, new Point2(2, 1), 1) };
        yield return new object[] { new ArcEntity(SourceId, new Point2(2, 1), 1, 20, 130) };
        yield return new object[] { new PolygonEntity(SourceId, new Point2(2, 1), 1, 15, 5, true) };
        yield return new object[] { new EllipseEntity(SourceId, new Point2(2, 1), new Point2(2, 1), 0.5, 10, 230) };
        yield return new object[] { SplineEntity.FromFitPoints(SourceId, new[] { new Point2(2, 1), new Point2(3, 3), new Point2(5, 2) }) };
        yield return new object[] { new PointEntity(SourceId, new Point2(2, 1)) };
    }

    [Theory]
    [MemberData(nameof(EntityKinds))]
    public void EveryDrawingEntityTransformsAndRoundTripsWithStableIdentity(DrawingEntity source)
    {
        foreach (var kind in Enum.GetValues<PatternKind>())
        {
            var document = Create(new DrawingDocument(new[] { source }), Definition(kind));
            var member = Member(document);
            var transform = kind == PatternKind.Radial ? Transform2.RotationDegreesAbout(90, new Point2(1, 1)) : Transform2.Translation(10, 0);
            var expected = source.Transform(transform) with { Id = EntityId.Create(member.EntityId) };
            Geometry(Entity(document, member.EntityId)).Should().Be(Geometry(expected));
            var reopened = DxferDocumentSerializer.Read(DxferDocumentSerializer.Write(document));
            DxferDocumentSerializer.Write(reopened).Should().Be(DxferDocumentSerializer.Write(document));
            DrawingPatternService.TryValidate(reopened, out var reason).Should().BeTrue(reason);
            var modified = source.Transform(Transform2.Translation(3, 2));
            DrawingPatternService.TryReconcile(reopened, Replace(reopened, modified), out var followed, out reason).Should().BeTrue(reason);
            Geometry(Entity(followed, member.EntityId)).Should().Be(Geometry(modified.Transform(transform) with { Id = EntityId.Create(member.EntityId) }));
        }
    }

    [Fact]
    public void PreviewCommitUpdateAndShrinkRegrowHaveStableMemberIdsAndUndoSnapshots()
    {
        var seed = Seed();
        var before = DxferDocumentSerializer.Write(seed);
        var first = Create(seed);
        var preview = Create(seed);
        first.Metadata.Patterns.Single().Members.Should().Equal(preview.Metadata.Patterns.Single().Members);
        var snapshot = DxferDocumentSerializer.Write(first);
        DrawingPatternService.TryUpdate(first, Definition() with { XPitch = 12 }, out var edited, out var reason).Should().BeTrue(reason);
        edited.Metadata.Patterns.Single().Members.Should().Equal(first.Metadata.Patterns.Single().Members);
        DrawingPatternService.TryUpdate(edited, Definition() with { Rows = 1, Columns = 2 }, out var smaller, out reason).Should().BeTrue(reason);
        smaller.Entities.Should().HaveCount(2);
        DrawingPatternService.TryUpdate(smaller, Definition(), out var regrown, out reason).Should().BeTrue(reason);
        regrown.Metadata.Patterns.Single().Members.Should().Equal(first.Metadata.Patterns.Single().Members);
        DxferDocumentSerializer.Write(seed).Should().Be(before);
        DxferDocumentSerializer.Write(first).Should().Be(snapshot);
    }

    [Fact]
    public void NoOpAndAlreadyReconciledCandidatesPreserveReferenceIdentity()
    {
        var document = Create();
        DrawingPatternService.TryReconcile(document, document, out var unchanged, out _).Should().BeTrue();
        unchanged.Should().BeSameAs(document);
        var candidate = Replace(document, document.Entities[0].Transform(Transform2.Translation(1, 2)));
        DrawingPatternService.TryReconcile(document, candidate, out var result, out var reason).Should().BeTrue(reason);
        DrawingPatternService.TryReconcile(document, result, out var again, out reason).Should().BeTrue(reason);
        again.Should().BeSameAs(result);
    }

    [Fact]
    public void SourceTopologyAndConstructionFollowWithoutChangingUnrelatedGeometry()
    {
        var source = new PolylineEntity(SourceId, new[] { new Point2(0, 0), new Point2(1, 0) });
        var unrelated = new PointEntity(EntityId.Create("unrelated"), new Point2(-4, 9));
        var document = Create(new DrawingDocument(new DrawingEntity[] { source, unrelated }));
        var changed = new PolylineEntity(SourceId, new[] { new Point2(0, 0), new Point2(2, 1), new Point2(3, 4) }, true);
        DrawingPatternService.TryReconcile(document, Replace(document, changed), out var next, out var reason).Should().BeTrue(reason);
        ((PolylineEntity)Entity(next, Member(next).EntityId)).Vertices.Should().HaveCount(3);
        Entity(next, Member(next).EntityId).IsConstruction.Should().BeTrue();
        Entity(next, "unrelated").Should().BeSameAs(unrelated);
    }

    [Fact]
    public void DependentMutationDeletionAndSourceDeletionRefuseAtomically()
    {
        var document = Create();
        var dependent = Entity(document, Member(document).EntityId);
        foreach (var candidate in new[]
        {
            Replace(document, dependent.Transform(Transform2.Translation(1, 1))),
            new DrawingDocument(document.Entities.Where(e => e.Id != dependent.Id), document.Dimensions, document.Constraints, document.Metadata),
            new DrawingDocument(document.Entities.Skip(1), document.Dimensions, document.Constraints, document.Metadata)
        })
        {
            DrawingPatternService.TryReconcile(document, candidate, out var result, out var reason).Should().BeFalse();
            result.Should().BeSameAs(document);
            reason.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void DissolveKeepsGeometryAndMakesMembersEditable()
    {
        var document = Create();
        DrawingPatternService.TryDissolve(document, "pattern-a", out var dissolved, out var reason).Should().BeTrue(reason);
        reason.Should().Contain("unlinked");
        dissolved.Entities.Should().Equal(document.Entities);
        dissolved.Metadata.Patterns.Should().BeEmpty();
        var dependent = Entity(dissolved, Member(document).EntityId);
        DrawingPatternService.TryReconcile(dissolved, Replace(dissolved, dependent.Transform(Transform2.Translation(1, 1))), out _, out reason)
            .Should().BeTrue(reason);
        document.Metadata.Patterns.Should().ContainSingle();
    }

    [Fact]
    public void NestedSourcesAndMetadataRemovalThroughOrdinaryMutationRefuse()
    {
        var document = Create();
        DrawingPatternService.TryCreate(document, Definition() with { Id = "nested", SourceEntityIds = new[] { Member(document).EntityId } }, out _, out _)
            .Should().BeFalse();
        var bypass = new DrawingDocument(document.Entities, document.Dimensions, document.Constraints, document.Metadata with { Patterns = Array.Empty<PatternDefinition>() });
        DrawingPatternService.TryReconcile(document, bypass, out var result, out var reason).Should().BeFalse();
        result.Should().BeSameAs(document);
        reason.Should().Contain("pattern");
    }

    [Theory]
    [InlineData("source")]
    [InlineData("member")]
    [InlineData("member:center")]
    public void FixedSourcesOrMembersRefuseIncompatibleSourceTranslation(string target)
    {
        var document = Create();
        var key = target.Replace("member", Member(document).EntityId);
        document = WithIntent(document, new SketchConstraint("fixed", SketchConstraintKind.Fix, new[] { key }));
        DrawingPatternService.TryReconcile(document, Replace(document, document.Entities[0].Transform(Transform2.Translation(1, 1))), out var result, out var reason)
            .Should().BeFalse();
        result.Should().BeSameAs(document);
        reason.Should().Contain("Fixed");
    }

    [Fact]
    public void FixedMemberCenterAllowsSourceRadiusChangeButWholeFixDoesNot()
    {
        var document = Create();
        var key = Member(document).EntityId;
        document = WithIntent(document, new SketchConstraint("fixed", SketchConstraintKind.Fix, new[] { key + ":center" }));
        DrawingPatternService.TryReconcile(document, Replace(document, (CircleEntity)document.Entities[0] with { Radius = 2 }), out var result, out var reason)
            .Should().BeTrue(reason);
        ((CircleEntity)Entity(result, key)).Radius.Should().Be(2);
    }

    [Fact]
    public void DrivingAndOffsetDependenciesCannotBeSilentlyBroken()
    {
        var document = Create();
        var id = Member(document).EntityId;
        document = WithIntent(document, dimension: new SketchDimension("radius", SketchDimensionKind.Radius, new[] { id }, 1, isDriving: true));
        DrawingPatternService.TryReconcile(document, Replace(document, (CircleEntity)document.Entities[0] with { Radius = 2 }), out var refused, out var reason)
            .Should().BeFalse();
        reason.Should().Contain("driving");
        refused.Should().BeSameAs(document);
        var badIntent = WithIntent(Create(), dimension: new SketchDimension("wrong", SketchDimensionKind.Radius, new[] { id }, 7, isDriving: true));
        DrawingPatternService.TryReconcile(Create(), badIntent, out _, out _).Should().BeFalse();

        var offset = new CircleEntity(EntityId.Create("offset"), ((CircleEntity)Entity(document, id)).Center, 2);
        var offsetDoc = new DrawingDocument(document.Entities.Append(offset), Array.Empty<SketchDimension>(),
            new[] { new SketchConstraint("offset", SketchConstraintKind.Offset, new[] { id, "offset" }, SketchConstraintState.Satisfied) }, document.Metadata);
        DrawingPatternService.TryReconcile(offsetDoc, Replace(offsetDoc, offsetDoc.Entities[0].Transform(Transform2.Translation(1, 0))), out _, out reason)
            .Should().BeFalse();
        reason.Should().Contain("constraint");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RemovingReferencedMembersRefusesEvenForReferenceOrSuppressedIntent(bool dimension)
    {
        var document = Create();
        var id = Member(document, 1, 2).EntityId;
        document = dimension
            ? WithIntent(document, dimension: new SketchDimension("reference", SketchDimensionKind.Radius, new[] { id }, 1, isDriving: false))
            : WithIntent(document, new SketchConstraint("fixed", SketchConstraintKind.Fix, new[] { id }, SketchConstraintState.Suppressed));
        DrawingPatternService.TryUpdate(document, Definition() with { Rows = 1, Columns = 2 }, out var result, out var reason).Should().BeFalse();
        result.Should().BeSameAs(document);
        reason.Should().Contain("dimensions or constraints");
    }

    [Fact]
    public void LockedLayersAndReferenceOnlyDocumentsRefuse()
    {
        var document = Create();
        var id = Member(document).EntityId;
        var metadata = document.Metadata with
        {
            EntityStyles = new Dictionary<string, DxfEntityStyle> { [id] = new("locked") },
            Layers = new Dictionary<string, DxfEntityStyle> { ["locked"] = new("locked", IsLocked: true) }
        };
        var locked = new DrawingDocument(document.Entities, document.Dimensions, document.Constraints, metadata);
        DrawingPatternService.TryReconcile(locked, Replace(locked, locked.Entities[0].Transform(Transform2.Translation(1, 1))), out _, out _).Should().BeFalse();
        DrawingPatternService.TryDissolve(locked, "pattern-a", out _, out _).Should().BeFalse();
        var reference = new DrawingDocument(Seed().Entities, Array.Empty<SketchDimension>(), Array.Empty<SketchConstraint>(),
            DrawingDocumentMetadata.Empty with { Mode = DrawingDocumentMode.ReferenceOnly });
        DrawingPatternService.TryCreate(reference, Definition(), out _, out _).Should().BeFalse();
    }

    [Fact]
    public void StylesFollowSourceAndUnrelatedCopiesRemainOrdinary()
    {
        var seed = new DrawingDocument(Seed().Entities, Array.Empty<SketchDimension>(), Array.Empty<SketchConstraint>(),
            DrawingDocumentMetadata.Empty with { EntityStyles = new Dictionary<string, DxfEntityStyle> { ["source"] = new("cuts", TrueColor: 123) } });
        var document = Create(seed);
        var member = Member(document);
        document.Metadata.EntityStyles[member.EntityId].Should().Be(seed.Metadata.EntityStyles["source"]);
        var copy = Entity(document, member.EntityId) with { Id = EntityId.Create("ordinary-copy") };
        var candidate = new DrawingDocument(document.Entities.Append(copy), document.Dimensions, document.Constraints, document.Metadata);
        DrawingPatternService.TryReconcile(document, candidate, out var next, out var reason).Should().BeTrue(reason);
        DrawingPatternService.IsGeneratedMember(next, "ordinary-copy").Should().BeFalse();
    }

    [Fact]
    public void ImplicitDefaultLayerSurvivesNewEntityAssignmentAndFollowsSourceLayerChanges()
    {
        var seed = DrawingLayerService.WithMetadata(Seed(), DrawingDocumentMetadata.Empty with
        {
            ActiveLayerName = "cuts",
            Layers = new Dictionary<string, DxfEntityStyle> { ["cuts"] = new("cuts", TrueColor: 123) }
        });
        var document = Create(seed);
        var memberId = Member(document).EntityId;
        document.Metadata.EntityStyles[memberId].LayerName.Should().Be("0");
        var assigned = DrawingLayerService.AssignNewEntities(seed, document);
        assigned.Should().BeSameAs(document);
        DrawingPatternService.TryReconcile(document, assigned, out var same, out var reason).Should().BeTrue(reason);
        same.Should().BeSameAs(document);
        var moved = DrawingLayerService.Move(document, new[] { "source" }, "cuts");
        DrawingPatternService.TryReconcile(document, moved, out var followed, out reason).Should().BeTrue(reason);
        followed.Metadata.Patterns.Single().Members.Should().OnlyContain(m => followed.Metadata.EntityStyles[m.EntityId].LayerName == "cuts");
        var independentlyMoved = DrawingLayerService.Move(document, new[] { memberId }, "cuts");
        DrawingPatternService.TryReconcile(document, independentlyMoved, out _, out reason).Should().BeFalse();
        reason.Should().Contain("styles follow");
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(123.456, -789.123)]
    public void RadialCircleOrPointAtPatternCenterRefusesCoincidentCopies(double x, double y)
    {
        var center = new Point2(x, y);
        foreach (var source in new DrawingEntity[] { new CircleEntity(SourceId, center, 1), new PointEntity(SourceId, center) })
        {
            var seed = new DrawingDocument(new[] { source });
            DrawingPatternService.TryCreate(seed, Definition(PatternKind.Radial) with { Center = center }, out var refused, out var reason)
                .Should().BeFalse();
            refused.Should().BeSameAs(seed);
            reason.Should().Contain("coincides");
        }
    }

    [Fact]
    public void MultipleSourcesAtGeneratedLimitReconcileWithStableIdsAndRefuseFurtherExpansion()
    {
        var sources = Enumerable.Range(0, 8).Select(i => new PointEntity(EntityId.Create("source-" + i), new Point2(i, i))).ToArray();
        var definition = Definition() with { Rows = 1, Columns = 513, SourceEntityIds = sources.Select(e => e.Id.Value).ToArray() };
        var document = Create(new DrawingDocument(sources), definition);
        document.Metadata.Patterns.Single().Members.Should().HaveCount(DrawingPatternService.MaxGeneratedEntities);
        var changed = Replace(document, sources[0].Transform(Transform2.Translation(1, 2)));
        DrawingPatternService.TryReconcile(document, changed, out var followed, out var reason).Should().BeTrue(reason);
        followed.Metadata.Patterns.Single().Members.Should().Equal(document.Metadata.Patterns.Single().Members);
        DrawingPatternService.TryValidate(followed, out reason).Should().BeTrue(reason);
        DrawingPatternService.TryUpdate(document, definition with { Columns = 514 }, out _, out _).Should().BeFalse();
        DrawingPatternService.TryCreate(document, definition with { Id = "second", Columns = 2 }, out _, out reason).Should().BeFalse();
        reason.Should().Contain("limits");
    }

    [Fact]
    public void ReferenceDimensionsFollowRegeneratedMeasurementsWithoutChangingIntentOrAnchor()
    {
        var document = Create();
        var memberId = Member(document).EntityId;
        var anchor = new Point2(14, 3);
        document = WithIntent(document, dimension: new SketchDimension("reference", SketchDimensionKind.Radius, new[] { memberId }, 1, anchor));
        DrawingPatternService.TryReconcile(document, Replace(document, (CircleEntity)document.Entities[0] with { Radius = 2 }), out var result, out var reason)
            .Should().BeTrue(reason);
        result.Dimensions.Single().Value.Should().Be(2);
        result.Dimensions.Single().IsDriving.Should().BeFalse();
        result.Dimensions.Single().Anchor.Should().Be(anchor);
    }

    [Fact]
    public void GridGlobalTranslationPreservesDefinitionButUnsupportedGlobalRotationRefuses()
    {
        var document = Create();
        var translation = new DrawingDocument(document.Entities.Select(e => e.Transform(Transform2.Translation(10, 20))),
            document.Dimensions, document.Constraints, document.Metadata);
        DrawingPatternService.TryReconcile(document, translation, out var next, out var reason).Should().BeTrue(reason);
        next.Metadata.Patterns.Should().Equal(document.Metadata.Patterns);
        var rotated = new DrawingDocument(document.Entities.Select(e => e.Transform(Transform2.RotationDegrees(90))),
            document.Dimensions, document.Constraints, document.Metadata);
        DrawingPatternService.TryReconcile(document, rotated, out var refused, out reason).Should().BeFalse();
        refused.Should().BeSameAs(document);
    }

    [Fact]
    public void CoreDragRegeneratesSourcesAndRefusesMemberGrips()
    {
        var document = Create();
        SketchGeometryDragService.TryApplyDrag(document, "source|point|center|2|1", new Point2(2, 1), new Point2(4, 3), false,
            out var next, out var reason, automaticSnapConstraints: false).Should().BeTrue(reason);
        ((CircleEntity)Entity(next, Member(next).EntityId)).Center.Should().Be(new Point2(14, 3));
        SketchGeometryDragService.TryApplyDrag(document, Member(document).EntityId, new Point2(12, 1), new Point2(14, 3), false,
            out var refused, out reason).Should().BeFalse();
        refused.Should().BeSameAs(document);
        reason.Should().Contain("unlink");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CoreDragFollowsDimensionAnchorsAfterRegeneratingMembers(bool driving)
    {
        var document = Create();
        var memberId = Member(document).EntityId;
        document = WithIntent(document, dimension: new SketchDimension("member-radius", SketchDimensionKind.Radius,
            new[] { memberId }, 1, new Point2(14, 0), driving));
        SketchGeometryDragService.TryApplyDrag(document, "source|point|center|2|1", new Point2(2, 1), new Point2(4, 3), false,
            out var next, out var reason, automaticSnapConstraints: false).Should().BeTrue(reason);
        next.Dimensions.Single().Anchor.Should().Be(new Point2(16, 2));
        next.Dimensions.Single().Value.Should().Be(1);
        next.Dimensions.Single().IsDriving.Should().Be(driving);
    }

    [Fact]
    public void SolverSourceDimensionRegeneratesAndMemberDimensionRefuses()
    {
        var document = Create();
        var sourceEdit = new SketchDimension("radius", SketchDimensionKind.Radius, new[] { "source" }, 2, isDriving: true);
        var result = new LegacySketchSolverAdapter().Solve(new SketchSolveRequest(document, Array.Empty<SketchConstraint>(), new[] { sourceEdit }));
        result.Status.Should().Be(SketchSolveStatus.Solved);
        ((CircleEntity)Entity(result.Document, Member(document).EntityId)).Radius.Should().Be(2);
        var memberEdit = new SketchDimension("radius", SketchDimensionKind.Radius, new[] { Member(document).EntityId }, 2, isDriving: true);
        result = new LegacySketchSolverAdapter().Solve(new SketchSolveRequest(document, Array.Empty<SketchConstraint>(), new[] { memberEdit }));
        result.Status.Should().Be(SketchSolveStatus.Failed);
        result.Document.Should().BeSameAs(document);
        result.Diagnostics.Should().Contain(message => message.Contains("independently"));
    }

    [Fact]
    public void InvalidParametersAndExpansionRefuseWithoutMutatingInput()
    {
        var seed = Seed();
        foreach (var definition in new[]
        {
            Definition() with { Rows = 0 }, Definition() with { Columns = int.MaxValue },
            Definition() with { Rows = 1, Columns = 1 }, Definition(PatternKind.Radial) with { Count = 1 },
            Definition() with { XPitch = 0 }, Definition() with { XPitch = double.NaN },
            Definition() with { YPitch = double.PositiveInfinity }, Definition() with { Id = "" },
            Definition() with { SourceEntityIds = new[] { "missing" } },
            Definition() with { SourceEntityIds = new[] { "source", "source" } },
            Definition(PatternKind.Radial) with { FullCircle = false, StepAngleDegrees = 120 },
            Definition() with { XPitch = double.MaxValue }
        })
        {
            DrawingPatternService.TryCreate(seed, definition, out var result, out var reason).Should().BeFalse();
            result.Should().BeSameAs(seed);
            reason.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void NativeRejectsForgedOrMissingPatternMembersAndReadsOldMetadata()
    {
        var document = Create();
        foreach (var mode in new[] { "missing", "duplicate", "geometry", "count", "nested" })
        {
            var json = JsonNode.Parse(DxferDocumentSerializer.Write(document))!;
            var pattern = json["metadata"]!["patterns"]![0]!;
            if (mode == "missing") json["entities"]!.AsArray().RemoveAt(1);
            if (mode == "duplicate") pattern["members"]!.AsArray().Add(pattern["members"]![0]!.DeepClone());
            if (mode == "geometry") json["entities"]![1]!["radius"] = 9;
            if (mode == "count") pattern["rows"] = int.MaxValue;
            if (mode == "nested") pattern["sourceEntityIds"]![0] = Member(document).EntityId;
            Action read = () => DxferDocumentSerializer.Read(json.ToJsonString());
            read.Should().Throw<FormatException>();
        }
        var legacy = JsonNode.Parse(DxferDocumentSerializer.Write(Seed()))!;
        legacy["metadata"]!.AsObject().Remove("patterns");
        DxferDocumentSerializer.Read(legacy.ToJsonString()).Metadata.Patterns.Should().BeEmpty();
    }

    [Theory]
    [InlineData(PatternKind.Rectangular, false)]
    [InlineData(PatternKind.Rectangular, true)]
    [InlineData(PatternKind.Staggered, false)]
    [InlineData(PatternKind.Staggered, true)]
    [InlineData(PatternKind.Radial, false)]
    [InlineData(PatternKind.Radial, true)]
    public void ExternalDxfEditsKeepVisibleGeometryAndDiscardStalePatternLinks(PatternKind kind, bool editMember)
    {
        var document = Create(definition: Definition(kind));
        document = WithIntent(document, dimension: new SketchDimension("source-radius", SketchDimensionKind.Radius,
            new[] { "source" }, 1, isDriving: true));
        var snapshot = DxferDocumentSerializer.Write(document);
        var embedded = DxfDocumentWriter.Write(document, new(IncludeNativeDocument: true));
        var intact = DxfDocumentReader.Read(embedded, out var restored);
        restored.Should().BeTrue();
        DxferDocumentSerializer.Write(intact).Should().Be(snapshot);

        var targetId = editMember ? Member(document).EntityId : "source";
        var edited = Replace(document, (CircleEntity)Entity(document, targetId) with { Center = new Point2(123, 456), Radius = 3 });
        var visibleDxf = DxfDocumentWriter.Write(edited);
        // Model an external CAD rewrite of the visible entities that retains the old native OBJECTS envelope.
        var objectStart = embedded.IndexOf("0\nSECTION\n2\nOBJECTS\n", StringComparison.Ordinal);
        objectStart.Should().BeGreaterThan(0);
        visibleDxf.Should().EndWith("0\nEOF\n");
        var externalDxf = visibleDxf[..^"0\nEOF\n".Length] + embedded[objectStart..];
        var imported = DxfDocumentReader.Read(externalDxf, out restored);
        restored.Should().BeFalse();
        imported.Metadata.Patterns.Should().BeEmpty();
        imported.Dimensions.Should().BeEmpty();
        imported.Constraints.Should().BeEmpty();
        imported.Metadata.Warnings.Should().Contain(w => w.Code == "native-document-ignored" && w.Message.Contains("stale"));
        imported.Entities.Should().BeEquivalentTo(DxfDocumentReader.Read(visibleDxf).Entities, options => options.WithStrictOrdering());
        imported.Entities.OfType<CircleEntity>().Should().ContainSingle(c => c.Center == new Point2(123, 456) && c.Radius == 3);
        imported.Entities.Should().HaveCount(document.Entities.Count);

        var reopened = DxferDocumentSerializer.Read(DxferDocumentSerializer.Write(imported));
        reopened.Metadata.Patterns.Should().BeEmpty();
        DrawingPatternService.TryReconcile(reopened, reopened, out var unchanged, out var reason).Should().BeTrue(reason);
        unchanged.Should().BeSameAs(reopened);
        Action invalidNativeWrite = () => DxferDocumentSerializer.Write(edited);
        invalidNativeWrite.Should().Throw<FormatException>();
        DxferDocumentSerializer.Write(document).Should().Be(snapshot);
    }
}

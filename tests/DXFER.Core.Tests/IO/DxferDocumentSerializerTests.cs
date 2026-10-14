using System.Text.Json.Nodes;
using DXFER.CadIO;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.IO;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.IO;

public sealed class DxferDocumentSerializerTests
{
    private readonly ISketchSolver solver = new LegacySketchSolverAdapter();

    [Fact]
    public void ConstrainedRectangleReopensWithIdentityAndContinuesPropagating()
    {
        var original = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("bottom"), new(0, 0), new(10, 0)),
            new LineEntity(EntityId.Create("right"), new(10, 0), new(10, 10)),
            new LineEntity(EntityId.Create("top"), new(10, 10), new(0, 10)),
            new LineEntity(EntityId.Create("left"), new(0, 10), new(0, 0)),
            new LineEntity(EntityId.Create("target"), new(20, 20), new(24, 22), true)
        }, Array.Empty<SketchDimension>(), new[]
        {
            Constraint("bl", SketchConstraintKind.Coincident, "bottom:start", "left:end"),
            Constraint("br", SketchConstraintKind.Coincident, "bottom:end", "right:start"),
            Constraint("tr", SketchConstraintKind.Coincident, "right:end", "top:start"),
            Constraint("tl", SketchConstraintKind.Coincident, "top:end", "left:start"),
            Constraint("bh", SketchConstraintKind.Horizontal, "bottom"),
            Constraint("th", SketchConstraintKind.Horizontal, "top"),
            Constraint("lv", SketchConstraintKind.Vertical, "left"),
            Constraint("rv", SketchConstraintKind.Vertical, "right")
        });
        var solved = solver.Solve(SketchSolveRequest.FromDocument(original));
        solved.Status.Should().Be(SketchSolveStatus.Solved);
        var reopened = RoundTrip(solved.Document);
        reopened.Should().BeEquivalentTo(solved.Document, o => o.WithStrictOrdering());

        var moved = solver.Solve(new SketchSolveRequest(reopened,
            new[] { Constraint("midpoint", SketchConstraintKind.Midpoint, "target", "bottom:start") },
            Array.Empty<SketchDimension>()));

        moved.Status.Should().Be(SketchSolveStatus.Solved);
        ((LineEntity)moved.Document.Entities[0]).Start.Should().Be(new Point2(22, 21));
        moved.Document.Constraints.Should().OnlyContain(c => c.State == SketchConstraintState.Satisfied);
    }

    [Fact]
    public void DrivingLengthAndAnchorSurviveAndSameIdEditStillReplacesDimension()
    {
        var reopened = RoundTrip(LineDocument());
        reopened.Dimensions.Single().Anchor.Should().Be(new Point2(5, -2));
        var length = reopened.Dimensions.Single();
        var result = solver.Solve(new SketchSolveRequest(reopened, Array.Empty<SketchConstraint>(),
            new[] { new SketchDimension(length.Id, length.Kind, length.ReferenceKeys, 20, length.Anchor, true) }));

        result.Status.Should().Be(SketchSolveStatus.Solved);
        result.Document.Dimensions.Should().ContainSingle().Which.Value.Should().Be(20);
        ((LineEntity)result.Document.Entities.Single()).End.Should().Be(new Point2(20, 0));
        RoundTrip(result.Document).Should().BeEquivalentTo(result.Document, o => o.WithStrictOrdering());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedDirectionalConflictPreservesGeometryAndIntentWithoutSolvingOnRead(bool segment)
    {
        DrawingEntity entity = segment
            ? new PolylineEntity(EntityId.Create("edge"), new[] { new Point2(0, 0), new Point2(10, 0), new Point2(10, 5) })
            : new LineEntity(EntityId.Create("edge"), new(0, 0), new(10, 0));
        var reference = segment ? "edge|segment|0" : "edge";
        var original = new DrawingDocument(new[] { entity }, Array.Empty<SketchDimension>(),
            new[] { Constraint("horizontal", SketchConstraintKind.Horizontal, reference) });
        var failed = solver.Solve(new SketchSolveRequest(original,
            new[] { Constraint("vertical", SketchConstraintKind.Vertical, reference) }, Array.Empty<SketchDimension>()));
        failed.Status.Should().Be(SketchSolveStatus.OverConstrained);

        var reopened = RoundTrip(failed.Document);

        reopened.Should().BeEquivalentTo(failed.Document, o => o.WithStrictOrdering());
        reopened.Entities.Should().BeEquivalentTo(original.Entities, o => o.WithStrictOrdering());
        reopened.Constraints.Should().Contain(c => c.Id == "vertical" && c.State == SketchConstraintState.Unsatisfied);
    }

    [Fact]
    public void FailedDrivingLengthRemainsEditableAfterReopen()
    {
        var original = LineDocument();
        var failed = solver.Solve(new SketchSolveRequest(original, Array.Empty<SketchConstraint>(),
            new[] { new SketchDimension("conflict", SketchDimensionKind.LinearDistance,
                new[] { "edge:start", "edge:end" }, 20, new Point2(5, 2), true) }));
        failed.Status.Should().Be(SketchSolveStatus.OverConstrained);

        var reopened = RoundTrip(failed.Document);
        reopened.Entities.Should().Equal(original.Entities);
        reopened.Dimensions.Should().HaveCount(2);
        var conflict = reopened.Dimensions.Single(d => d.Id == "conflict");
        conflict.Value.Should().Be(20);
        SketchDimensionSolverService.GetDimensionState(reopened, conflict).Should().Be(SketchConstraintState.Unsatisfied);
        var recovered = new DrawingDocument(reopened.Entities,
            reopened.Dimensions.Where(d => d.Id != "conflict"), reopened.Constraints, reopened.Metadata);
        solver.Solve(SketchSolveRequest.FromDocument(recovered)).Status.Should().Be(SketchSolveStatus.Solved);
    }

    [Fact]
    public void AllEntityTypesAndMetadataRoundTripWithoutDxfConversion()
    {
        var entities = new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("line"), new(1.123456789012345, 2), new(3, 4), true),
            new CircleEntity(EntityId.Create("circle"), new(2, 3), 4),
            new ArcEntity(EntityId.Create("arc"), new(3, 4), 5, 300, 60),
            new EllipseEntity(EntityId.Create("ellipse"), new(4, 5), new(3, 2), .5, 15, 270),
            new PointEntity(EntityId.Create("point"), new(5, 6)),
            new PolylineEntity(EntityId.Create("polyline"), new[] { new Point2(0, 0), new Point2(1, 1), new Point2(0, 0) }),
            new PolygonEntity(EntityId.Create("polygon"), new(6, 7), 3, 12, 7, true),
            new SplineEntity(EntityId.Create("rational"), 2, new[] { new Point2(0, 0), new Point2(1, 2), new Point2(3, 0) },
                new double[] { 0, 0, 0, 1, 1, 1 }, new double[] { 1, .5, 1 }),
            SplineEntity.FromFitPoints(EntityId.Create("fit"), new[] { new Point2(0, 0), new Point2(3, 4), new Point2(6, 0) },
                true, new Point2(-1, 2), new Point2(7, 2))
        };
        var metadata = new DrawingDocumentMetadata
        {
            Units = DrawingUnits.Millimeters,
            Mode = DrawingDocumentMode.ReferenceOnly,
            TrustedSource = true,
            SourceFileName = "original.dxf",
            SourceSha256 = "source-hash",
            NormalizedFileName = "normalized.dxf",
            NormalizedSha256 = "normalized-hash",
            Warnings = new[] { new DrawingDocumentWarning("review", DrawingDocumentWarningSeverity.Warning, "Review imported geometry.") },
            UnsupportedEntityCounts = new Dictionary<string, int> { ["INSERT"] = 2 },
            EntityStyles = new Dictionary<string, DxfEntityStyle> { ["line"] = new("BEND", "DASHED", 3) }
        };
        var document = new DrawingDocument(entities,
            new[] { new SketchDimension("reference", SketchDimensionKind.Radius, new[] { "circle" }, 4, isDriving: false) },
            new[] { new SketchConstraint("suppressed", SketchConstraintKind.Fix, new[] { "point" }, SketchConstraintState.Suppressed),
                new SketchConstraint("failed-reference", SketchConstraintKind.Coincident, new[] { "deleted:start", "line:end" }, SketchConstraintState.Unsatisfied) },
            metadata);

        var reopened = RoundTrip(document);
        reopened.Should().BeEquivalentTo(new DrawingDocument(entities, document.Dimensions, document.Constraints,
            metadata with { TrustedSource = false }), o => o.WithStrictOrdering());
        var text = DxferDocumentSerializer.Write(reopened);
        DxferDocumentSerializer.Write(DxferDocumentSerializer.Read(text)).Should().Be(text);
    }

    [Fact]
    public void OrdinaryDxfRemainsGeometryOnlyAndLegacyDxfStillImports()
    {
        var dxf = DxfDocumentWriter.Write(LineDocument());
        dxf.Should().NotContain(DxferDocumentSerializer.Format);
        var reopened = DrawingDocumentFile.Read("external.dxf", dxf);
        reopened.Entities.Should().ContainSingle().Which.Should().BeOfType<LineEntity>();
        reopened.Constraints.Should().BeEmpty();
        reopened.Dimensions.Should().BeEmpty();
        var legacy = DrawingDocumentFile.Read("legacy.dxf", "0\nLINE\n10\n0\n20\n0\n11\n10\n21\n0\n0\nEOF\n");
        ((LineEntity)legacy.Entities.Single()).End.Should().Be(new Point2(10, 0));
    }

    [Fact]
    public void NativeRoutingUsesMarkerAndNeverFallsBackToDxfForInvalidNativeFiles()
    {
        var text = DxferDocumentSerializer.Write(LineDocument());
        DrawingDocumentFile.Read("renamed.dxf", "\uFEFF \n" + text).Dimensions.Should().ContainSingle();
        DrawingDocumentFile.IsNativeFileName("DRAWING.DXFER.JSON").Should().BeTrue();
        var read = () => DrawingDocumentFile.Read("broken.dxfer.json", DxfDocumentWriter.Write(LineDocument()));
        read.Should().Throw<FormatException>();
        var sidecar = () => DrawingDocumentFile.Read("old.dxfer.json", DxferSidecarWriter.Write(LineDocument()));
        sidecar.Should().Throw<FormatException>().WithMessage("*Analysis sidecars*");
    }

    [Theory]
    [InlineData("version")]
    [InlineData("unknown-metadata")]
    [InlineData("unknown-kind")]
    [InlineData("unknown-state")]
    [InlineData("null-metadata")]
    [InlineData("null-warnings")]
    [InlineData("null-entity")]
    [InlineData("null-dimension")]
    [InlineData("null-constraint")]
    [InlineData("null-warning")]
    [InlineData("null-styles")]
    [InlineData("null-counts")]
    [InlineData("null-reference-item")]
    [InlineData("null-id")]
    [InlineData("null-references")]
    [InlineData("empty-reference")]
    [InlineData("duplicate-entity")]
    [InlineData("duplicate-dimension")]
    [InlineData("duplicate-constraint")]
    [InlineData("missing-geometry")]
    [InlineData("missing-coordinate")]
    [InlineData("missing-dimensions")]
    [InlineData("missing-driving")]
    [InlineData("invalid-units")]
    public void CorruptOrUnknownNativeDataIsRejectedAtomically(string mutation)
    {
        var root = JsonNode.Parse(DxferDocumentSerializer.Write(LineDocument()))!;
        switch (mutation)
        {
            case "version": root["schemaVersion"] = 999; break;
            case "unknown-metadata": root["metadata"]!["futureSolver"] = "unknown"; break;
            case "unknown-kind": root["entities"]![0]!["kind"] = "solid"; break;
            case "unknown-state": root["constraints"]![0]!["state"] = "FutureState"; break;
            case "null-metadata": root["metadata"] = null; break;
            case "null-warnings": root["metadata"]!["warnings"] = null; break;
            case "null-entity": root["entities"]![0] = null; break;
            case "null-dimension": root["dimensions"]![0] = null; break;
            case "null-constraint": root["constraints"]![0] = null; break;
            case "null-warning": root["metadata"]!["warnings"]!.AsArray().Add((JsonNode?)null); break;
            case "null-styles": root["metadata"]!["entityStyles"] = null; break;
            case "null-counts": root["metadata"]!["unsupportedEntityCounts"] = null; break;
            case "null-reference-item": root["constraints"]![0]!["referenceKeys"]![0] = null; break;
            case "null-id": root["entities"]![0]!["id"] = null; break;
            case "null-references": root["dimensions"]![0]!["referenceKeys"] = null; break;
            case "empty-reference": root["constraints"]![0]!["referenceKeys"]![0] = ""; break;
            case "duplicate-entity": root["entities"]!.AsArray().Add(root["entities"]![0]!.DeepClone()); break;
            case "duplicate-dimension": root["dimensions"]!.AsArray().Add(root["dimensions"]![0]!.DeepClone()); break;
            case "duplicate-constraint": root["constraints"]!.AsArray().Add(root["constraints"]![0]!.DeepClone()); break;
            case "missing-geometry": root["entities"]![0]!.AsObject().Remove("end"); break;
            case "missing-coordinate": root["entities"]![0]!["end"]!.AsObject().Remove("x"); break;
            case "missing-dimensions": root.AsObject().Remove("dimensions"); break;
            case "missing-driving": root["dimensions"]![0]!.AsObject().Remove("isDriving"); break;
            case "invalid-units": root["metadata"]!["units"] = 999; break;
        }
        var read = () => DrawingDocumentFile.Read("drawing.dxfer.json", root.ToJsonString());
        read.Should().Throw<FormatException>();
    }

    [Theory]
    [InlineData("{", "")]
    [InlineData("\"format\": \"dxfer-document\",", "\"format\": \"dxfer-document\", \"format\": \"dxfer-document\",")]
    [InlineData("\"value\": 10", "\"value\": 1e999")]
    public void MalformedJsonDuplicatePropertiesAndNonfiniteValuesAreRejected(string before, string after)
    {
        var text = DxferDocumentSerializer.Write(LineDocument()).Replace(before, after, StringComparison.Ordinal);
        var read = () => DxferDocumentSerializer.Read(text);
        read.Should().Throw<FormatException>();
    }

    [Fact]
    public void EmptyDocumentRoundTripsAndOversizedInputIsRejected()
    {
        RoundTrip(new DrawingDocument(Array.Empty<DrawingEntity>())).Entities.Should().BeEmpty();
        var read = () => DxferDocumentSerializer.Read(new string(' ', DxferDocumentSerializer.MaxTextLength + 1));
        read.Should().Throw<FormatException>().WithMessage("*size limit*");
    }

    [Fact]
    public void EmbeddedNativeDocumentToleratesLineEndingsAndDoesNotAddGeometry()
    {
        var document = LineDocument();
        var plain = DxfDocumentWriter.Write(document);
        var embedded = DxfDocumentWriter.Write(document, new(IncludeNativeDocument: true));
        embedded[..embedded.IndexOf("0\nSECTION\n2\nOBJECTS\n", StringComparison.Ordinal)]
            .Should().Be(plain[..^"0\nEOF\n".Length]);
        var reopened = DxfDocumentReader.Read(embedded.Replace("\n", "\r\n"), out var restored);
        restored.Should().BeTrue();
        reopened.Should().BeEquivalentTo(document, o => o.WithStrictOrdering());
        var legacy = DxfDocumentReader.Read(plain, out restored);
        restored.Should().BeFalse();
        legacy.Entities.Should().HaveCount(document.Entities.Count);
    }

    [Theory]
    [InlineData("coordinate")]
    [InlineData("units")]
    [InlineData("deleted-entity")]
    [InlineData("malformed-record")]
    [InlineData("version")]
    [InlineData("payload")]
    [InlineData("owner")]
    [InlineData("duplicate-record")]
    public void StaleCorruptOrUnknownEnvelopeNeverOverridesCurrentDxfGeometry(string mutation)
    {
        var text = DxfDocumentWriter.Write(LineDocument(), new(IncludeNativeDocument: true));
        switch (mutation)
        {
            case "coordinate": text = text.Replace("11\n10\n", "11\n25\n", StringComparison.Ordinal); break;
            case "units": text = text.Replace("$INSUNITS\n70\n1\n", "$INSUNITS\n70\n4\n", StringComparison.Ordinal); break;
            case "deleted-entity":
                var start = text.IndexOf("0\nLINE\n", StringComparison.Ordinal);
                var end = text.IndexOf("0\nENDSEC\n", start, StringComparison.Ordinal);
                text = text.Remove(start, end - start);
                break;
            case "malformed-record": text = text.Replace("0\nEOF\n", "bad-code\nbad-value\n0\nEOF\n", StringComparison.Ordinal); break;
            case "version": text = text.Replace("DXFER_NATIVE_V1", "DXFER_NATIVE_V99", StringComparison.Ordinal); break;
            case "payload":
                var chunk = text.IndexOf("310\n", StringComparison.Ordinal) + 4;
                text = text.Remove(chunk, 2).Insert(chunk, "FF");
                break;
            case "owner": text = text.Replace("330\n8\n", "330\nBAD\n", StringComparison.Ordinal); break;
            case "duplicate-record":
                var record = text.IndexOf("0\nXRECORD\n", StringComparison.Ordinal);
                var recordEnd = text.IndexOf("0\nENDSEC\n", record, StringComparison.Ordinal);
                text = text.Insert(recordEnd, text[record..recordEnd]);
                break;
        }
        var reopened = DxfDocumentReader.Read(text, out var restored);
        restored.Should().BeFalse();
        reopened.Constraints.Should().BeEmpty();
        reopened.Dimensions.Should().BeEmpty();
        reopened.Metadata.Warnings.Should().Contain(w => w.Code == "native-document-ignored");
        if (mutation == "deleted-entity") reopened.Entities.Should().BeEmpty();
        else ((LineEntity)reopened.Entities.Single()).End.Should().Be(new Point2(mutation == "coordinate" ? 25 : 10, 0));
        if (mutation == "units") reopened.Metadata.Units.Should().Be(DrawingUnits.Millimeters);
    }

    [Fact]
    public void NativeSnapshotRetainsCoincidentDuplicateAndConstructionEntities()
    {
        var line = new LineEntity(EntityId.Create("a"), new(0, 0), new(10, 0));
        var document = new DrawingDocument(new DrawingEntity[]
        {
            line, line with { Id = EntityId.Create("b") }, line with { Id = EntityId.Create("guide"), IsConstruction = true }
        }, Array.Empty<SketchDimension>(), new[] { Constraint("coincident", SketchConstraintKind.Coincident, "a:start", "b:start") });
        RoundTrip(document).Should().BeEquivalentTo(document, o => o.WithStrictOrdering());
    }

    [Fact]
    public void GrainAnnotationDoesNotInvalidateEmbeddedSnapshot()
    {
        var document = LineDocument();
        var text = DxfDocumentWriter.Write(document, new(new GrainAnnotation("GRAIN 90", 90), true));
        var reopened = DxfDocumentReader.Read(text, out var restored);
        restored.Should().BeTrue();
        reopened.Should().BeEquivalentTo(document, o => o.WithStrictOrdering());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IntentWithNoCutEntitiesPersistsOnlyInObjects(bool construction)
    {
        var entities = construction
            ? new DrawingEntity[] { new LineEntity(EntityId.Create("guide"), new(0, 0), new(10, 0), true) }
            : Array.Empty<DrawingEntity>();
        var document = new DrawingDocument(entities,
            new[] { new SketchDimension("intent", SketchDimensionKind.LinearDistance, new[] { "guide:start", "guide:end" }, 10, isDriving: true) },
            new[] { new SketchConstraint("failed", SketchConstraintKind.Vertical, new[] { "guide" }, SketchConstraintState.Unsatisfied) });
        var dxf = DxfDocumentWriter.Write(document, new(IncludeNativeDocument: true));
        dxf.Should().Contain("0\nSECTION\n2\nENTITIES\n0\nENDSEC\n");
        RoundTrip(document).Should().BeEquivalentTo(document, o => o.WithStrictOrdering());
    }

    private static DrawingDocument LineDocument() => new(
        new DrawingEntity[] { new LineEntity(EntityId.Create("edge"), new(0, 0), new(10, 0)) },
        new[] { new SketchDimension("length", SketchDimensionKind.LinearDistance,
            new[] { "edge:start", "edge:end" }, 10, new Point2(5, -2), true) },
        new[] { Constraint("horizontal", SketchConstraintKind.Horizontal, "edge") },
        new DrawingDocumentMetadata { Units = DrawingUnits.Inches });

    private static DrawingDocument RoundTrip(DrawingDocument document)
    {
        var json = DrawingDocumentFile.Read("test.dxfer.json", DxferDocumentSerializer.Write(document));
        var embedded = DxfDocumentWriter.Write(document, new(IncludeNativeDocument: true));
        var reopened = DxfDocumentReader.Read(embedded, out var restored);
        restored.Should().BeTrue();
        reopened.Should().BeEquivalentTo(json, o => o.WithStrictOrdering());
        return reopened;
    }

    private static SketchConstraint Constraint(string id, SketchConstraintKind kind, params string[] references) =>
        new(id, kind, references, SketchConstraintState.Satisfied);
}

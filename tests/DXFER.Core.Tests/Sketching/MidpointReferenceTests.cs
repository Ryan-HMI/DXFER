using DXFER.Blazor.Interop;
using DXFER.Blazor.Sketching;
using DXFER.CadIO;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Operations;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class MidpointReferenceTests
{
    [Theory]
    [InlineData("source:mid", "source:mid")]
    [InlineData("source|segment|0:mid", "source|segment|0:mid")]
    public void MidpointKeysNormalizeToIdentityNotCoordinates(string key, string expected)
    {
        SketchReference.TryNormalize(key, out var normalized).Should().BeTrue();
        normalized.Should().Be(expected);
    }

    [Theory]
    [InlineData("source|point|mid|999|999")]
    [InlineData("source|point|mid-0|999|999")]
    public void RawCanvasMidpointsRemainNonpersistent(string key) =>
        SketchReference.TryParse(key, out _).Should().BeFalse();

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void AcquiredAxisFollowsEditedSourceAndSurvivesNativeReopen(bool polyline, bool vertical)
    {
        var document = Create(polyline, vertical);
        var before = DxfDocumentWriter.Write(document, new(IncludeNativeDocument: true));
        var reopened = DxfDocumentReader.Read(before);
        var snapshot = reopened.Entities.ToArray();
        MoveSource(reopened, polyline, out var moved).Should().BeTrue();
        var point = moved.Entities.OfType<PointEntity>().Single().Location;
        point.Should().Be(vertical ? new Point2(7, 20) : new Point2(20, 2));
        moved.Constraints.Should().OnlyContain(c => c.State == SketchConstraintState.Satisfied);
        reopened.Entities.Should().Equal(snapshot, "the history snapshot remains immutable");
        SketchReferenceResolver.TryGetPoint(reopened, Key(polyline), out var originalMidpoint).Should().BeTrue();
        originalMidpoint.Should().Be(new Point2(5, 0));
        moved.Constraints.SelectMany(c => c.ReferenceKeys).Should().Contain(Key(polyline));
        var again = DxfDocumentReader.Read(DxfDocumentWriter.Write(moved, new(IncludeNativeDocument: true)));
        SketchReferenceResolver.TryGetPoint(again, Key(polyline), out var midpoint).Should().BeTrue();
        midpoint.Should().Be(new Point2(7, 2));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FixedPeerRefusesAndUnlockRecovers(bool polyline)
    {
        var document = Create(polyline, false);
        var fixedDocument = new DrawingDocument(document.Entities, document.Dimensions,
            document.Constraints.Append(new SketchConstraint("fix", SketchConstraintKind.Fix, ["created"], SketchConstraintState.Satisfied)));
        MoveSource(fixedDocument, polyline, out var rejected).Should().BeFalse();
        rejected.Entities.Should().Equal(document.Entities);
        MoveSource(document, polyline, out _).Should().BeTrue();
    }

    [Fact]
    public void LockedPeerRemainsProtectedAtDocumentAcceptanceBoundary()
    {
        var document = Create(false, false);
        document = new(document.Entities, document.Dimensions, document.Constraints, document.Metadata with
        {
            EntityStyles = new Dictionary<string, DxfEntityStyle> { ["created"] = new("Locked") },
            Layers = new Dictionary<string, DxfEntityStyle> { ["Locked"] = new("Locked", IsLocked: true) }
        });
        MoveSource(document, false, out var candidate).Should().BeTrue();
        DrawingLayerService.PreservesProtectedGeometry(document, candidate).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectMidpointSnapRemainsMidpointNotAutomaticCoincident(bool polyline)
    {
        var source = Source(polyline);
        var point = new PointEntity(EntityId.Create("created"), new(5, 0));
        var constraints = SketchCreationConstraintFactory.CreateConstraintsForInsertion("point", [source], [point], k => k.ToString());
        constraints.Should().Contain(c => c.Kind == SketchConstraintKind.Midpoint);
        constraints.Should().NotContain(c => c.Kind == SketchConstraintKind.Coincident);
        SketchReferenceResolver.EnumeratePersistentPoints([source]).Should().NotContain(p => p.Reference.ToString().EndsWith(":mid"));
    }

    [Theory]
    [InlineData("arc")]
    [InlineData("circle")]
    [InlineData("ellipse")]
    [InlineData("polygon")]
    public void UnsupportedCurveMidpointNeverResolvesToChord(string kind)
    {
        var id = EntityId.Create("source");
        DrawingEntity entity = kind switch
        {
            "arc" => new ArcEntity(id, new(0, 0), 5, 0, 90),
            "circle" => new CircleEntity(id, new(0, 0), 5),
            "ellipse" => new EllipseEntity(id, new(0, 0), new(5, 0), 0.5),
            _ => new PolygonEntity(id, new(0, 0), 5, 0, 6)
        };
        var document = new DrawingDocument([entity]);
        SketchReferenceResolver.TryGetPoint(document, "source:mid", out _).Should().BeFalse();
        SketchReferenceResolver.TryGetPoint(document, "source|segment|0:mid", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ReverseDragRefusesWithoutRepositioningDerivedMidpoint(bool polyline, bool vertical)
    {
        var document = Create(polyline, vertical);
        var point = document.Entities.OfType<PointEntity>().Single().Location;
        SketchGeometryDragService.TryApplyDrag(document, "created", point, new(point.X + 3, point.Y + 3),
            false, out var rejected, out _, false).Should().BeFalse();
        rejected.Entities.Should().Equal(document.Entities);
    }

    [Fact]
    public void SourceDimensionEditPropagatesToAcquiredMidpointAxis()
    {
        var document = Create(false, true);
        var result = new LegacySketchSolverAdapter().Solve(new SketchSolveRequest(document, [],
            [new SketchDimension("length", SketchDimensionKind.LinearDistance, ["source:start", "source:end"], 12, null, true)]));
        result.Status.Should().Be(SketchSolveStatus.Solved);
        SketchReferenceResolver.TryGetPoint(result.Document, "source:mid", out var midpoint).Should().BeTrue();
        result.Document.Entities.OfType<PointEntity>().Single().Location.X.Should().Be(midpoint.X);
        midpoint.X.Should().Be(6);
    }

    [Fact]
    public void UnsafeImportedPolylineMidpointIsUnavailable()
    {
        var document = Create(true, false);
        document = new(document.Entities, document.Dimensions, document.Constraints, document.Metadata with
        {
            Warnings = [new(CutPathLengthService.UnsafeImportCode, DrawingDocumentWarningSeverity.Warning, "Polyline bulges were not preserved.")]
        });
        SketchReferenceResolver.TryGetPoint(document, Key(true), out _).Should().BeFalse();
        SketchConstraintService.ValidateConstraint(document, document.Constraints.Single()).State.Should().Be(SketchConstraintState.Unsatisfied);
    }

    private static string Key(bool polyline) => polyline ? "source|segment|0:mid" : "source:mid";
    private static DrawingEntity Source(bool polyline) => polyline
        ? new PolylineEntity(EntityId.Create("source"), [new(0, 0), new(10, 0), new(10, 10)])
        : new LineEntity(EntityId.Create("source"), new(0, 0), new(10, 0));

    private static DrawingDocument Create(bool polyline, bool vertical)
    {
        var source = Source(polyline);
        var point = new PointEntity(EntityId.Create("created"), vertical ? new(5, 20) : new(20, 0));
        var kind = vertical ? "Vertical" : "Horizontal";
        var constraints = SketchCreationConstraintFactory.CreateConstraintsForInsertion("point", [source], [point], k => k.ToString(),
            creationSnaps: [new CanvasCreationSnapDto(point.Location, kind, [Key(polyline)])]);
        return new([source, point], [], constraints);
    }

    private static bool MoveSource(DrawingDocument document, bool polyline, out DrawingDocument moved) =>
        SketchGeometryDragService.TryApplyDrag(document, polyline ? "source|point|vertex-1|10|0" : "source|point|end|10|0",
            new(10, 0), new(14, 4), false, out moved, out _, false);
}

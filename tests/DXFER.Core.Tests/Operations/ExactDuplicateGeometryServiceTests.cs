using DXFER.CadIO;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Operations;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Operations;

public sealed class ExactDuplicateGeometryServiceTests
{
    [Fact]
    public void RoundedAccessPlateDeduplicatesHolesWithoutChangingBoundsAndSurvivesExport()
    {
        var document = AccessPlate();
        var result = ExactDuplicateGeometryService.Remove(document);
        result.RemovedCount.Should().Be(2);
        result.Document.GetBounds().Should().Be(document.GetBounds());
        result.Document.Entities.OfType<CircleEntity>().Should().HaveCount(2);
        result.Document.Entities.Should().HaveCount(10);
        CutPathLengthService.Calculate(result.Document).CutLengthInches.Should()
            .BeApproximately((decimal)(20 + Math.Tau * .25 + 2 * Math.PI * .221), .000001m);
        ClosedContourService.Calculate(result.Document).Should().Be(new ClosedContourResult(3, null));
        ExactDuplicateGeometryService.Remove(result.Document).RemovedCount.Should().Be(0);
        var normalized = DrawingNormalizationService.AutoNormalize(document);
        normalized.OriginalDocument.Should().BeSameAs(document);
        normalized.NormalizedDocument.Entities.Should().HaveCount(10);
        var exported = DxfDocumentReader.Read(DxfDocumentWriter.Write(normalized.NormalizedDocument));
        ClosedContourService.Calculate(exported).ClosedContourCount.Should().Be(3);
        CutPathLengthService.Calculate(exported).CutLengthInches.Should().BeApproximately(22.959380m, .000001m);
    }

    [Fact]
    public void ReversedLinesEquivalentArcsAndCyclicReversedWholePolylinesDeduplicate()
    {
        var document = new DrawingDocument([
            Line("l1", 0, 0, 4, 0), Line("l2", 4, 0, 0, 0),
            new ArcEntity(Id("a1"), new(0, 0), 1, -90, 0),
            new ArcEntity(Id("a2"), new(0, 0), 1, 270, 360),
            new PolylineEntity(Id("p1"), [new(0, 0), new(1, 0), new(1, 1), new(0, 0)]),
            new PolylineEntity(Id("p2"), [new(1, 1), new(1, 0), new(0, 0), new(1, 1)]),
            new PolylineEntity(Id("o1"), [new(0, 0), new(1, 0), new(1, 1)]),
            new PolylineEntity(Id("o2"), [new(1, 1), new(1, 0), new(0, 0)])]);
        ExactDuplicateGeometryService.Remove(document).RemovedCount.Should().Be(4);
    }

    [Fact]
    public void IdenticalPolygonEllipseAndSplineParametersDeduplicateButSplineStillNeedsLengthReview()
    {
        var polygon = new PolygonEntity(Id("p1"), new(0, 0), 3, 15, 5);
        var ellipse = new EllipseEntity(Id("e1"), new(10, 0), new(2, 1), .5);
        var document = new DrawingDocument([
            polygon, polygon with { Id = Id("p2") }, ellipse, ellipse with { Id = Id("e2") },
            SplineEntity.FromFitPoints(Id("s1"), [new(20, 0), new(21, 2), new(23, 0)]),
            SplineEntity.FromFitPoints(Id("s2"), [new(20, 0), new(21, 2), new(23, 0)])]);
        var result = ExactDuplicateGeometryService.Remove(document);
        result.RemovedCount.Should().Be(3);
        CutPathLengthService.Calculate(result.Document).CutLengthInches.Should().BeNull();
    }

    [Fact]
    public void NearbyPartialOverlapsDifferentArcsAndDifferentRepresentationsAreNotMerged()
    {
        var document = new DrawingDocument([
            Line("l1", 0, 0, 4, 0), Line("near", 0, 1e-12, 4, 1e-12), Line("partial", 1, 0, 3, 0),
            new PolylineEntity(Id("poly"), [new(0, 0), new(4, 0)]),
            new CircleEntity(Id("c1"), new(0, 0), 1), new CircleEntity(Id("c2"), new(0, 0), 1 + 1e-12),
            new ArcEntity(Id("a1"), new(0, 0), 1, 0, 90),
            new ArcEntity(Id("a2"), new(0, 0), 1, 1e-12, 90)]);
        ExactDuplicateGeometryService.Remove(document).RemovedCount.Should().Be(0);
        ClosedContourService.Calculate(document).ClosedContourCount.Should().BeNull();
    }

    [Fact]
    public void StylesConstructionGrainBendEtchAndPointsArePreserved()
    {
        var entities = Enumerable.Range(0, 8).Select(i => (DrawingEntity)Line(i.ToString(), 0, 0, 1, 0)).ToArray();
        var styles = new Dictionary<string, DxfEntityStyle> {
            ["0"] = new("CUT"), ["1"] = new("OTHER"), ["2"] = new("CUT", ColorNumber: 2),
            ["3"] = new("GRAIN"), ["4"] = new("GRAIN"), ["5"] = new("BEND"),
            ["6"] = new("ETCH"), ["7"] = new("CUT", "DASHED") };
        var document = new DrawingDocument(entities.Concat([
            Line("construction1", 0, 0, 1, 0) with { IsConstruction = true },
            Line("construction2", 0, 0, 1, 0) with { IsConstruction = true },
            new PointEntity(Id("point1"), new(0, 0)), new PointEntity(Id("point2"), new(0, 0))]), [], [],
            DrawingDocumentMetadata.Empty with { EntityStyles = styles });
        ExactDuplicateGeometryService.Remove(document).RemovedCount.Should().Be(0);
    }

    [Fact]
    public void ReferencedCopyWinsAndTwoReferencedCopiesRemainIntact()
    {
        var document = new DrawingDocument([Line("first", 0, 0, 1, 0), Line("referenced", 1, 0, 0, 0)],
            [], [new SketchConstraint("constraint", SketchConstraintKind.Horizontal, ["referenced:start", "referenced:end"])]);
        var result = ExactDuplicateGeometryService.Remove(document);
        result.RemovedCount.Should().Be(1);
        result.Document.Entities.Single().Id.Value.Should().Be("referenced");
        result.Document.Constraints.Should().Equal(document.Constraints);
        var both = new DrawingDocument(document.Entities,
            [new SketchDimension("dimension", SketchDimensionKind.LinearDistance, ["first:start", "first:end"], 1)], document.Constraints);
        var protectedResult = ExactDuplicateGeometryService.Remove(both);
        protectedResult.RemovedCount.Should().Be(0);
        protectedResult.ProtectedCount.Should().Be(1);
        protectedResult.Document.Should().BeSameAs(both);
    }

    [Fact]
    public void ReferenceOnlyAndUnsafeImportEvidenceAreNotDiscarded()
    {
        var duplicate = new DrawingDocument([Line("one", 0, 0, 1, 0), Line("two", 0, 0, 1, 0)]);
        var readOnly = new DrawingDocument(duplicate.Entities, [], [],
            DrawingDocumentMetadata.Empty with { Mode = DrawingDocumentMode.ReferenceOnly });
        ExactDuplicateGeometryService.Remove(readOnly).Document.Should().BeSameAs(readOnly);
        var warning = new DrawingDocumentWarning(CutPathLengthService.UnsafeImportCode, DrawingDocumentWarningSeverity.Warning, "Unsupported geometry");
        var unsafeDocument = new DrawingDocument(duplicate.Entities, [], [],
            DrawingDocumentMetadata.Empty with { Warnings = [warning] });
        var cleaned = ExactDuplicateGeometryService.Remove(unsafeDocument).Document;
        cleaned.Metadata.Warnings.Should().Contain(warning);
        CutPathLengthService.Calculate(cleaned).CutLengthInches.Should().BeNull();
    }

    private static DrawingDocument AccessPlate() => new([
        new CircleEntity(Id("h1"), new(.25, 2.25), .1105), new CircleEntity(Id("h2"), new(6.25, 2.25), .1105),
        new CircleEntity(Id("h3"), new(.25, 2.25), .1105), new CircleEntity(Id("h4"), new(6.25, 2.25), .1105),
        Line("bottom", .25, 0, 6.25, 0), Line("top", .25, 4.5, 6.25, 4.5),
        Line("left", 0, .25, 0, 4.25), Line("right", 6.5, .25, 6.5, 4.25),
        new ArcEntity(Id("br"), new(6.25, .25), .25, -90, 0),
        new ArcEntity(Id("tr"), new(6.25, 4.25), .25, 0, 90),
        new ArcEntity(Id("tl"), new(.25, 4.25), .25, 90, 180),
        new ArcEntity(Id("bl"), new(.25, .25), .25, 180, 270)]);
    private static EntityId Id(string id) => EntityId.Create(id);
    private static LineEntity Line(string id, double x, double y, double x2, double y2) => new(Id(id), new(x, y), new(x2, y2));
}

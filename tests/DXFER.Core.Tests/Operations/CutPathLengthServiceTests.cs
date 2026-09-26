using DXFER.CadIO;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Operations;
using FluentAssertions;

namespace DXFER.Core.Tests.Operations;

public sealed class CutPathLengthServiceTests
{
    [Theory]
    [InlineData(DrawingUnits.Inches, 1)]
    [InlineData(DrawingUnits.Millimeters, 1)]
    [InlineData(DrawingUnits.Unspecified, 1)]
    public void RectangleAndHolesIncludeEveryCutPath(DrawingUnits units, double scale)
    {
        var document = Document(units,
            new PolylineEntity(Id("outline"), new[] { P(0, 0), P(10 * scale, 0), P(10 * scale, 5 * scale), P(0, 5 * scale), P(0, 0) }),
            new CircleEntity(Id("hole1"), P(2 * scale, 2 * scale), scale),
            new CircleEntity(Id("hole2"), P(7 * scale, 2 * scale), scale / 2));
        var result = CutPathLengthService.Calculate(document);
        result.CutLengthInches.Should().BeApproximately((decimal)(30 + 3 * Math.PI), 0.000001m);
        result.CutLengthReviewReason.Should().BeNull();
    }

    [Fact]
    public void ArcWrapOpenPolylineAndClosedPolygonUseTheirFullLengths()
    {
        var result = CutPathLengthService.Calculate(Document(DrawingUnits.Inches,
            new ArcEntity(Id("arc"), P(0, 0), 2, 270, 90),
            new PolylineEntity(Id("open"), new[] { P(0, 0), P(3, 0), P(3, 4) }),
            new PolygonEntity(Id("square"), P(0, 0), Math.Sqrt(2), 45, 4)));
        result.CutLengthInches.Should().BeApproximately((decimal)(15 + 2 * Math.PI), 0.000001m);
    }

    [Fact]
    public void ConstructionGrainAndPointsAreNotCutPaths()
    {
        var document = Document(DrawingUnits.Inches,
            new LineEntity(Id("cut"), P(0, 0), P(3, 4)),
            new CircleEntity(Id("construction"), P(0, 0), 100, true),
            new LineEntity(Id("grain"), P(0, 0), P(100, 0)),
            new PointEntity(Id("point"), P(500, 500)));
        document = new DrawingDocument(document.Entities, document.Dimensions, document.Constraints,
            document.Metadata with { EntityStyles = new Dictionary<string, DxfEntityStyle> { ["grain"] = new("GRAIN") } });
        CutPathLengthService.Calculate(document).CutLengthInches.Should().Be(5m);
    }

    [Fact]
    public void EllipseApproximationConvergesAndCircularEllipseMatchesCircumference()
    {
        CutPathLengthService.Calculate(Document(DrawingUnits.Inches,
            new EllipseEntity(Id("ellipse"), P(0, 0), P(2, 0), 1)))
            .CutLengthInches.Should().BeApproximately((decimal)(4 * Math.PI), 0.00001m);
    }

    [Fact]
    public void UnsupportedSplineNeverReturnsPartialLineLength()
    {
        var result = CutPathLengthService.Calculate(Document(DrawingUnits.Inches,
            new LineEntity(Id("cut"), P(0, 0), P(10, 0)),
            SplineEntity.FromFitPoints(Id("spline"), new[] { P(0, 0), P(2, 3), P(4, 0) })));
        result.CutLengthInches.Should().BeNull();
        result.CutLengthReviewReason.Should().Contain("Spline");
    }

    [Fact]
    public void InchDefaultDoesNotAllowEmptyOrInvalidGeometry()
    {
        CutPathLengthService.Calculate(Document(DrawingUnits.Unspecified,
            new LineEntity(Id("cut"), P(0, 0), P(10, 0)))).CutLengthInches.Should().Be(10);
        CutPathLengthService.Calculate(Document(DrawingUnits.Inches)).CutLengthInches.Should().BeNull();
        CutPathLengthService.Calculate(Document(DrawingUnits.Inches,
            new CircleEntity(Id("bad"), P(0, 0), double.NaN))).CutLengthInches.Should().BeNull();
        CutPathLengthService.Calculate(Document(DrawingUnits.Inches,
            new ArcEntity(Id("bad"), P(0, 0), 1, 0, 0))).CutLengthInches.Should().BeNull();
    }

    [Fact]
    public void EditedGeometryAndExportRoundTripUseCurrentDimensions()
    {
        var before = Document(DrawingUnits.Inches, new CircleEntity(Id("hole"), P(0, 0), 1));
        var after = Document(DrawingUnits.Inches, new CircleEntity(Id("hole"), P(0, 0), 3));
        var exported = DxfDocumentReader.Read(DxfDocumentWriter.Write(after));
        CutPathLengthService.Calculate(exported).CutLengthInches.Should().BeApproximately(
            CutPathLengthService.Calculate(before).CutLengthInches!.Value * 3, 0.000001m);
    }

    private static DrawingDocument Document(DrawingUnits units, params DrawingEntity[] entities) =>
        new(entities, [], [], DrawingDocumentMetadata.Empty with { Units = units });
    private static EntityId Id(string id) => EntityId.Create(id);
    private static Point2 P(double x, double y) => new(x, y);
}

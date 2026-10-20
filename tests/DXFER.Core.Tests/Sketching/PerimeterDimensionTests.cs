using DXFER.Blazor.Sketching;
using DXFER.CadIO;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class PerimeterDimensionTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CircleQuadrantAndEdgeCreatePerimeterGap(bool polyline, bool reverse)
    {
        DrawingEntity edge = polyline
            ? new PolylineEntity(new("edge"), [new(6, -5), new(6, 5), new(9, 5)])
            : new LineEntity(new("edge"), new(6, -5), new(6, 5));
        var document = new DrawingDocument([new CircleEntity(new("curve"), new(0, 0), 2), edge]);
        var edgeKey = polyline ? "edge|segment|0" : "edge";
        var keys = new[] { "curve|point|quadrant-1|0|2", edgeKey };
        if (reverse) Array.Reverse(keys);
        SketchCommandFactory.TryBuildDimension(document, keys, "gap", out var gap, out var status).Should().BeTrue(status);
        gap.Kind.Should().Be(SketchDimensionKind.PerimeterDistance);
        gap.Value.Should().Be(4, "the quadrant is a perimeter pick, not a point six inches from the edge");
        gap.ReferenceKeys.Should().BeEquivalentTo(["curve", edgeKey]);
    }

    [Theory]
    [InlineData("point", false)]
    [InlineData("point", true)]
    [InlineData("line", false)]
    [InlineData("line", true)]
    [InlineData("circle", false)]
    [InlineData("circle", true)]
    [InlineData("arc", false)]
    [InlineData("arc", true)]
    public void FactoryMeasurementAndNativeRoundtripRetainPerimeterIntent(string kind, bool reverse)
    {
        DrawingEntity peer = kind switch
        {
            "point" => new PointEntity(new("peer"), new(6, 0)),
            "line" => new LineEntity(new("peer"), new(6, -5), new(6, 5)),
            "arc" => new ArcEntity(new("peer"), new(8, 0), 1, 90, 270),
            _ => new CircleEntity(new("peer"), new(8, 0), 1)
        };
        var document = new DrawingDocument([new CircleEntity(new("curve"), new(0, 0), 2), peer]);
        var keys = reverse ? new[] { "peer", "curve" } : new[] { "curve", "peer" };
        SketchCommandFactory.TryBuildDimension(document, keys, "gap", out var gap, out var status).Should().BeTrue(status);
        gap.Kind.Should().Be(SketchDimensionKind.PerimeterDistance);
        gap.Value.Should().Be(kind is "point" or "line" ? 4 : 5);
        SketchDimensionSolverService.IsDimensionSatisfied(document, gap).Should().BeTrue();
        var placed = new DrawingDocument(document.Entities, [gap], []);
        var reopened = DxfDocumentReader.Read(DxfDocumentWriter.Write(placed, new(IncludeNativeDocument: true)));
        reopened.Dimensions.Should().BeEquivalentTo(placed.Dimensions);
        SketchDimensionSolverService.IsDimensionSatisfied(reopened, reopened.Dimensions.Single()).Should().BeTrue();
        document.Dimensions.Should().BeEmpty();
    }

    [Fact]
    public void ArcSupportingCircleIsNotAnInvisibleDimensionTarget()
    {
        var document = new DrawingDocument([
            new ArcEntity(new("arc"), new(0, 0), 2, 90, 270),
            new PointEntity(new("point"), new(6, 0))]);
        SketchCommandFactory.TryBuildDimension(document, ["arc", "point"], "gap", out _, out var status).Should().BeFalse();
        status.Should().Contain("endpoint");
    }

    [Fact]
    public void CenterReferenceRemainsPointAndDoesNotBecomePerimeter()
    {
        var document = new DrawingDocument([
            new CircleEntity(new("first"), new(0, 0), 2),
            new CircleEntity(new("second"), new(8, 0), 1)]);
        SketchCommandFactory.TryBuildDimension(document, ["first:center", "second"], "gap", out var gap, out var status).Should().BeTrue(status);
        gap.Value.Should().Be(7);
        gap.ReferenceKeys.Should().Contain("first:center");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitCenterAndArcEndpointRemainPointToEdgeMeasurements(bool arc)
    {
        DrawingEntity curve = arc ? new ArcEntity(new("curve"), new(0, 0), 2, 90, 270)
            : new CircleEntity(new("curve"), new(0, 0), 2);
        var document = new DrawingDocument([curve, new LineEntity(new("edge"), new(6, -5), new(6, 5))]);
        var point = arc ? "curve|point|start|0|2" : "curve:center";
        SketchCommandFactory.TryBuildDimension(document, [point, "edge"], "gap", out var gap, out var status).Should().BeTrue(status);
        gap.Kind.Should().Be(SketchDimensionKind.PointToLineDistance);
        gap.Value.Should().Be(6);
        gap.ReferenceKeys.Should().Contain(point);
    }
}

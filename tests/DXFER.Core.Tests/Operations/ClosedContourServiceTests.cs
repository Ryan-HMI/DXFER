using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Operations;
using FluentAssertions;

namespace DXFER.Core.Tests.Operations;

public sealed class ClosedContourServiceTests
{
    [Fact]
    public void ShuffledReversedRectangleLinesAndHoleCountTwoContoursNotFiveEntities()
    {
        var rectangle = Rectangle();
        var document = Doc([rectangle[2], rectangle[0], rectangle[3], rectangle[1],
            new CircleEntity(Id("hole"), new(5, 2.5), 1)]);
        ClosedContourService.Calculate(document).Should().Be(new ClosedContourResult(2, null));
    }

    [Fact]
    public void SegmentedArcsAndMixedLineArcLoopsJoinAtEndpoints()
    {
        var arcs = Doc([
            new ArcEntity(Id("a"), new(0, 0), 1, 180, 270),
            new ArcEntity(Id("b"), new(0, 0), 1, 0, 90),
            new ArcEntity(Id("c"), new(0, 0), 1, 270, 360),
            new ArcEntity(Id("d"), new(0, 0), 1, 90, 180)]);
        ClosedContourService.Calculate(arcs).ClosedContourCount.Should().Be(1);
        ClosedContourService.Calculate(Doc([
            new ArcEntity(Id("half"), new(0, 0), 1, 0, 180),
            Line("diameter", -1, 0, 1, 0)])).ClosedContourCount.Should().Be(1);
    }

    [Fact]
    public void ClosedPolylinePolygonFullEllipseAndDisjointCircleCountIndependently()
    {
        var document = Doc([
            new PolylineEntity(Id("poly"), [new(0, 0), new(4, 0), new(4, 4), new(0, 4), new(0, 0)]),
            new PolygonEntity(Id("polygon"), new(10, 0), 1, 0, 5),
            new EllipseEntity(Id("ellipse"), new(20, 0), new(2, 0), 0.5),
            new CircleEntity(Id("circle"), new(30, 0), 1)]);
        ClosedContourService.Calculate(document).ClosedContourCount.Should().Be(4);
    }

    [Theory]
    [InlineData(1, DrawingUnits.Inches)]
    [InlineData(1, DrawingUnits.Millimeters)]
    [InlineData(1, DrawingUnits.Unspecified)]
    public void JoinToleranceIsPhysicalAndDoesNotInventClosureAcrossLargerGaps(double scale, DrawingUnits units)
    {
        var tolerance = ClosedContourService.JoinToleranceInches;
        DrawingDocument WithGap(double gap) => Doc([
            Line("a", 0, 0, scale * 10, 0), Line("b", scale * 10, 0, scale * 10, scale * 5),
            Line("c", scale * 10, scale * 5, 0, scale * 5), Line("d", 0, scale * 5, 0, gap * scale)], units);
        ClosedContourService.Calculate(WithGap(tolerance * 0.5)).ClosedContourCount.Should().Be(1);
        ClosedContourService.Calculate(WithGap(tolerance * 2)).ClosedContourCount.Should().BeNull();
    }

    [Fact]
    public void OpenBranchAndDuplicatePathsRequireReviewWithoutDiscardingPerimeter()
    {
        var rectangle = Rectangle();
        foreach (var entities in new[] { rectangle.Take(3).ToArray(),
            rectangle.Append(Line("branch", 0, 0, -1, -1)).ToArray(),
            rectangle.Append(Line("duplicate", 0, 0, 10, 0)).ToArray() })
        {
            var document = Doc(entities);
            ClosedContourService.Calculate(document).ClosedContourCount.Should().BeNull();
            ClosedContourService.Calculate(document).ContourReviewReason.Should().NotBeNullOrWhiteSpace();
            CutPathLengthService.Calculate(document).CutLengthInches.Should().NotBeNull();
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void DuplicateIntersectingAndTangentCirclesRequireReview(double separation)
    {
        ClosedContourService.Calculate(Doc([
            new CircleEntity(Id("one"), new(0, 0), 1),
            new CircleEntity(Id("two"), new(separation, 0), 1)])).ClosedContourCount.Should().BeNull();
    }

    [Fact]
    public void SelfCrossingAndRetracedClosedPathsRequireReview()
    {
        foreach (var points in new Point2[][] {
            [new(0, 0), new(4, 4), new(0, 4), new(4, 0), new(0, 0)],
            [new(0, 0), new(4, 0), new(2, 0), new(2, 4), new(0, 0)] })
            ClosedContourService.Calculate(Doc([new PolylineEntity(Id("bad"), points)]))
                .ClosedContourCount.Should().BeNull();
    }

    [Fact]
    public void PolylineEndpointsJoinLinesAndEndpointOnAnotherPathInteriorRequiresReview()
    {
        var bottomRight = new PolylineEntity(Id("partial"), [new(0, 0), new(10, 0), new(10, 5)]);
        var document = Doc([bottomRight, Line("top", 0, 5, 10, 5), Line("left", 0, 5, 0, 0)]);
        ClosedContourService.Calculate(document).ClosedContourCount.Should().Be(1);
        var touching = Doc(document.Entities.Append(new PolylineEntity(Id("touching"),
            [new(5, 0), new(4, 2), new(6, 2), new(5, 0)])).ToArray());
        ClosedContourService.Calculate(touching).ClosedContourCount.Should().BeNull();
    }

    [Fact]
    public void NearTangentCurvesAndTinyClearanceRequireReviewButSeparatedCirclesDoNot()
    {
        var delta = ClosedContourService.JoinToleranceInches;
        foreach (var separation in new[] { 2 - delta, 2 + delta })
            ClosedContourService.Calculate(Doc([
                new CircleEntity(Id("a"), new(0, 0), 1),
                new CircleEntity(Id("b"), new(separation, 0), 1)]))
                .ClosedContourCount.Should().BeNull();
        ClosedContourService.Calculate(Doc([
            new CircleEntity(Id("a"), new(0, 0), 1),
            new CircleEntity(Id("b"), new(2.01, 0), 1)]))
            .ClosedContourCount.Should().Be(2);
    }

    [Fact]
    public void AmbiguousEndpointClusterDoesNotDependOnInputOrder()
    {
        var paths = Rectangle().Append(Line("near-branch", 0.000005, 0, -1, 0)).ToArray();
        ClosedContourService.Calculate(Doc(paths)).ClosedContourCount.Should().BeNull();
        ClosedContourService.Calculate(Doc(paths.Reverse().ToArray())).ClosedContourCount.Should().BeNull();
    }

    [Fact]
    public void EmptyUnsupportedImportsAndExcessiveCurveDetailReturnReviewNotZero()
    {
        ClosedContourService.Calculate(Doc([])).ClosedContourCount.Should().BeNull();
        var good = Doc(Rectangle());
        var unsupported = new DrawingDocument(good.Entities, [], [], good.Metadata with {
            UnsupportedEntityCounts = new Dictionary<string, int> { ["INSERT"] = 1 } });
        ClosedContourService.Calculate(unsupported).ClosedContourCount.Should().BeNull();
        ClosedContourService.Calculate(Doc([new CircleEntity(Id("huge"), new(0, 0), 10000)]))
            .ContourReviewReason.Should().Contain("limit");
    }

    [Fact]
    public void InchDefaultPreservesSplinePartialEllipseAndWorkLimitGuards()
    {
        ClosedContourService.Calculate(Doc([SplineEntity.FromFitPoints(Id("s"), [new(0, 0), new(1, 2), new(0, 0)])]))
            .ClosedContourCount.Should().BeNull();
        ClosedContourService.Calculate(Doc([new EllipseEntity(Id("e"), new(0, 0), new(2, 0), 0.5, 0, 180)]))
            .ClosedContourCount.Should().BeNull();
        ClosedContourService.Calculate(Doc(Rectangle(), DrawingUnits.Unspecified)).ClosedContourCount.Should().Be(1);
        ClosedContourService.Calculate(Doc(Enumerable.Range(0, 513)
            .Select(i => (DrawingEntity)new CircleEntity(Id(i.ToString()), new(i * 3, 0), 1)).ToArray()))
            .ContourReviewReason.Should().Contain("limit");
    }

    [Fact]
    public void NonCutEntitiesAreExcludedAndCurrentEditsChangeCount()
    {
        var one = new CircleEntity(Id("cut"), new(0, 0), 1);
        var document = Doc([one, Line("grain", 0, 0, 20, 20), new PointEntity(Id("point"), new(0, 0)),
            new LineEntity(Id("construction"), new(0, 0), new(5, 5), true)]);
        document = new(document.Entities, [], [], document.Metadata with {
            EntityStyles = new Dictionary<string, DxfEntityStyle> { ["grain"] = new("GRAIN") } });
        ClosedContourService.Calculate(document).ClosedContourCount.Should().Be(1);
        var edited = new DrawingDocument(document.Entities.Append(new CircleEntity(Id("second"), new(10, 0), 1)), [], [], document.Metadata);
        ClosedContourService.Calculate(edited).ClosedContourCount.Should().Be(2);
        ClosedContourService.Calculate(document).ClosedContourCount.Should().Be(1);
    }

    private static DrawingEntity[] Rectangle() =>
        [Line("bottom", 0, 0, 10, 0), Line("right", 10, 5, 10, 0),
         Line("top", 10, 5, 0, 5), Line("left", 0, 0, 0, 5)];
    private static LineEntity Line(string id, double x, double y, double x2, double y2) => new(Id(id), new(x, y), new(x2, y2));
    private static EntityId Id(string id) => EntityId.Create(id);
    private static DrawingDocument Doc(DrawingEntity[] entities, DrawingUnits units = DrawingUnits.Inches) =>
        new(entities, [], [], DrawingDocumentMetadata.Empty with { Units = units });
}

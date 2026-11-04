using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class SketchReferenceTests
{
    [Theory]
    [InlineData("line-a:start", "line-a:start", SketchReferenceTarget.Start)]
    [InlineData("line-a:end", "line-a:end", SketchReferenceTarget.End)]
    [InlineData("circle-a:center", "circle-a:center", SketchReferenceTarget.Center)]
    [InlineData("arc-a:center", "arc-a:center", SketchReferenceTarget.Center)]
    [InlineData("line-a", "line-a", SketchReferenceTarget.Entity)]
    [InlineData("circle-a", "circle-a", SketchReferenceTarget.Entity)]
    [InlineData("arc-a", "arc-a", SketchReferenceTarget.Entity)]
    [InlineData("line-a|point|start|0|0", "line-a:start", SketchReferenceTarget.Start)]
    [InlineData("line-a|point|end|10|0", "line-a:end", SketchReferenceTarget.End)]
    [InlineData("circle-a|point|center|20|20", "circle-a:center", SketchReferenceTarget.Center)]
    [InlineData("poly-a|segment|1", "poly-a|segment|1", SketchReferenceTarget.Entity)]
    [InlineData("poly-a|segment|1:start", "poly-a|segment|1:start", SketchReferenceTarget.Start)]
    [InlineData("poly-a|segment|1:end", "poly-a|segment|1:end", SketchReferenceTarget.End)]
    [InlineData("poly-a|point|vertex-0|0|0", "poly-a|segment|0:start", SketchReferenceTarget.Start)]
    [InlineData("poly-a|point|vertex-1|10|0", "poly-a|segment|0:end", SketchReferenceTarget.End)]
    [InlineData("poly-a|point|vertex-2|10|6", "poly-a|segment|1:end", SketchReferenceTarget.End)]
    public void ParserNormalizesSupportedReferences(string key, string normalized, SketchReferenceTarget target)
    {
        var result = SketchReference.TryParse(key, out var reference);

        result.Should().BeTrue();
        reference.EntityId.Should().Be(normalized.Split(new[] { "|segment|", ":" }, StringSplitOptions.None)[0]);
        reference.Target.Should().Be(target);
        reference.ToString().Should().Be(normalized);
    }

    [Fact]
    public void ResolverResolvesSupportedPointsAndWholeEntities()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("line-a"), new Point2(0, 1), new Point2(2, 3)),
            new CircleEntity(EntityId.Create("circle-a"), new Point2(5, 6), 7),
            new ArcEntity(EntityId.Create("arc-a"), new Point2(8, 9), 10, 0, 90),
            new PointEntity(EntityId.Create("point-a"), new Point2(11, 12)),
            new PolylineEntity(
                EntityId.Create("poly-a"),
                new[] { new Point2(20, 20), new Point2(25, 20), new Point2(25, 28) })
        });

        SketchReferenceResolver.TryGetPoint(document, "line-a:start", out var lineStart).Should().BeTrue();
        lineStart.Should().Be(new Point2(0, 1));

        SketchReferenceResolver.TryGetPoint(document, "line-a:end", out var lineEnd).Should().BeTrue();
        lineEnd.Should().Be(new Point2(2, 3));

        SketchReferenceResolver.TryGetPoint(document, "circle-a:center", out var circleCenter).Should().BeTrue();
        circleCenter.Should().Be(new Point2(5, 6));

        SketchReferenceResolver.TryGetPoint(document, "arc-a:center", out var arcCenter).Should().BeTrue();
        arcCenter.Should().Be(new Point2(8, 9));

        SketchReferenceResolver.TryGetPoint(document, "point-a", out var pointEntityLocation).Should().BeTrue();
        pointEntityLocation.Should().Be(new Point2(11, 12));

        SketchReferenceResolver.TryGetEntity(document, "line-a", out var entity).Should().BeTrue();
        entity.Should().BeOfType<LineEntity>();

        SketchReferenceResolver.TryGetPoint(document, "poly-a|segment|1:start", out var segmentStart).Should().BeTrue();
        segmentStart.Should().Be(new Point2(25, 20));

        SketchReferenceResolver.TryGetLine(document, "poly-a|segment|1", out var segmentLine).Should().BeTrue();
        segmentLine.Start.Should().Be(new Point2(25, 20));
        segmentLine.End.Should().Be(new Point2(25, 28));
    }

    [Fact]
    public void ResolverResolvesCanvasSnapPointCoordinates()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("line-a"), new Point2(0, 0), new Point2(10, 0))
        });

        var result = SketchReferenceResolver.TryGetPoint(
            document,
            "line-a|point|mid|5|0",
            out var midpoint);

        result.Should().BeTrue();
        midpoint.Should().Be(new Point2(5, 0));
    }

    [Theory]
    [InlineData("vertex-1", 10, 2)]
    [InlineData("mid-0", 5, 1)]
    public void PolylineCanvasReferenceUsesCurrentGeometry(string label, double x, double y)
    {
        var document = new DrawingDocument(new[] { new PolylineEntity(EntityId.Create("poly"),
            new[] { new Point2(0, 0), new(10, 2), new(10, 6) }) });
        SketchReferenceResolver.TryGetPoint(document, $"poly|point|{label}|999|999", out var point).Should().BeTrue();
        point.Should().Be(new Point2(x, y));
        SketchReferenceResolver.TryGetPoint(document, "poly|point|vertex-99|10|0", out _).Should().BeFalse();
        SketchReferenceResolver.TryGetPoint(document, "poly|point|mid-99|10|0", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PolygonVertexReferencesFollowTranslationRotationAndResize(bool circumscribed)
    {
        var polygon = new PolygonEntity(EntityId.Create("shape"), new(17, -9), 10, 37, 5, circumscribed);
        var edited = polygon with { Center = new(-4, 23), Radius = 7, RotationAngleDegrees = 121 };
        var oldVertices = polygon.GetVertices();
        var document = new DrawingDocument([edited]);
        for (var index = 0; index < oldVertices.Count; index++)
        {
            var key = FormattableString.Invariant($"shape|point|vertex-{index}|{oldVertices[index].X:R}|{oldVertices[index].Y:R}");
            SketchReference.TryNormalize(key, out var canonical).Should().BeTrue();
            foreach (var alias in new[] { key, canonical })
            {
                SketchReferenceResolver.TryGetPoint(document, alias, out var point).Should().BeTrue();
                point.Should().Be(edited.GetVertices()[index]);
                SketchReferenceResolver.TryGetPoint(new DrawingDocument([]), alias, out _).Should().BeFalse();
            }
        }
        foreach (var key in new[] { "shape|point|vertex-99|1|2", "shape|segment|99:end", "shape:start", "shape:end" })
            SketchReferenceResolver.TryGetPoint(document, key, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(0.5, 35, 213)]
    [InlineData(1, 320, 40)]
    [InlineData(2, -75, 142)]
    public void EllipticalEndpointReferencesUseLiveParametersAndDirectedAxes(double ratio, double start, double end)
    {
        var ellipse = new EllipseEntity(EntityId.Create("shape"), new(-4, 23), new(-8, 6), ratio, start, end);
        var document = new DrawingDocument([ellipse]);
        foreach (var label in new[] { "start", "end" })
        foreach (var key in new[] { $"shape:{label}", $"shape|point|{label}|999|-999" })
        {
            SketchReferenceResolver.TryGetPoint(document, key, out var point).Should().BeTrue();
            point.Should().Be(ellipse.PointAtParameterDegrees(label == "start" ? start : end));
            SketchReferenceResolver.TryGetPoint(new DrawingDocument([]), key, out _).Should().BeFalse();
        }
        SketchReferenceResolver.TryGetPoint(document, "shape|segment|0:start", out _).Should().BeFalse();
    }

    [Fact]
    public void InvalidLiveGeometryDoesNotFallBackToPlausibleEndpointCoordinates()
    {
        var polygon = new PolygonEntity(EntityId.Create("shape"), new(17, -9), 10, 37, 5);
        var ellipse = new EllipseEntity(EntityId.Create("shape"), new(17, -9), new(6, 8), 0.5, 30, 210);
        var invalid = new DrawingEntity[]
        {
            polygon with { Radius = 0 }, polygon with { Radius = double.NaN },
            polygon with { Center = new(double.PositiveInfinity, 0) }, polygon with { RotationAngleDegrees = double.NaN },
            ellipse with { MajorAxisEndPoint = new(0, 0) }, ellipse with { MinorRadiusRatio = 0 },
            ellipse with { MinorRadiusRatio = double.NaN }, ellipse with { Center = new(double.PositiveInfinity, 0) },
            ellipse with { StartParameterDegrees = double.NaN }, ellipse with { EndParameterDegrees = double.PositiveInfinity }
        };
        foreach (var source in invalid)
        {
            var label = source is PolygonEntity ? "vertex-1" : "start";
            var key = $"shape|point|{label}|1|2";
            SketchReference.TryNormalize(key, out var canonical).Should().BeTrue();
            foreach (var alias in new[] { key, canonical })
                SketchReferenceResolver.TryGetPoint(new DrawingDocument([source]), alias, out _).Should().BeFalse();
        }
    }
}

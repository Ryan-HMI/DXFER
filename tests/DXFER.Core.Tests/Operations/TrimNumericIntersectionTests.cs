using System.Reflection;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Operations;
using FluentAssertions;

namespace DXFER.Core.Tests.Operations;

public sealed class TrimNumericIntersectionTests
{
    public static TheoryData<double, double> ScalesAndDirections => new()
    {
        { 0.0001, 0 }, { 0.0001, 90 }, { 0.0001, 37 },
        { 0.01, 0 }, { 0.01, 37 }, { 1, 0 }, { 1, 37 },
        { 1000, 0 }, { 1000, 37 }, { 1000000, 0 }, { 1000000, 37 }
    };

    [Theory]
    [MemberData(nameof(ScalesAndDirections))]
    public void SecantsKeepTwoDistinctIntersectionsAcrossScales(double radius, double angle)
    {
        var circle = Circle(radius);
        var line = ChordLine(radius, angle, 0.25);
        var points = Intersections(circle, line);
        points.Should().HaveCount(2);
        foreach (var point in points)
            Distance(point, circle.Center).Should().BeApproximately(radius, radius * 1e-10);
        Distance(points[0], points[1]).Should().BeApproximately(2 * radius * Math.Sqrt(1 - 0.25 * 0.25), radius * 1e-10);
    }

    [Theory]
    [MemberData(nameof(ScalesAndDirections))]
    public void TrueTangenciesStillYieldOneIntersection(double radius, double angle)
    {
        var points = Intersections(Circle(radius), ChordLine(radius, angle, 1));
        points.Should().ContainSingle();
        Distance(points[0], new(0, 0)).Should().BeApproximately(radius, radius * 1e-10);
    }

    [Theory]
    [MemberData(nameof(ScalesAndDirections))]
    public void NearMissesDoNotBecomeTangentCutters(double radius, double angle)
    {
        Intersections(Circle(radius), ChordLine(radius, angle, 1.0001)).Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(ScalesAndDirections))]
    public void NearTangentSecantsDoNotCollapseToOneCut(double radius, double angle)
    {
        var points = Intersections(Circle(radius), ChordLine(radius, angle, 0.9999));
        points.Should().HaveCount(2);
        Distance(points[0], points[1]).Should().BeApproximately(2 * radius * Math.Sqrt(1 - 0.9999 * 0.9999), radius * 1e-9);
    }

    [Fact]
    public void LongLineDoesNotCancelTheSmallCircleChord()
    {
        var points = Intersections(Circle(1), new LineEntity(EntityId.Create("line"), new(-1e8, 0), new(1e8, 0)));
        points.Should().HaveCount(2);
        points[0].X.Should().BeApproximately(-1, 1e-7);
        points[1].X.Should().BeApproximately(1, 1e-7);
    }

    [Fact]
    public void LongObliqueLineCannotWidenTangencyToBridgeAMeaningfulGap()
    {
        var line = new LineEntity(EntityId.Create("line"), new(-1e9, -1e9 + 2.000003), new(1e9, 1e9 + 2.000003));
        Intersections(Circle(Math.Sqrt(2)), line).Should().BeEmpty();
    }

    [Fact]
    public void TranslationAndReversedDirectionPreserveTheCuts()
    {
        var center = new Point2(1000, -2000);
        var circle = new CircleEntity(EntityId.Create("circle"), center, 0.01);
        var line = new LineEntity(EntityId.Create("line"), new(1000, -1999.97), new(1000, -2000.03));
        var points = Intersections(circle, line);
        points.Should().HaveCount(2);
        points[0].Y.Should().BeApproximately(-1999.99, 1e-10);
        points[1].Y.Should().BeApproximately(-2000.01, 1e-10);
    }

    [Fact]
    public void SegmentEndpointsAndInfiniteLineFilteringStayDistinct()
    {
        var circle = Circle(1);
        var diameter = new LineEntity(EntityId.Create("line"), new(-1, 0), new(1, 0));
        Intersections(circle, diameter).Should().Equal(diameter.Start, diameter.End);
        var outside = diameter with { Start = new(2, 0), End = new(3, 0) };
        Intersections(circle, outside).Should().BeEmpty();
        Intersections(circle, outside, requireSegment: false).Should().Equal(new Point2(-1, 0), new Point2(1, 0));
        var endpointTangent = diameter with { Start = new(0, 1), End = new(2, 1) };
        Intersections(circle, endpointTangent).Should().ContainSingle().Which.Should().Be(endpointTangent.Start);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(270)]
    [InlineData(-90)]
    [InlineData(360)]
    public void ArcStartRoundoffIsAcceptedOnlyWhenEndpointsAreAllowed(double start)
    {
        var arc = new ArcEntity(EntityId.Create("arc"), new(0, 0), 2, start + 1e-10, start + 180);
        var point = Polar(2, start);
        SweepParameter(arc, point, true, out var parameter).Should().BeTrue();
        parameter.Should().Be(0);
        SweepParameter(arc, point, false, out _).Should().BeFalse();
    }

    [Fact]
    public void ArcEndpointsInteriorAndRealOutsideAnglesKeepTheirPolicies()
    {
        var arc = new ArcEntity(EntityId.Create("arc"), new(0, 0), 2, 270, 90);
        foreach (var angle in new[] { 270.0, 90.0 })
        {
            SweepParameter(arc, Polar(2, angle), true, out _).Should().BeTrue();
            SweepParameter(arc, Polar(2, angle), false, out _).Should().BeFalse();
        }
        SweepParameter(arc, Polar(2, 0), false, out var interior).Should().BeTrue();
        interior.Should().BeApproximately(90, 1e-10);
        foreach (var angle in new[] { 270 - 1e-4, 90 + 1e-4, 180 })
            SweepParameter(arc, Polar(2, angle), true, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(0.0001, false)]
    [InlineData(0.0001, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    public void PublicTrimKeepsBothOuterStubsAtSemicircleEndpoints(double scale, bool reverse)
    {
        var line = new LineEntity(EntityId.Create("target"), new(0, 0), new(0, 10 * scale));
        if (reverse) line = line with { Start = line.End, End = line.Start };
        var arc = new ArcEntity(EntityId.Create("arc"), new(0, 5 * scale), 2 * scale, 270.00000000000006, 90);
        var document = new DrawingDocument(new DrawingEntity[] { line, arc });
        DrawingModifyService.TryPowerTrimOrExtendLine(document, "target", new(0, 5 * scale),
            name => EntityId.Create(name + "-split"), out var next).Should().BeTrue();
        var fragments = next.Entities.OfType<LineEntity>().OrderBy(e => Math.Min(e.Start.Y, e.End.Y)).ToArray();
        fragments.Should().HaveCount(2);
        Math.Min(fragments[0].Start.Y, fragments[0].End.Y).Should().BeApproximately(0, scale * 1e-9);
        Math.Max(fragments[0].Start.Y, fragments[0].End.Y).Should().BeApproximately(3 * scale, scale * 1e-9);
        Math.Min(fragments[1].Start.Y, fragments[1].End.Y).Should().BeApproximately(7 * scale, scale * 1e-9);
        Math.Max(fragments[1].Start.Y, fragments[1].End.Y).Should().BeApproximately(10 * scale, scale * 1e-9);
        next.Entities.OfType<ArcEntity>().Should().ContainSingle().Which.Should().Be(arc);
    }

    private static CircleEntity Circle(double radius) => new(EntityId.Create("circle"), new(0, 0), radius);

    private static Point2 Polar(double radius, double angle) => new(
        radius * Math.Cos(angle * Math.PI / 180), radius * Math.Sin(angle * Math.PI / 180));

    private static LineEntity ChordLine(double radius, double angle, double normalOffset)
    {
        var unit = Polar(1, angle);
        var center = new Point2(-unit.Y * radius * normalOffset, unit.X * radius * normalOffset);
        return new(EntityId.Create("line"), new(center.X - 3 * radius * unit.X, center.Y - 3 * radius * unit.Y),
            new(center.X + 3 * radius * unit.X, center.Y + 3 * radius * unit.Y));
    }

    private static double Distance(Point2 a, Point2 b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static IReadOnlyList<Point2> Intersections(CircleEntity circle, LineEntity line, bool requireSegment = true)
    {
        object?[] args = [circle, line, null, requireSegment];
        var method = typeof(DrawingModifyService).GetMethod("TryGetLineCircleIntersections", BindingFlags.NonPublic | BindingFlags.Static)!;
        var accepted = (bool)method.Invoke(null, args)!;
        var points = (IReadOnlyList<Point2>)args[2]!;
        accepted.Should().Be(points.Count > 0);
        return points;
    }

    private static bool SweepParameter(ArcEntity arc, Point2 point, bool endpoints, out double parameter)
    {
        object?[] args = [arc, point, endpoints, 0d];
        var method = typeof(DrawingModifyService).GetMethod("TryGetArcSweepParameter", BindingFlags.NonPublic | BindingFlags.Static)!;
        var accepted = (bool)method.Invoke(null, args)!;
        parameter = (double)args[3]!;
        return accepted;
    }
}

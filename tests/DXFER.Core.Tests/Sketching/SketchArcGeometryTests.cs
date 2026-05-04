using DXFER.Blazor.Components;
using DXFER.Core.Geometry;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class SketchArcGeometryTests
{
    [Fact]
    public void ThreePointArcChoosesSweepThroughMiddlePoint()
    {
        var arc = SketchArcGeometry.GetThreePointArc(
            new Point2(1, 0),
            new Point2(0, -1),
            new Point2(-1, 0));

        arc.Should().NotBeNull();
        var actual = arc!.Value;
        actual.Center.X.Should().BeApproximately(0, 0.000001);
        actual.Center.Y.Should().BeApproximately(0, 0.000001);
        actual.Radius.Should().BeApproximately(1, 0.000001);
        actual.StartAngleDegrees.Should().BeApproximately(180, 0.000001);
        actual.EndAngleDegrees.Should().BeApproximately(360, 0.000001);
    }

    [Fact]
    public void CenterPointArcUsesStartRadiusAndEndAngle()
    {
        var arc = SketchArcGeometry.GetCenterPointArc(
            new Point2(0, 0),
            new Point2(2, 0),
            new Point2(0, 5));

        arc.Should().NotBeNull();
        var actual = arc!.Value;
        actual.Center.X.Should().BeApproximately(0, 0.000001);
        actual.Center.Y.Should().BeApproximately(0, 0.000001);
        actual.Radius.Should().BeApproximately(2, 0.000001);
        actual.StartAngleDegrees.Should().BeApproximately(0, 0.000001);
        actual.EndAngleDegrees.Should().BeApproximately(90, 0.000001);
    }

    [Fact]
    public void CenterPointArcChoosesClockwiseShortestVisualSweep()
    {
        var arc = SketchArcGeometry.GetCenterPointArc(
            new Point2(0, 0),
            new Point2(2, 0),
            new Point2(0, -5));

        arc.Should().NotBeNull();
        var actual = arc!.Value;
        actual.Center.X.Should().BeApproximately(0, 0.000001);
        actual.Center.Y.Should().BeApproximately(0, 0.000001);
        actual.Radius.Should().BeApproximately(2, 0.000001);
        actual.StartAngleDegrees.Should().BeApproximately(270, 0.000001);
        actual.EndAngleDegrees.Should().BeApproximately(360, 0.000001);
    }

    [Fact]
    public void CenterPointArcUsesClockwiseShortestVisualSweepPastZero()
    {
        var arc = SketchArcGeometry.GetCenterPointArc(
            new Point2(0, 0),
            new Point2(0, 2),
            new Point2(5, 0));

        arc.Should().NotBeNull();
        var actual = arc!.Value;
        actual.StartAngleDegrees.Should().BeApproximately(0, 0.000001);
        actual.EndAngleDegrees.Should().BeApproximately(90, 0.000001);
    }

    [Fact]
    public void TangentArcUsesStartTangentAndEndpoint()
    {
        var arc = SketchArcGeometry.GetTangentArc(
            new Point2(0, 0),
            new Point2(1, 0),
            new Point2(2, 2));

        arc.Should().NotBeNull();
        var actual = arc!.Value;
        actual.Center.X.Should().BeApproximately(0, 0.000001);
        actual.Center.Y.Should().BeApproximately(2, 0.000001);
        actual.Radius.Should().BeApproximately(2, 0.000001);
        actual.StartAngleDegrees.Should().BeApproximately(270, 0.000001);
        actual.EndAngleDegrees.Should().BeApproximately(360, 0.000001);
    }

    [Fact]
    public void TangentArcRejectsParallelTangentAndChord()
    {
        var arc = SketchArcGeometry.GetTangentArc(
            new Point2(0, 0),
            new Point2(1, 0),
            new Point2(2, 0));

        arc.Should().BeNull();
    }
}

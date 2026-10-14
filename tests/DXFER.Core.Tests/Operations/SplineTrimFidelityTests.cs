using DXFER.CadIO;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Operations;
using FluentAssertions;

namespace DXFER.Core.Tests.Operations;

public sealed class SplineTrimFidelityTests
{
    [Fact]
    public void FluxNegativeYCubicFixtureKeepsExactRightHalfAndOtherGeometry()
    {
        var target = new SplineEntity(EntityId.Create("target"), 3,
            new[] { new Point2(0, -4), new Point2(1, -2), new Point2(3, -6), new Point2(4, -4) },
            new[] { 0d, 0, 0, 0, 1, 1, 1, 1 });
        var cutter = new LineEntity(EntityId.Create("cut"), new Point2(2, -6), new Point2(2, -2));
        var line = new LineEntity(EntityId.Create("line"), new Point2(0, 0), new Point2(1, 0));
        var circle = new CircleEntity(EntityId.Create("circle"), new Point2(4, 0), 1);
        var document = new DrawingDocument(new DrawingEntity[] { target, cutter, line, circle });

        // Canvas hit testing supplies a point on the rendered spline, not raw cursor coordinates.
        Trim(document, target.GetSamplePoints()[5], out var next).Should().BeTrue();

        var result = next.Entities.OfType<SplineEntity>().Single();
        result.Degree.Should().Be(3);
        result.ControlPoints.Should().Equal(new Point2(2, -4), new Point2(2.75, -4.5),
            new Point2(3.5, -5), new Point2(4, -4));
        AssertSameCurve(target, result);
        next.Entities.Skip(1).Should().Equal(cutter, line, circle);
    }

    [Fact]
    public void NativeQaFixtureKeepsExpectedCubicControlPolygon()
    {
        var target = new SplineEntity(EntityId.Create("target"), 3,
            new[] { new Point2(0, 0), new Point2(0, 4), new Point2(4, 4), new Point2(4, 0) },
            new[] { 0d, 0, 0, 0, 1, 1, 1, 1 });
        var cutter = new LineEntity(EntityId.Create("cut"), new Point2(2, -1), new Point2(2, 5));
        var document = new DrawingDocument(new DrawingEntity[] { target, cutter });

        Trim(document, new Point2(0.625, 2.25), out var next).Should().BeTrue();

        var result = (SplineEntity)next.Entities[0];
        result.Degree.Should().Be(3);
        result.ControlPoints.Should().Equal(new Point2(2, 3), new Point2(3, 3), new Point2(4, 2), new Point2(4, 0));
        result.Knots.Should().Equal(0.5, 0.5, 0.5, 0.5, 1, 1, 1, 1);
        next.Entities[1].Should().BeSameAs(cutter);
        AssertSameCurve(target, result);
        var exported = DxfDocumentReader.Read(DxfDocumentWriter.Write(next)).Entities.OfType<SplineEntity>().Single();
        exported.Degree.Should().Be(3);
        exported.ControlPoints.Should().Equal(result.ControlPoints);
        exported.Knots.Should().Equal(result.Knots);
    }

    [Theory]
    [InlineData(0.5, true)]
    [InlineData(0.37, true)]
    [InlineData(0.37, false)]
    public void CubicTrimRetainsExactGeometryAndDxfRepresentation(double cutParameter, bool removeStart)
    {
        var target = Cubic();
        var originalDxf = DxfDocumentWriter.Write(new DrawingDocument(new[] { target.WithConstruction(false) }));
        var cutPoint = Evaluate(target, cutParameter);
        var cutter = Vertical("cut", cutPoint.X);
        var document = new DrawingDocument(new DrawingEntity[] { target, cutter });
        var pick = target.GetSamplePoints()[removeStart ? 3 : 21];

        Trim(document, pick, out var next).Should().BeTrue();

        var result = next.Entities.OfType<SplineEntity>().Should().ContainSingle().Subject;
        result.Degree.Should().Be(3);
        result.ControlPoints.Should().HaveCount(4);
        result.Id.Should().Be(target.Id);
        result.IsConstruction.Should().BeTrue();
        result.FitPoints.Should().BeEmpty();
        AssertDomain(result, removeStart ? cutParameter : 0, removeStart ? 1 : cutParameter);
        AssertSameCurve(target, result);
        next.Entities[1].Should().BeSameAs(cutter);
        DxfDocumentWriter.Write(new DrawingDocument(new[] { target.WithConstruction(false) })).Should().Be(originalDxf);

        var roundTrip = DxfDocumentReader.Read(DxfDocumentWriter.Write(new DrawingDocument(new[] { result.WithConstruction(false) })));
        var exported = roundTrip.Entities.OfType<SplineEntity>().Should().ContainSingle().Subject;
        exported.Degree.Should().Be(3);
        exported.ControlPoints.Should().HaveCount(4);
        AssertSameCurve(target, exported, 1e-8);
    }

    [Theory]
    [InlineData(3.0, 5.123)]
    [InlineData(2.43, 7.0)]
    public void RationalNonuniformRepeatedKnotsPreserveBothSurvivors(double leftParameter, double rightParameter)
    {
        var target = new SplineEntity(EntityId.Create("target"), 3,
            new[] { new Point2(0, 0), new Point2(1, 3), new Point2(2, -2), new Point2(3, 2), new Point2(4, -1), new Point2(5, 3), new Point2(6, 0) },
            new[] { 2d, 2, 2, 2, 3, 3, 7, 11, 11, 11, 11 },
            new[] { 1d, 2, 0.7, 1.2, 1, 0.8, 1 });
        var left = Evaluate(target, leftParameter);
        var right = Evaluate(target, rightParameter);
        var document = new DrawingDocument(new DrawingEntity[] { target, Vertical("left", left.X), Vertical("right", right.X) });
        var pick = target.GetSamplePoints().First(point => point.X > left.X + 0.01 && point.X < right.X - 0.01);

        Trim(document, pick, out var next).Should().BeTrue();

        var results = next.Entities.OfType<SplineEntity>().ToArray();
        results.Should().HaveCount(2);
        results.Select(spline => spline.Id.Value).Should().Equal("target", "spline-split");
        AssertDomain(results[0], 2, leftParameter);
        AssertDomain(results[1], rightParameter, 11);
        foreach (var result in results)
        {
            result.Degree.Should().Be(3);
            result.Weights.Should().Contain(weight => Math.Abs(weight - 1) > 0.01);
            AssertSameCurve(target, result);
        }
    }

    [Fact]
    public void RepeatedTrimsKeepAnExactInteriorSubcurve()
    {
        var target = Cubic();
        var first = new DrawingDocument(new DrawingEntity[] { target, Vertical("cut", Evaluate(target, 0.27).X) });
        Trim(first, target.GetSamplePoints()[2], out var afterFirst).Should().BeTrue();
        var remaining = afterFirst.Entities.OfType<SplineEntity>().Single();
        var second = new DrawingDocument(new DrawingEntity[] { remaining, Vertical("cut", Evaluate(target, 0.73).X) });

        Trim(second, remaining.GetSamplePoints()[^3], out var afterSecond).Should().BeTrue();

        var result = afterSecond.Entities.OfType<SplineEntity>().Single();
        AssertDomain(result, 0.27, 0.73);
        result.ControlPoints.Should().HaveCount(4);
        AssertSameCurve(target, result);
    }

    [Fact]
    public void FitSplineTrimPreservesActualEndpointHandlesAndEveryRetainedSpan()
    {
        var target = SplineEntity.FromFitPoints(EntityId.Create("target"),
            new[] { new Point2(0, 0), new Point2(2, 1), new Point2(4, 0), new Point2(6, 2) },
            startTangentHandle: new Point2(0.3, 1.2), endTangentHandle: new Point2(5.6, 0.8));
        var samples = target.GetSamplePoints();
        var cut = samples[13];
        var document = new DrawingDocument(new DrawingEntity[] { target, Vertical("cut", cut.X) });

        Trim(document, samples[3], out var next).Should().BeTrue();

        var result = next.Entities.OfType<SplineEntity>().Single();
        result.Degree.Should().Be(3);
        result.FitPoints.Should().BeEmpty();
        AssertDomain(result, 13d / 32, 3);
        for (var index = 13; index < samples.Count; index++)
        {
            AssertPoint(samples[index], Evaluate(result, index / 32d), 1e-9);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CurvedExtensionFailsWithoutChangingTheDocument(bool extendStart)
    {
        var target = Cubic();
        var samples = target.GetSamplePoints();
        var endpoint = samples[extendStart ? 0 : ^1];
        var neighbor = samples[extendStart ? 1 : ^2];
        var direction = new Point2(endpoint.X - neighbor.X, endpoint.Y - neighbor.Y);
        var pick = new Point2(endpoint.X + direction.X, endpoint.Y + direction.Y);
        var boundary = new Point2(endpoint.X + 5 * direction.X, endpoint.Y + 5 * direction.Y);
        var cutter = new LineEntity(EntityId.Create("boundary"),
            new Point2(boundary.X - direction.Y, boundary.Y + direction.X),
            new Point2(boundary.X + direction.Y, boundary.Y - direction.X));
        var document = new DrawingDocument(new DrawingEntity[] { target, cutter });

        Trim(document, pick, out var next).Should().BeFalse();

        next.Should().BeSameAs(document);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DegreeOneExtensionPreservesOriginalWeightedParameterization(bool extendStart)
    {
        var target = new SplineEntity(EntityId.Create("target"), 1,
            new[] { new Point2(0, 0), new Point2(2, 1), new Point2(4, 0) },
            new[] { 2d, 2, 3, 7, 7 }, new[] { 1d, 2, 3 });
        var cutter = Vertical("cut", extendStart ? -2 : 6);
        var document = new DrawingDocument(new DrawingEntity[] { target, cutter });

        Trim(document, extendStart ? new Point2(-0.2, -0.1) : new Point2(4.2, -0.1), out var next).Should().BeTrue();

        var result = next.Entities.OfType<SplineEntity>().Single();
        result.Degree.Should().Be(1);
        result.ControlPoints.Should().HaveCount(4);
        for (var index = 0; index <= 1000; index++)
        {
            var parameter = 2 + 5 * index / 1000d;
            AssertPoint(Evaluate(target, parameter), Evaluate(result, parameter), 1e-10);
        }
    }

    [Fact]
    public void PointCutterUsesExactCurveParameterBetweenDisplaySamples()
    {
        var target = Cubic();
        var point = Evaluate(target, 0.371);
        var document = new DrawingDocument(new DrawingEntity[] { target, new PointEntity(EntityId.Create("point"), point) });

        Trim(document, target.GetSamplePoints()[3], out var next).Should().BeTrue();

        var result = next.Entities.OfType<SplineEntity>().Single();
        AssertDomain(result, 0.371, 1);
        AssertSameCurve(target, result);
    }

    [Fact]
    public void CurvedSplineCutterUsesRefinedParametersOnBothCurves()
    {
        var target = Cubic();
        var intersection = Evaluate(target, 0.37);
        var cutterShape = new SplineEntity(EntityId.Create("cutter"), 3,
            new[] { new Point2(-1, -5), new Point2(1, -2), new Point2(-1, 2), new Point2(1, 5) }, target.Knots);
        var offset = Evaluate(cutterShape, 0.41);
        var cutter = new SplineEntity(cutterShape.Id, 3,
            cutterShape.ControlPoints.Select(point => new Point2(point.X + intersection.X - offset.X, point.Y + intersection.Y - offset.Y)), target.Knots);
        var document = new DrawingDocument(new DrawingEntity[] { target, cutter });

        Trim(document, target.GetSamplePoints()[3], out var next).Should().BeTrue();

        var result = (SplineEntity)next.Entities[0];
        AssertDomain(result, 0.37, 1);
        AssertSameCurve(target, result);
        next.Entities[1].Should().BeSameAs(cutter);
    }

    [Fact]
    public void RationalQuadraticTrimAndExportRemainOnTheExactCircle()
    {
        var target = new SplineEntity(EntityId.Create("target"), 2,
            new[] { new Point2(1, 0), new Point2(1, 1), new Point2(0, 1) },
            new[] { 0d, 0, 0, 1, 1, 1 }, new[] { 1d, Math.Sqrt(0.5), 1 });
        var document = new DrawingDocument(new DrawingEntity[] { target, Vertical("cut", Evaluate(target, 0.37).X) });

        Trim(document, target.GetSamplePoints()[3], out var next).Should().BeTrue();

        var result = next.Entities.OfType<SplineEntity>().Single();
        result.Degree.Should().Be(2);
        result.ControlPoints.Should().HaveCount(3);
        AssertDomain(result, 0.37, 1);
        AssertSameCurve(target, result);
        var exported = DxfDocumentReader.Read(DxfDocumentWriter.Write(next)).Entities.OfType<SplineEntity>().Single();
        exported.Degree.Should().Be(2);
        AssertSameCurve(target, exported, 1e-8);
    }

    [Fact]
    public void PickAtCutDoesNotDeleteEitherSide()
    {
        var target = Cubic();
        var document = new DrawingDocument(new DrawingEntity[] { target, Vertical("cut", 2) });
        Trim(document, Evaluate(target, 0.5), out var next).Should().BeFalse();
        next.Should().BeSameAs(document);
    }

    [Fact]
    public void UnsupportedUnclampedTargetFailsUnchanged()
    {
        var target = new SplineEntity(EntityId.Create("target"), 3,
            Cubic().ControlPoints, new[] { -3d, -2, -1, 0, 1, 2, 3, 4 });
        var document = new DrawingDocument(new DrawingEntity[] { target, Vertical("cut", 2) });

        Trim(document, target.GetSamplePoints()[3], out var next).Should().BeFalse();
        next.Should().BeSameAs(document);
    }

    [Fact]
    public void UnsupportedSplineCutterCannotBeSilentlyIgnoredAndDeleteTheTarget()
    {
        var target = Cubic();
        var cutter = new SplineEntity(EntityId.Create("cutter"), 3,
            new[] { new Point2(2, -3), new Point2(2, 0), new Point2(2, 3), new Point2(2, 6) },
            new[] { -3d, -2, -1, 0, 1, 2, 3, 4 });
        var document = new DrawingDocument(new DrawingEntity[] { target, cutter });

        Trim(document, target.GetSamplePoints()[3], out var next).Should().BeFalse();
        next.Should().BeSameAs(document);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidKnotOrderingOrDiscontinuityFailsUnchanged(bool discontinuous)
    {
        var target = discontinuous
            ? new SplineEntity(EntityId.Create("target"), 1,
                new[] { new Point2(0, 0), new Point2(1, 1), new Point2(2, 1), new Point2(3, 0) },
                new[] { 0d, 0, 0.5, 0.5, 1, 1 })
            : new SplineEntity(EntityId.Create("target"), 2,
                new[] { new Point2(0, 0), new Point2(1, 1), new Point2(2, 1), new Point2(3, 0) },
                new[] { 0d, 0, 0, 0.7, 1, 0.9, 1 });
        var document = new DrawingDocument(new DrawingEntity[] { target, Vertical("cut", 2) });

        Trim(document, new Point2(0.5, 0.5), out var next).Should().BeFalse();
        next.Should().BeSameAs(document);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void InvalidWeightsFailUnchanged(double weight)
    {
        var target = new SplineEntity(EntityId.Create("target"), 3, Cubic().ControlPoints, Cubic().Knots,
            new[] { 1, weight, 1, 1 });
        var document = new DrawingDocument(new DrawingEntity[] { target, Vertical("cut", 2) });

        Trim(document, new Point2(0.5, 0.5), out var next).Should().BeFalse();
        next.Should().BeSameAs(document);
    }

    private static SplineEntity Cubic() => new(EntityId.Create("target"), 3,
        new[] { new Point2(0, 0), new Point2(1, 2), new Point2(3, 2), new Point2(4, 0) },
        new[] { 0d, 0, 0, 0, 1, 1, 1, 1 }, isConstruction: true);

    private static LineEntity Vertical(string id, double x) => new(EntityId.Create(id), new Point2(x, -20), new Point2(x, 20));

    private static bool Trim(DrawingDocument document, Point2 pick, out DrawingDocument next) =>
        DrawingModifyService.TryPowerTrimOrExtendLine(document, "target", pick, prefix => EntityId.Create($"{prefix}-split"), out next);

    private static void AssertDomain(SplineEntity spline, double start, double end)
    {
        spline.Knots[spline.Degree].Should().BeApproximately(start, 1e-7);
        spline.Knots[spline.ControlPoints.Count].Should().BeApproximately(end, 1e-7);
    }

    private static void AssertSameCurve(SplineEntity original, SplineEntity result, double tolerance = 1e-9)
    {
        var start = result.Knots[result.Degree];
        var end = result.Knots[result.ControlPoints.Count];
        for (var index = 0; index <= 1000; index++)
        {
            var parameter = start + (end - start) * index / 1000;
            AssertPoint(Evaluate(original, parameter), Evaluate(result, parameter), tolerance);
        }
    }

    private static void AssertPoint(Point2 expected, Point2 actual, double tolerance)
    {
        actual.X.Should().BeApproximately(expected.X, tolerance);
        actual.Y.Should().BeApproximately(expected.Y, tolerance);
    }

    // Independent Cox-de Boor basis oracle, not the production evaluator or its display samples.
    private static Point2 Evaluate(SplineEntity spline, double parameter)
    {
        if (parameter >= spline.Knots[spline.ControlPoints.Count]) return spline.ControlPoints[^1];
        double Basis(int index, int degree)
        {
            var knots = spline.Knots;
            if (degree == 0) return parameter >= knots[index] && parameter < knots[index + 1] ? 1 : 0;
            var leftWidth = knots[index + degree] - knots[index];
            var rightWidth = knots[index + degree + 1] - knots[index + 1];
            return (leftWidth == 0 ? 0 : (parameter - knots[index]) / leftWidth * Basis(index, degree - 1))
                + (rightWidth == 0 ? 0 : (knots[index + degree + 1] - parameter) / rightWidth * Basis(index + 1, degree - 1));
        }

        double x = 0, y = 0, weight = 0;
        for (var index = 0; index < spline.ControlPoints.Count; index++)
        {
            var basis = Basis(index, spline.Degree) * spline.Weights[index];
            x += spline.ControlPoints[index].X * basis;
            y += spline.ControlPoints[index].Y * basis;
            weight += basis;
        }

        return new Point2(x / weight, y / weight);
    }
}

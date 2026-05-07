using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Operations;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Operations;

public sealed class DrawingModifyServiceTests
{
    [Fact]
    public void TranslateSelectedMovesOnlySelectedEntities()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("selected"), new Point2(0, 0), new Point2(2, 0)),
            new LineEntity(EntityId.Create("other"), new Point2(10, 0), new Point2(12, 0))
        });

        var next = DrawingModifyService.TranslateSelected(
            document,
            new[] { "selected" },
            new Point2(1, 1),
            new Point2(4, 5));

        next.Entities[0].Should().Be(new LineEntity(EntityId.Create("selected"), new Point2(3, 4), new Point2(5, 4)));
        next.Entities[1].Should().Be(document.Entities[1]);
    }

    [Fact]
    public void OffsetSelectedLineCreatesParallelCopyThroughPickedSide()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("edge"), new Point2(0, 0), new Point2(10, 0))
        });

        var offset = DrawingModifyService.TryOffsetSelected(
            document,
            new[] { "edge" },
            new Point2(2, 3),
            prefix => EntityId.Create($"{prefix}-copy"),
            out var next,
            out var createdCount);

        offset.Should().BeTrue();
        createdCount.Should().Be(1);
        next.Entities[1].Should().Be(new LineEntity(EntityId.Create("line-copy"), new Point2(0, 3), new Point2(10, 3)));
    }

    [Fact]
    public void OffsetSelectedSplineCreatesSplineCopyThroughPickedSide()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new SplineEntity(
                EntityId.Create("curve"),
                1,
                new[] { new Point2(0, 0), new Point2(10, 0) },
                Array.Empty<double>())
        });

        var offset = DrawingModifyService.TryOffsetSelected(
            document,
            new[] { "curve" },
            new Point2(2, 3),
            prefix => EntityId.Create($"{prefix}-copy"),
            out var next,
            out var createdCount);

        offset.Should().BeTrue();
        createdCount.Should().Be(1);
        var spline = next.Entities[1].Should().BeOfType<SplineEntity>().Subject;
        spline.Id.Should().Be(EntityId.Create("spline-copy"));
        spline.ControlPoints.Should().Equal(new Point2(0, 3), new Point2(10, 3));
    }

    [Fact]
    public void LinearPatternAddsTranslatedCopies()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new PointEntity(EntityId.Create("point"), new Point2(1, 2))
        });
        var index = 0;

        var patterned = DrawingModifyService.TryLinearPatternSelected(
            document,
            new[] { "point" },
            new Point2(0, 0),
            new Point2(5, 0),
            3,
            prefix => EntityId.Create($"{prefix}-{++index}"),
            out var next,
            out var createdCount);

        patterned.Should().BeTrue();
        createdCount.Should().Be(2);
        next.Entities[1].Should().Be(new PointEntity(EntityId.Create("point-1"), new Point2(6, 2)));
        next.Entities[2].Should().Be(new PointEntity(EntityId.Create("point-2"), new Point2(11, 2)));
    }

    [Fact]
    public void ChamferSelectedLinesTrimsCornerAndAddsBridge()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("horizontal"), new Point2(0, 0), new Point2(10, 0)),
            new LineEntity(EntityId.Create("vertical"), new Point2(0, 0), new Point2(0, 10))
        });

        var chamfered = DrawingModifyService.TryChamferSelectedLines(
            document,
            new[] { "horizontal", "vertical" },
            2,
            out var next);

        chamfered.Should().BeTrue();
        next.Entities.Should().HaveCount(3);
        next.Entities[0].Should().Be(new LineEntity(EntityId.Create("horizontal"), new Point2(2, 0), new Point2(10, 0)));
        next.Entities[1].Should().Be(new LineEntity(EntityId.Create("vertical"), new Point2(0, 2), new Point2(0, 10)));
        next.Entities[2].Should().BeOfType<LineEntity>()
            .Which.Start.Should().Be(new Point2(2, 0));
    }

    [Fact]
    public void PowerTrimRemovesPickedMiddleSpanBetweenCutters()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("target"), new Point2(0, 0), new Point2(10, 0)),
            new LineEntity(EntityId.Create("left"), new Point2(3, -1), new Point2(3, 1)),
            new LineEntity(EntityId.Create("right"), new Point2(7, -1), new Point2(7, 1))
        });

        var trimmed = DrawingModifyService.TryPowerTrimOrExtendLine(
            document,
            "target",
            new Point2(5, 0),
            prefix => EntityId.Create($"{prefix}-split"),
            out var next);

        trimmed.Should().BeTrue();
        next.Entities.Should().HaveCount(4);
        next.Entities[0].Should().Be(new LineEntity(EntityId.Create("target"), new Point2(0, 0), new Point2(3, 0)));
        next.Entities[1].Should().Be(new LineEntity(EntityId.Create("line-split"), new Point2(7, 0), new Point2(10, 0)));
    }

    [Fact]
    public void PowerTrimDeletesPickedLineWhenNoCuttersExist()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("target"), new Point2(0, 0), new Point2(10, 0)),
            new CircleEntity(EntityId.Create("other"), new Point2(20, 0), 2)
        });

        var trimmed = DrawingModifyService.TryPowerTrimOrExtendLine(
            document,
            "target",
            new Point2(5, 0),
            prefix => EntityId.Create($"{prefix}-split"),
            out var next);

        trimmed.Should().BeTrue();
        next.Entities.Should().ContainSingle()
            .Which.Should().Be(new CircleEntity(EntityId.Create("other"), new Point2(20, 0), 2));
    }

    [Fact]
    public void PowerTrimCircleRemovesPickedSpanBetweenLineCutters()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new CircleEntity(EntityId.Create("target"), new Point2(0, 0), 5),
            new LineEntity(EntityId.Create("horizontal"), new Point2(-10, 0), new Point2(10, 0)),
            new LineEntity(EntityId.Create("vertical"), new Point2(0, -10), new Point2(0, 10))
        });

        var trimmed = DrawingModifyService.TryPowerTrimOrExtendLine(
            document,
            "target",
            new Point2(3, 3),
            prefix => EntityId.Create($"{prefix}-split"),
            out var next);

        trimmed.Should().BeTrue();
        next.Entities.Should().HaveCount(3);
        next.Entities[0].Should().BeOfType<ArcEntity>()
            .Which.Should().Match<ArcEntity>(arc =>
                arc.Id.Value == "target"
                && arc.Center == new Point2(0, 0)
                && Math.Abs(arc.Radius - 5) <= 0.000001
                && Math.Abs(arc.StartAngleDegrees - 90) <= 0.000001
                && Math.Abs(arc.EndAngleDegrees - 360) <= 0.000001);
    }

    [Theory]
    [MemberData(nameof(ApplicableCircleTargetCutters))]
    public void PowerTrimCircleUsesApplicableEntityTypesAsCutters(DrawingEntity cutter)
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new CircleEntity(EntityId.Create("target"), new Point2(0, 0), 5),
            cutter
        });

        var trimmed = DrawingModifyService.TryPowerTrimOrExtendLine(
            document,
            "target",
            new Point2(0, 5),
            prefix => EntityId.Create($"{prefix}-split"),
            out var next);

        trimmed.Should().BeTrue(cutter.Kind);
        next.Entities.Should().HaveCount(2);
        next.Entities[0].Should().BeOfType<ArcEntity>();
        next.Entities[1].Should().Be(cutter);
    }

    [Fact]
    public void PowerTrimCircleUsesPointEntitiesAsCutters()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new CircleEntity(EntityId.Create("target"), new Point2(0, 0), 5),
            new PointEntity(EntityId.Create("left-point"), new Point2(-5, 0)),
            new PointEntity(EntityId.Create("right-point"), new Point2(5, 0))
        });

        var trimmed = DrawingModifyService.TryPowerTrimOrExtendLine(
            document,
            "target",
            new Point2(0, 5),
            prefix => EntityId.Create($"{prefix}-split"),
            out var next);

        trimmed.Should().BeTrue();
        next.Entities.Should().HaveCount(3);
        AssertArc(next.Entities[0], "target", new Point2(0, 0), 5, 180, 360);
        next.Entities[1].Should().Be(new PointEntity(EntityId.Create("left-point"), new Point2(-5, 0)));
        next.Entities[2].Should().Be(new PointEntity(EntityId.Create("right-point"), new Point2(5, 0)));
    }

    [Fact]
    public void PowerTrimCircleUsesTangentArcTouchAsCutter()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new CircleEntity(EntityId.Create("target"), new Point2(0, 0), 5),
            new ArcEntity(EntityId.Create("tangent-cutter"), new Point2(0, 10), 5, 180, 360),
            new LineEntity(EntityId.Create("line-cutter"), new Point2(-2.5, -10), new Point2(-2.5, 10))
        });

        var trimmed = DrawingModifyService.TryPowerTrimOrExtendLine(
            document,
            "target",
            PointOnCircle(new Point2(0, 0), 5, 105),
            prefix => EntityId.Create($"{prefix}-split"),
            out var next);

        trimmed.Should().BeTrue();
        next.Entities.Should().HaveCount(3);
        AssertArc(next.Entities[0], "target", new Point2(0, 0), 5, 120, 450);
        next.Entities[1].Should().Be(new ArcEntity(EntityId.Create("tangent-cutter"), new Point2(0, 10), 5, 180, 360));
        next.Entities[2].Should().Be(new LineEntity(EntityId.Create("line-cutter"), new Point2(-2.5, -10), new Point2(-2.5, 10)));
    }

    [Fact]
    public void PowerTrimCircleUsesRefinedSplineCutterIntersections()
    {
        var cutterPoints = new[]
        {
            new Point2(-6, 1),
            new Point2(-2, 3),
            new Point2(2, 3),
            new Point2(6, 1)
        };
        var expectedAngles = FindCatmullRomCircleCrossings(cutterPoints, new Point2(0, 0), 5)
            .Select(point => NormalizeTestAngleDegrees(Math.Atan2(point.Y, point.X) * 180.0 / Math.PI))
            .OrderBy(angle => angle)
            .ToArray();
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new CircleEntity(EntityId.Create("target"), new Point2(0, 0), 5),
            SplineEntity.FromFitPoints(EntityId.Create("spline-cutter"), cutterPoints)
        });

        var trimmed = DrawingModifyService.TryPowerTrimOrExtendLine(
            document,
            "target",
            new Point2(0, 5),
            prefix => EntityId.Create($"{prefix}-split"),
            out var next);

        trimmed.Should().BeTrue();
        expectedAngles.Should().HaveCount(2);
        var arc = next.Entities[0].Should().BeOfType<ArcEntity>().Subject;
        arc.StartAngleDegrees.Should().BeApproximately(expectedAngles[1], 0.000001);
        arc.EndAngleDegrees.Should().BeApproximately(expectedAngles[0] + 360, 0.000001);
    }

    [Fact]
    public void PowerTrimLineUsesCircleIntersectionsAsCutters()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("target"), new Point2(-10, 0), new Point2(10, 0)),
            new CircleEntity(EntityId.Create("cutter"), new Point2(0, 0), 5)
        });

        var trimmed = DrawingModifyService.TryPowerTrimOrExtendLine(
            document,
            "target",
            new Point2(0, 0),
            prefix => EntityId.Create($"{prefix}-split"),
            out var next);

        trimmed.Should().BeTrue();
        next.Entities.Should().HaveCount(3);
        next.Entities[0].Should().Be(new LineEntity(EntityId.Create("target"), new Point2(-10, 0), new Point2(-5, 0)));
        next.Entities[1].Should().Be(new LineEntity(EntityId.Create("line-split"), new Point2(5, 0), new Point2(10, 0)));
        next.Entities[2].Should().Be(new CircleEntity(EntityId.Create("cutter"), new Point2(0, 0), 5));
    }

    [Fact]
    public void PowerTrimLineUsesRefinedSplineCutterIntersections()
    {
        var leftCutterPoints = new[]
        {
            new Point2(-6, 3),
            new Point2(-4, 1),
            new Point2(-2, -3)
        };
        var rightCutterPoints = new[]
        {
            new Point2(2, -3),
            new Point2(4, 1),
            new Point2(6, 3)
        };
        var expectedLeft = FindCatmullRomHorizontalCrossing(leftCutterPoints, 1);
        var expectedRight = FindCatmullRomHorizontalCrossing(rightCutterPoints, 0);
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("target"), new Point2(-10, 0), new Point2(10, 0)),
            SplineEntity.FromFitPoints(EntityId.Create("left-cutter"), leftCutterPoints),
            SplineEntity.FromFitPoints(EntityId.Create("right-cutter"), rightCutterPoints)
        });

        var trimmed = DrawingModifyService.TryPowerTrimOrExtendLine(
            document,
            "target",
            new Point2(0, 0),
            prefix => EntityId.Create($"{prefix}-split"),
            out var next);

        trimmed.Should().BeTrue();
        next.Entities.Should().HaveCount(4);
        next.Entities.OfType<LineEntity>().Should().Contain(line =>
            line.Id.Value == "target"
            && Math.Abs(line.Start.X + 10) <= 0.000001
            && Math.Abs(line.Start.Y) <= 0.000001
            && Math.Abs(line.End.X - expectedLeft.X) <= 0.000001
            && Math.Abs(line.End.Y - expectedLeft.Y) <= 0.000001);
        next.Entities.OfType<LineEntity>().Should().Contain(line =>
            line.Id.Value == "line-split"
            && Math.Abs(line.Start.X - expectedRight.X) <= 0.000001
            && Math.Abs(line.Start.Y - expectedRight.Y) <= 0.000001
            && Math.Abs(line.End.X - 10) <= 0.000001
            && Math.Abs(line.End.Y) <= 0.000001);
    }

    [Fact]
    public void PowerTrimEllipseRemovesPickedSpanBetweenLineCutters()
    {
        var target = new EllipseEntity(EntityId.Create("target"), new Point2(0, 0), new Point2(5, 0), 0.5);
        var document = new DrawingDocument(new DrawingEntity[]
        {
            target,
            new LineEntity(EntityId.Create("horizontal"), new Point2(-10, 0), new Point2(10, 0)),
            new LineEntity(EntityId.Create("vertical"), new Point2(0, -10), new Point2(0, 10))
        });

        var trimmed = DrawingModifyService.TryPowerTrimOrExtendLine(
            document,
            "target",
            target.PointAtParameterDegrees(45),
            prefix => EntityId.Create($"{prefix}-split"),
            out var next);

        trimmed.Should().BeTrue();
        next.Entities.Should().HaveCount(3);
        next.Entities[0].Should().BeOfType<EllipseEntity>()
            .Which.Should().Match<EllipseEntity>(ellipse =>
                ellipse.Id.Value == "target"
                && ellipse.Center == new Point2(0, 0)
                && ellipse.MajorAxisEndPoint == new Point2(5, 0)
                && Math.Abs(ellipse.MinorRadiusRatio - 0.5) <= 0.000001
                && Math.Abs(ellipse.StartParameterDegrees - 90) <= 0.000001
                && Math.Abs(ellipse.EndParameterDegrees - 360) <= 0.000001);
    }

    [Theory]
    [MemberData(nameof(ApplicableEllipseTargetCutters))]
    public void PowerTrimEllipseUsesApplicableEntityTypesAsCutters(DrawingEntity cutter)
    {
        var target = new EllipseEntity(EntityId.Create("target"), new Point2(0, 0), new Point2(5, 0), 0.5);
        var document = new DrawingDocument(new DrawingEntity[]
        {
            target,
            cutter
        });

        var trimmed = DrawingModifyService.TryPowerTrimOrExtendLine(
            document,
            "target",
            target.PointAtParameterDegrees(90),
            prefix => EntityId.Create($"{prefix}-split"),
            out var next);

        trimmed.Should().BeTrue(cutter.Kind);
        next.Entities.Should().HaveCount(3);
        var firstEllipse = next.Entities[0].Should().BeOfType<EllipseEntity>().Subject;
        firstEllipse.Id.Should().Be(EntityId.Create("target"));
        firstEllipse.Center.Should().Be(new Point2(0, 0));
        firstEllipse.MajorAxisEndPoint.Should().Be(new Point2(5, 0));
        firstEllipse.MinorRadiusRatio.Should().BeApproximately(0.5, 0.000001);
        firstEllipse.StartParameterDegrees.Should().BeApproximately(0, 0.000001);
        firstEllipse.EndParameterDegrees.Should().BeLessThan(90);
        next.Entities[1].Should().Be(cutter);
        var splitEllipse = next.Entities[2].Should().BeOfType<EllipseEntity>().Subject;
        splitEllipse.Id.Should().Be(EntityId.Create("ellipse-split"));
        splitEllipse.StartParameterDegrees.Should().BeGreaterThan(90);
        splitEllipse.EndParameterDegrees.Should().BeApproximately(360, 0.000001);
    }

    [Fact]
    public void PowerTrimEllipseUsesPointEntitiesAsCutters()
    {
        var target = new EllipseEntity(EntityId.Create("target"), new Point2(0, 0), new Point2(5, 0), 0.5);
        var document = new DrawingDocument(new DrawingEntity[]
        {
            target,
            new PointEntity(EntityId.Create("left-point"), target.PointAtParameterDegrees(120)),
            new PointEntity(EntityId.Create("right-point"), target.PointAtParameterDegrees(60))
        });

        var trimmed = DrawingModifyService.TryPowerTrimOrExtendLine(
            document,
            "target",
            target.PointAtParameterDegrees(90),
            prefix => EntityId.Create($"{prefix}-split"),
            out var next);

        trimmed.Should().BeTrue();
        next.Entities.Should().HaveCount(4);
        next.Entities[0].Should().BeOfType<EllipseEntity>()
            .Which.EndParameterDegrees.Should().BeApproximately(60, 0.000001);
        next.Entities[1].Should().Be(new PointEntity(EntityId.Create("left-point"), target.PointAtParameterDegrees(120)));
        next.Entities[2].Should().Be(new PointEntity(EntityId.Create("right-point"), target.PointAtParameterDegrees(60)));
        next.Entities[3].Should().BeOfType<EllipseEntity>()
            .Which.StartParameterDegrees.Should().BeApproximately(120, 0.000001);
    }

    [Fact]
    public void PowerTrimEllipseAcceptsNearEdgeCanvasPick()
    {
        var target = new EllipseEntity(EntityId.Create("target"), new Point2(0, 0), new Point2(5, 0), 0.5);
        var document = new DrawingDocument(new DrawingEntity[]
        {
            target,
            new LineEntity(EntityId.Create("horizontal"), new Point2(-10, 0), new Point2(10, 0)),
            new LineEntity(EntityId.Create("vertical"), new Point2(0, -10), new Point2(0, 10))
        });
        var nearTopPick = new Point2(0.08, 2.55);

        var trimmed = DrawingModifyService.TryPowerTrimOrExtendLine(
            document,
            "target",
            nearTopPick,
            prefix => EntityId.Create($"{prefix}-split"),
            out var next);

        trimmed.Should().BeTrue();
        next.Entities.Should().HaveCount(3);
        next.Entities[0].Should().BeOfType<EllipseEntity>()
            .Which.StartParameterDegrees.Should().BeApproximately(90, 0.000001);
    }

    [Fact]
    public void PowerTrimEllipseUsesRefinedSplineCutterIntersections()
    {
        var target = new EllipseEntity(EntityId.Create("target"), new Point2(0, 0), new Point2(5, 0), 0.5);
        var cutterPoints = new[]
        {
            new Point2(-6, 0.5),
            new Point2(-2, 2),
            new Point2(2, 2),
            new Point2(6, 0.5)
        };
        var expectedParameters = FindCatmullRomEllipseCrossings(cutterPoints, target)
            .Select(point => GetAxisAlignedEllipseParameterDegrees(point, majorRadius: 5, minorRadius: 2.5))
            .OrderBy(parameter => parameter)
            .ToArray();
        var document = new DrawingDocument(new DrawingEntity[]
        {
            target,
            SplineEntity.FromFitPoints(EntityId.Create("spline-cutter"), cutterPoints)
        });

        var trimmed = DrawingModifyService.TryPowerTrimOrExtendLine(
            document,
            "target",
            target.PointAtParameterDegrees(90),
            prefix => EntityId.Create($"{prefix}-split"),
            out var next);

        trimmed.Should().BeTrue();
        expectedParameters.Should().HaveCount(2);
        var firstEllipse = next.Entities[0].Should().BeOfType<EllipseEntity>().Subject;
        firstEllipse.StartParameterDegrees.Should().BeApproximately(0, 0.000001);
        firstEllipse.EndParameterDegrees.Should().BeApproximately(expectedParameters[0], 0.000001);
        var splitEllipse = next.Entities[2].Should().BeOfType<EllipseEntity>().Subject;
        splitEllipse.StartParameterDegrees.Should().BeApproximately(expectedParameters[1], 0.000001);
        splitEllipse.EndParameterDegrees.Should().BeApproximately(360, 0.000001);
    }

    [Fact]
    public void PowerTrimSplineRemovesPickedSpanBetweenLineCutters()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new SplineEntity(
                EntityId.Create("target"),
                1,
                new[] { new Point2(-10, 0), new Point2(10, 0) },
                Array.Empty<double>()),
            new LineEntity(EntityId.Create("left"), new Point2(-5, -2), new Point2(-5, 2)),
            new LineEntity(EntityId.Create("right"), new Point2(5, -2), new Point2(5, 2))
        });

        var trimmed = DrawingModifyService.TryPowerTrimOrExtendLine(
            document,
            "target",
            new Point2(0, 0),
            prefix => EntityId.Create($"{prefix}-split"),
            out var next);

        trimmed.Should().BeTrue();
        next.Entities.Should().HaveCount(4);
        AssertSpline(next.Entities[0], "target", new Point2(-10, 0), new Point2(-5, 0));
        next.Entities[1].Should().Be(new LineEntity(EntityId.Create("left"), new Point2(-5, -2), new Point2(-5, 2)));
        next.Entities[2].Should().Be(new LineEntity(EntityId.Create("right"), new Point2(5, -2), new Point2(5, 2)));
        AssertSpline(next.Entities[3], "spline-split", new Point2(5, 0), new Point2(10, 0));
    }

    [Fact]
    public void PowerTrimSplinePreservesSampledPathInsteadOfReinterpolatingTrimmedSpan()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            SplineEntity.FromFitPoints(
                EntityId.Create("target"),
                new[]
                {
                    new Point2(-10, 0),
                    new Point2(-5, 4),
                    new Point2(0, 0),
                    new Point2(5, -4),
                    new Point2(10, 0)
                }),
            new LineEntity(EntityId.Create("left"), new Point2(-6, -10), new Point2(-6, 10)),
            new LineEntity(EntityId.Create("right"), new Point2(6, -10), new Point2(6, 10))
        });

        var trimmed = DrawingModifyService.TryPowerTrimOrExtendLine(
            document,
            "target",
            new Point2(0, 0),
            prefix => EntityId.Create($"{prefix}-split"),
            out var next);

        trimmed.Should().BeTrue();
        var firstSpline = next.Entities[0].Should().BeOfType<SplineEntity>().Subject;
        var splitSpline = next.Entities[3].Should().BeOfType<SplineEntity>().Subject;
        firstSpline.Degree.Should().Be(1);
        firstSpline.FitPoints.Should().BeEmpty();
        splitSpline.Degree.Should().Be(1);
        splitSpline.FitPoints.Should().BeEmpty();
    }

    [Fact]
    public void PowerTrimSplineTargetUsesRefinedCircleCutterIntersections()
    {
        var fitPoints = new[]
        {
            new Point2(-10, 0),
            new Point2(-5, 4),
            new Point2(0, 0),
            new Point2(5, -4),
            new Point2(10, 0)
        };
        var expectedIntersections = FindCatmullRomCircleCrossings(fitPoints, new Point2(0, 0), 5)
            .OrderBy(point => point.X)
            .ToArray();
        var document = new DrawingDocument(new DrawingEntity[]
        {
            SplineEntity.FromFitPoints(EntityId.Create("target"), fitPoints),
            new CircleEntity(EntityId.Create("circle-cutter"), new Point2(0, 0), 5)
        });

        var trimmed = DrawingModifyService.TryPowerTrimOrExtendLine(
            document,
            "target",
            new Point2(0, 0),
            prefix => EntityId.Create($"{prefix}-split"),
            out var next);

        trimmed.Should().BeTrue();
        expectedIntersections.Should().HaveCount(2);
        AssertSpline(next.Entities[0], "target", new Point2(-10, 0), expectedIntersections[0]);
        AssertSpline(next.Entities[2], "spline-split", expectedIntersections[1], new Point2(10, 0));
    }

    [Fact]
    public void PowerTrimSplineTargetUsesRefinedLineCutterIntersections()
    {
        var fitPoints = new[]
        {
            new Point2(-10, 0),
            new Point2(-5, 4),
            new Point2(0, 0),
            new Point2(5, -4),
            new Point2(10, 0)
        };
        var expectedLeft = FindCatmullRomVerticalCrossing(fitPoints, 1, -4);
        var expectedRight = FindCatmullRomVerticalCrossing(fitPoints, 2, 4);
        var document = new DrawingDocument(new DrawingEntity[]
        {
            SplineEntity.FromFitPoints(EntityId.Create("target"), fitPoints),
            new LineEntity(EntityId.Create("left-cutter"), new Point2(-4, -10), new Point2(-4, 10)),
            new LineEntity(EntityId.Create("right-cutter"), new Point2(4, -10), new Point2(4, 10))
        });

        var trimmed = DrawingModifyService.TryPowerTrimOrExtendLine(
            document,
            "target",
            new Point2(0, 0),
            prefix => EntityId.Create($"{prefix}-split"),
            out var next);

        trimmed.Should().BeTrue();
        AssertSpline(next.Entities[0], "target", new Point2(-10, 0), expectedLeft);
        AssertSpline(next.Entities[3], "spline-split", expectedRight, new Point2(10, 0));
    }

    [Fact]
    public void PowerTrimLineTargetUsesRefinedControlPointSplineCutterIntersections()
    {
        const double crossingParameter = 0.37;
        var leftControls = new[]
        {
            new Point2(-8, -crossingParameter),
            new Point2(-7, (1.0 / 3.0) - crossingParameter),
            new Point2(-2, (2.0 / 3.0) - crossingParameter),
            new Point2(-1, 1 - crossingParameter)
        };
        var rightControls = new[]
        {
            new Point2(1, -crossingParameter),
            new Point2(2, (1.0 / 3.0) - crossingParameter),
            new Point2(7, (2.0 / 3.0) - crossingParameter),
            new Point2(8, 1 - crossingParameter)
        };
        var expectedLeft = EvaluateCubicBezier(leftControls, crossingParameter);
        var expectedRight = EvaluateCubicBezier(rightControls, crossingParameter);
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("target"), new Point2(-10, 0), new Point2(10, 0)),
            CreateBezierSpline("left-spline-cutter", leftControls),
            CreateBezierSpline("right-spline-cutter", rightControls)
        });

        var trimmed = DrawingModifyService.TryPowerTrimOrExtendLine(
            document,
            "target",
            new Point2(0, 0),
            prefix => EntityId.Create($"{prefix}-split"),
            out var next);

        trimmed.Should().BeTrue();
        AssertLine(next.Entities[0], "target", new Point2(-10, 0), expectedLeft);
        AssertLine(next.Entities.Single(entity => entity.Id == EntityId.Create("line-split")), "line-split", expectedRight, new Point2(10, 0));
    }

    [Fact]
    public void PowerTrimControlPointSplineTargetUsesRefinedLineCutterIntersections()
    {
        const double leftParameter = 0.37;
        const double rightParameter = 0.73;
        var controls = new[]
        {
            new Point2(-10, 0),
            new Point2(-10 + (20.0 / 3.0), 4),
            new Point2(-10 + (40.0 / 3.0), -4),
            new Point2(10, 2)
        };
        var expectedLeft = EvaluateCubicBezier(controls, leftParameter);
        var expectedRight = EvaluateCubicBezier(controls, rightParameter);
        var document = new DrawingDocument(new DrawingEntity[]
        {
            CreateBezierSpline("target", controls),
            new LineEntity(EntityId.Create("left-cutter"), new Point2(expectedLeft.X, -10), new Point2(expectedLeft.X, 10)),
            new LineEntity(EntityId.Create("right-cutter"), new Point2(expectedRight.X, -10), new Point2(expectedRight.X, 10))
        });

        var trimmed = DrawingModifyService.TryPowerTrimOrExtendLine(
            document,
            "target",
            EvaluateCubicBezier(controls, 13.0 / 24.0),
            prefix => EntityId.Create($"{prefix}-split"),
            out var next);

        trimmed.Should().BeTrue();
        AssertSpline(next.Entities[0], "target", controls[0], expectedLeft);
        AssertSpline(next.Entities[3], "spline-split", expectedRight, controls[^1]);
    }

    [Fact]
    public void PowerTrimSplineTargetUsesRefinedSplineSegmentCutterIntersections()
    {
        var fitPoints = new[]
        {
            new Point2(-10, 0),
            new Point2(-5, 4),
            new Point2(0, 0),
            new Point2(5, -4),
            new Point2(10, 0)
        };
        var expectedLeft = FindCatmullRomVerticalCrossing(fitPoints, 1, -4);
        var expectedRight = FindCatmullRomVerticalCrossing(fitPoints, 2, 4);
        var document = new DrawingDocument(new DrawingEntity[]
        {
            SplineEntity.FromFitPoints(EntityId.Create("target"), fitPoints),
            SplineEntity.FromFitPoints(
                EntityId.Create("left-spline-cutter"),
                new[] { new Point2(-4, -10), new Point2(-4, 10) }),
            SplineEntity.FromFitPoints(
                EntityId.Create("right-spline-cutter"),
                new[] { new Point2(4, -10), new Point2(4, 10) })
        });

        var trimmed = DrawingModifyService.TryPowerTrimOrExtendLine(
            document,
            "target",
            new Point2(0, 0),
            prefix => EntityId.Create($"{prefix}-split"),
            out var next);

        trimmed.Should().BeTrue();
        AssertSpline(next.Entities[0], "target", new Point2(-10, 0), expectedLeft);
        AssertSpline(next.Entities[3], "spline-split", expectedRight, new Point2(10, 0));
    }

    [Fact]
    public void PowerTrimSplineTargetUsesRefinedCurvedSplineCutterIntersections()
    {
        var leftCutterPoints = new[]
        {
            new Point2(-6, -4),
            new Point2(-5, -1),
            new Point2(-4.2, 2),
            new Point2(-3, 3)
        };
        var rightCutterPoints = new[]
        {
            new Point2(3, -4),
            new Point2(4.2, -1),
            new Point2(5, 2),
            new Point2(6, 3)
        };
        var expectedLeft = FindCatmullRomHorizontalCrossing(leftCutterPoints, 1);
        var expectedRight = FindCatmullRomHorizontalCrossing(rightCutterPoints, 1);
        var document = new DrawingDocument(new DrawingEntity[]
        {
            SplineEntity.FromFitPoints(
                EntityId.Create("target"),
                new[] { new Point2(-10, 0), new Point2(10, 0) }),
            SplineEntity.FromFitPoints(EntityId.Create("left-spline-cutter"), leftCutterPoints),
            SplineEntity.FromFitPoints(EntityId.Create("right-spline-cutter"), rightCutterPoints)
        });

        var trimmed = DrawingModifyService.TryPowerTrimOrExtendLine(
            document,
            "target",
            new Point2(0, 0),
            prefix => EntityId.Create($"{prefix}-split"),
            out var next);

        trimmed.Should().BeTrue();
        AssertSpline(next.Entities[0], "target", new Point2(-10, 0), expectedLeft);
        AssertSpline(next.Entities[3], "spline-split", expectedRight, new Point2(10, 0));
    }

    [Fact]
    public void PowerTrimSplineTargetUsesRefinedArcCutterIntersections()
    {
        var fitPoints = new[]
        {
            new Point2(-10, 0),
            new Point2(-5, 4),
            new Point2(0, 0),
            new Point2(5, -4),
            new Point2(10, 0)
        };
        var expectedIntersections = FindCatmullRomCircleCrossings(fitPoints, new Point2(0, 0), 5)
            .OrderBy(point => point.X)
            .ToArray();
        var document = new DrawingDocument(new DrawingEntity[]
        {
            SplineEntity.FromFitPoints(EntityId.Create("target"), fitPoints),
            new ArcEntity(EntityId.Create("arc-cutter"), new Point2(0, 0), 5, 90, 360)
        });

        var trimmed = DrawingModifyService.TryPowerTrimOrExtendLine(
            document,
            "target",
            new Point2(0, 0),
            prefix => EntityId.Create($"{prefix}-split"),
            out var next);

        trimmed.Should().BeTrue();
        expectedIntersections.Should().HaveCount(2);
        AssertSpline(next.Entities[0], "target", new Point2(-10, 0), expectedIntersections[0]);
        AssertSpline(next.Entities[2], "spline-split", expectedIntersections[1], new Point2(10, 0));
    }

    [Fact]
    public void PowerTrimSplineTargetUsesRefinedEllipseCutterIntersections()
    {
        var fitPoints = new[]
        {
            new Point2(-10, 0),
            new Point2(-5, 4),
            new Point2(0, 0),
            new Point2(5, -4),
            new Point2(10, 0)
        };
        var ellipse = new EllipseEntity(EntityId.Create("ellipse-cutter"), new Point2(0, 0), new Point2(5, 0), 0.5);
        var expectedIntersections = FindCatmullRomEllipseCrossings(fitPoints, ellipse)
            .OrderBy(point => point.X)
            .ToArray();
        var document = new DrawingDocument(new DrawingEntity[]
        {
            SplineEntity.FromFitPoints(EntityId.Create("target"), fitPoints),
            ellipse
        });

        var trimmed = DrawingModifyService.TryPowerTrimOrExtendLine(
            document,
            "target",
            new Point2(0, 0),
            prefix => EntityId.Create($"{prefix}-split"),
            out var next);

        trimmed.Should().BeTrue();
        expectedIntersections.Should().HaveCount(2);
        AssertSpline(next.Entities[0], "target", new Point2(-10, 0), expectedIntersections[0]);
        AssertSpline(next.Entities[2], "spline-split", expectedIntersections[1], new Point2(10, 0));
    }

    [Theory]
    [MemberData(nameof(ApplicableSplineTargetCutters))]
    public void PowerTrimSplineUsesApplicableEntityTypesAsCutters(DrawingEntity cutter)
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new SplineEntity(
                EntityId.Create("target"),
                1,
                new[] { new Point2(-10, 0), new Point2(10, 0) },
                Array.Empty<double>()),
            cutter
        });

        var trimmed = DrawingModifyService.TryPowerTrimOrExtendLine(
            document,
            "target",
            new Point2(0, 0),
            prefix => EntityId.Create($"{prefix}-split"),
            out var next);

        trimmed.Should().BeTrue(cutter.Kind);
        next.Entities.Should().HaveCount(3);
        AssertSpline(next.Entities[0], "target", new Point2(-10, 0), new Point2(-5, 0));
        next.Entities[1].Should().Be(cutter);
        AssertSpline(next.Entities[2], "spline-split", new Point2(5, 0), new Point2(10, 0));
    }

    [Fact]
    public void PowerTrimSplineUsesPointEntitiesAsCutters()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new SplineEntity(
                EntityId.Create("target"),
                1,
                new[] { new Point2(-10, 0), new Point2(10, 0) },
                Array.Empty<double>()),
            new PointEntity(EntityId.Create("left-point"), new Point2(-5, 0)),
            new PointEntity(EntityId.Create("right-point"), new Point2(5, 0))
        });

        var trimmed = DrawingModifyService.TryPowerTrimOrExtendLine(
            document,
            "target",
            new Point2(0, 0),
            prefix => EntityId.Create($"{prefix}-split"),
            out var next);

        trimmed.Should().BeTrue();
        next.Entities.Should().HaveCount(4);
        AssertSpline(next.Entities[0], "target", new Point2(-10, 0), new Point2(-5, 0));
        next.Entities[1].Should().Be(new PointEntity(EntityId.Create("left-point"), new Point2(-5, 0)));
        next.Entities[2].Should().Be(new PointEntity(EntityId.Create("right-point"), new Point2(5, 0)));
        AssertSpline(next.Entities[3], "spline-split", new Point2(5, 0), new Point2(10, 0));
    }

    [Fact]
    public void PowerTrimSplineUsesTangentArcTouchAsCutter()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new SplineEntity(
                EntityId.Create("target"),
                1,
                new[] { new Point2(-10, 0), new Point2(10, 0) },
                Array.Empty<double>()),
            new ArcEntity(EntityId.Create("tangent-cutter"), new Point2(0, 5), 5, 180, 360),
            new LineEntity(EntityId.Create("line-cutter"), new Point2(5, -2), new Point2(5, 2))
        });

        var trimmed = DrawingModifyService.TryPowerTrimOrExtendLine(
            document,
            "target",
            new Point2(2.5, 0),
            prefix => EntityId.Create($"{prefix}-split"),
            out var next);

        trimmed.Should().BeTrue();
        next.Entities.Should().HaveCount(4);
        AssertSpline(next.Entities[0], "target", new Point2(-10, 0), new Point2(0, 0));
        next.Entities[1].Should().Be(new ArcEntity(EntityId.Create("tangent-cutter"), new Point2(0, 5), 5, 180, 360));
        next.Entities[2].Should().Be(new LineEntity(EntityId.Create("line-cutter"), new Point2(5, -2), new Point2(5, 2)));
        AssertSpline(next.Entities[3], "spline-split", new Point2(5, 0), new Point2(10, 0));
    }

    [Fact]
    public void PowerTrimSplineDeletesOpenTargetWhenPickedWithoutCutters()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new SplineEntity(
                EntityId.Create("target"),
                1,
                new[] { new Point2(-10, 0), new Point2(10, 0) },
                Array.Empty<double>())
        });

        var trimmed = DrawingModifyService.TryPowerTrimOrExtendLine(
            document,
            "target",
            new Point2(0, 0),
            prefix => EntityId.Create($"{prefix}-split"),
            out var next);

        trimmed.Should().BeTrue();
        next.Entities.Should().BeEmpty();
    }

    [Fact]
    public void PowerTrimPolylineRemovesPickedSpanBetweenLineCutters()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new PolylineEntity(EntityId.Create("target"), new[] { new Point2(-10, 0), new Point2(10, 0) }),
            new LineEntity(EntityId.Create("left"), new Point2(-5, -2), new Point2(-5, 2)),
            new LineEntity(EntityId.Create("right"), new Point2(5, -2), new Point2(5, 2))
        });

        var trimmed = DrawingModifyService.TryPowerTrimOrExtendLine(
            document,
            "target",
            new Point2(0, 0),
            prefix => EntityId.Create($"{prefix}-split"),
            out var next);

        trimmed.Should().BeTrue();
        next.Entities.Should().HaveCount(4);
        AssertPolyline(next.Entities[0], "target", new Point2(-10, 0), new Point2(-5, 0));
        next.Entities[1].Should().Be(new LineEntity(EntityId.Create("left"), new Point2(-5, -2), new Point2(-5, 2)));
        next.Entities[2].Should().Be(new LineEntity(EntityId.Create("right"), new Point2(5, -2), new Point2(5, 2)));
        AssertPolyline(next.Entities[3], "polyline-split", new Point2(5, 0), new Point2(10, 0));
    }

    [Fact]
    public void PowerTrimPolylineDeletesOpenTargetWhenPickedWithoutCutters()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new PolylineEntity(
                EntityId.Create("target"),
                new[] { new Point2(-10, 0), new Point2(0, 5), new Point2(10, 0) })
        });

        var trimmed = DrawingModifyService.TryPowerTrimOrExtendLine(
            document,
            "target",
            new Point2(0, 5),
            prefix => EntityId.Create($"{prefix}-split"),
            out var next);

        trimmed.Should().BeTrue();
        next.Entities.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(ApplicablePolylineTargetCutters))]
    public void PowerTrimPolylineUsesApplicableEntityTypesAsCutters(DrawingEntity cutter)
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new PolylineEntity(EntityId.Create("target"), new[] { new Point2(-10, 0), new Point2(10, 0) }),
            cutter
        });

        var trimmed = DrawingModifyService.TryPowerTrimOrExtendLine(
            document,
            "target",
            new Point2(0, 0),
            prefix => EntityId.Create($"{prefix}-split"),
            out var next);

        trimmed.Should().BeTrue(cutter.Kind);
        next.Entities.Should().HaveCount(3);
        AssertPolyline(next.Entities[0], "target", new Point2(-10, 0), new Point2(-5, 0));
        next.Entities[1].Should().Be(cutter);
        AssertPolyline(next.Entities[2], "polyline-split", new Point2(5, 0), new Point2(10, 0));
    }

    [Fact]
    public void PowerTrimPolylineUsesPointEntitiesAsCutters()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new PolylineEntity(EntityId.Create("target"), new[] { new Point2(-10, 0), new Point2(10, 0) }),
            new PointEntity(EntityId.Create("left-point"), new Point2(-5, 0)),
            new PointEntity(EntityId.Create("right-point"), new Point2(5, 0))
        });

        var trimmed = DrawingModifyService.TryPowerTrimOrExtendLine(
            document,
            "target",
            new Point2(0, 0),
            prefix => EntityId.Create($"{prefix}-split"),
            out var next);

        trimmed.Should().BeTrue();
        next.Entities.Should().HaveCount(4);
        AssertPolyline(next.Entities[0], "target", new Point2(-10, 0), new Point2(-5, 0));
        next.Entities[1].Should().Be(new PointEntity(EntityId.Create("left-point"), new Point2(-5, 0)));
        next.Entities[2].Should().Be(new PointEntity(EntityId.Create("right-point"), new Point2(5, 0)));
        AssertPolyline(next.Entities[3], "polyline-split", new Point2(5, 0), new Point2(10, 0));
    }

    [Fact]
    public void PowerTrimPolylineUsesTangentArcTouchAsCutter()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new PolylineEntity(EntityId.Create("target"), new[] { new Point2(-10, 0), new Point2(10, 0) }),
            new ArcEntity(EntityId.Create("tangent-cutter"), new Point2(0, 5), 5, 180, 360),
            new LineEntity(EntityId.Create("line-cutter"), new Point2(5, -2), new Point2(5, 2))
        });

        var trimmed = DrawingModifyService.TryPowerTrimOrExtendLine(
            document,
            "target",
            new Point2(2.5, 0),
            prefix => EntityId.Create($"{prefix}-split"),
            out var next);

        trimmed.Should().BeTrue();
        next.Entities.Should().HaveCount(4);
        AssertPolyline(next.Entities[0], "target", new Point2(-10, 0), new Point2(0, 0));
        next.Entities[1].Should().Be(new ArcEntity(EntityId.Create("tangent-cutter"), new Point2(0, 5), 5, 180, 360));
        next.Entities[2].Should().Be(new LineEntity(EntityId.Create("line-cutter"), new Point2(5, -2), new Point2(5, 2)));
        AssertPolyline(next.Entities[3], "polyline-split", new Point2(5, 0), new Point2(10, 0));
    }

    [Fact]
    public void PowerTrimPolygonRemovesPickedSpanBetweenLineCutters()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new PolygonEntity(EntityId.Create("target"), new Point2(0, 0), 5, 0, 4),
            new LineEntity(EntityId.Create("left"), new Point2(-2, -10), new Point2(-2, 10)),
            new LineEntity(EntityId.Create("right"), new Point2(2, -10), new Point2(2, 10))
        });

        var trimmed = DrawingModifyService.TryPowerTrimOrExtendLine(
            document,
            "target",
            new Point2(0, 5),
            prefix => EntityId.Create($"{prefix}-split"),
            out var next);

        trimmed.Should().BeTrue();
        next.Entities.Should().HaveCount(6);
        AssertLine(next.Entities[0], "polygon-line-1-split", new Point2(-2, 3), new Point2(-5, 0));
        AssertLine(next.Entities[1], "polygon-line-2-split", new Point2(-5, 0), new Point2(0, -5));
        AssertLine(next.Entities[2], "polygon-line-3-split", new Point2(0, -5), new Point2(5, 0));
        AssertLine(next.Entities[3], "polygon-line-4-split", new Point2(5, 0), new Point2(2, 3));
        next.Entities[4].Should().Be(new LineEntity(EntityId.Create("left"), new Point2(-2, -10), new Point2(-2, 10)));
        next.Entities[5].Should().Be(new LineEntity(EntityId.Create("right"), new Point2(2, -10), new Point2(2, 10)));
    }

    [Fact]
    public void PowerTrimPolygonExplodesIntoLinesAndRemovesStaleSketchReferences()
    {
        var document = new DrawingDocument(
            new DrawingEntity[]
            {
                new PolygonEntity(EntityId.Create("target"), new Point2(0, 0), 5, 0, 4),
                new LineEntity(EntityId.Create("left"), new Point2(-2, -10), new Point2(-2, 10)),
                new LineEntity(EntityId.Create("right"), new Point2(2, -10), new Point2(2, 10))
            },
            new[]
            {
                new SketchDimension(
                    "target-radius",
                    SketchDimensionKind.Radius,
                    new[] { "target" },
                    5,
                    isDriving: true)
            },
            new[]
            {
                new SketchConstraint(
                    "target-fixed",
                    SketchConstraintKind.Fix,
                    new[] { "target" },
                    SketchConstraintState.Satisfied)
            });

        var trimmed = DrawingModifyService.TryPowerTrimOrExtendLine(
            document,
            "target",
            new Point2(0, 5),
            prefix => EntityId.Create($"{prefix}-split"),
            out var next);

        trimmed.Should().BeTrue();
        next.Entities.Should().NotContain(entity => entity.Id == EntityId.Create("target"));
        next.Entities.Any(entity => entity is PolygonEntity or PolylineEntity).Should().BeFalse();
        next.Entities.OfType<LineEntity>()
            .Where(line => line.Id.Value.StartsWith("polygon-line-", StringComparison.Ordinal))
            .Should()
            .HaveCount(4);
        next.Dimensions.Should().BeEmpty();
        next.Constraints.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(ApplicablePolygonTargetCutters))]
    public void PowerTrimPolygonUsesApplicableEntityTypesAsCutters(DrawingEntity cutter)
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new PolygonEntity(EntityId.Create("target"), new Point2(0, 0), 5, 0, 4),
            cutter
        });

        var trimmed = DrawingModifyService.TryPowerTrimOrExtendLine(
            document,
            "target",
            new Point2(0, 5),
            prefix => EntityId.Create($"{prefix}-split"),
            out var next);

        trimmed.Should().BeTrue(cutter.Kind);
        next.Entities.Should().ContainSingle(entity => entity.Id == cutter.Id);
        next.Entities.Should().NotContain(entity => entity.Id == EntityId.Create("target"));
        next.Entities.OfType<LineEntity>()
            .Where(line => line.Id.Value.StartsWith("polygon-line-", StringComparison.Ordinal))
            .Should()
            .HaveCountGreaterThan(0);
    }

    [Fact]
    public void PowerTrimPolygonUsesPointEntitiesAsCutters()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new PolygonEntity(EntityId.Create("target"), new Point2(0, 0), 5, 0, 4),
            new PointEntity(EntityId.Create("left-point"), new Point2(-2, 3)),
            new PointEntity(EntityId.Create("right-point"), new Point2(2, 3))
        });

        var trimmed = DrawingModifyService.TryPowerTrimOrExtendLine(
            document,
            "target",
            new Point2(0, 5),
            prefix => EntityId.Create($"{prefix}-split"),
            out var next);

        trimmed.Should().BeTrue();
        next.Entities.Should().HaveCount(6);
        AssertLine(next.Entities[0], "polygon-line-1-split", new Point2(-2, 3), new Point2(-5, 0));
        AssertLine(next.Entities[1], "polygon-line-2-split", new Point2(-5, 0), new Point2(0, -5));
        AssertLine(next.Entities[2], "polygon-line-3-split", new Point2(0, -5), new Point2(5, 0));
        AssertLine(next.Entities[3], "polygon-line-4-split", new Point2(5, 0), new Point2(2, 3));
        next.Entities[4].Should().Be(new PointEntity(EntityId.Create("left-point"), new Point2(-2, 3)));
        next.Entities[5].Should().Be(new PointEntity(EntityId.Create("right-point"), new Point2(2, 3)));
    }

    [Fact]
    public void PowerTrimPointDeletesPointTarget()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new PointEntity(EntityId.Create("target"), new Point2(0, 0)),
            new LineEntity(EntityId.Create("other"), new Point2(-1, 0), new Point2(1, 0))
        });

        var trimmed = DrawingModifyService.TryPowerTrimOrExtendLine(
            document,
            "target",
            new Point2(0, 0),
            prefix => EntityId.Create($"{prefix}-split"),
            out var next);

        trimmed.Should().BeTrue();
        next.Entities.Should().Equal(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("other"), new Point2(-1, 0), new Point2(1, 0))
        });
    }

    [Theory]
    [MemberData(nameof(ApplicableLineTargetCutters))]
    public void PowerTrimLineUsesApplicableEntityTypesAsCutters(DrawingEntity cutter)
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("target"), new Point2(-10, 0), new Point2(10, 0)),
            cutter
        });

        var trimmed = DrawingModifyService.TryPowerTrimOrExtendLine(
            document,
            "target",
            new Point2(0, 0),
            prefix => EntityId.Create($"{prefix}-split"),
            out var next);

        trimmed.Should().BeTrue(cutter.Kind);
        next.Entities.Should().HaveCount(3);
        AssertLine(next.Entities[0], "target", new Point2(-10, 0), new Point2(-5, 0));
        AssertLine(next.Entities[1], "line-split", new Point2(5, 0), new Point2(10, 0));
        next.Entities[2].Should().Be(cutter);
    }

    [Fact]
    public void PowerTrimLineUsesPointEntitiesAsCutters()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("target"), new Point2(-10, 0), new Point2(10, 0)),
            new PointEntity(EntityId.Create("left-point"), new Point2(-5, 0)),
            new PointEntity(EntityId.Create("right-point"), new Point2(5, 0))
        });

        var trimmed = DrawingModifyService.TryPowerTrimOrExtendLine(
            document,
            "target",
            new Point2(0, 0),
            prefix => EntityId.Create($"{prefix}-split"),
            out var next);

        trimmed.Should().BeTrue();
        next.Entities.Should().HaveCount(4);
        next.Entities[0].Should().Be(new LineEntity(EntityId.Create("target"), new Point2(-10, 0), new Point2(-5, 0)));
        next.Entities[1].Should().Be(new LineEntity(EntityId.Create("line-split"), new Point2(5, 0), new Point2(10, 0)));
        next.Entities[2].Should().Be(new PointEntity(EntityId.Create("left-point"), new Point2(-5, 0)));
        next.Entities[3].Should().Be(new PointEntity(EntityId.Create("right-point"), new Point2(5, 0)));
    }

    [Fact]
    public void PowerTrimArcTrimsPickedOpenSpanAtLineCutter()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new ArcEntity(EntityId.Create("target"), new Point2(0, 0), 5, 0, 180),
            new LineEntity(EntityId.Create("cutter"), new Point2(0, -10), new Point2(0, 10))
        });

        var trimmed = DrawingModifyService.TryPowerTrimOrExtendLine(
            document,
            "target",
            new Point2(-3, 3),
            prefix => EntityId.Create($"{prefix}-split"),
            out var next);

        trimmed.Should().BeTrue();
        next.Entities.Should().HaveCount(2);
        next.Entities[0].Should().BeOfType<ArcEntity>()
            .Which.Should().Match<ArcEntity>(arc =>
                arc.Id.Value == "target"
                && arc.Center == new Point2(0, 0)
                && Math.Abs(arc.Radius - 5) <= 0.000001
                && Math.Abs(arc.StartAngleDegrees) <= 0.000001
                && Math.Abs(arc.EndAngleDegrees - 90) <= 0.000001);
        next.Entities[1].Should().Be(new LineEntity(EntityId.Create("cutter"), new Point2(0, -10), new Point2(0, 10)));
    }

    [Fact]
    public void PowerTrimArcDeletesOpenTargetWhenPickedWithoutCutters()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new ArcEntity(EntityId.Create("target"), new Point2(0, 0), 5, 0, 180)
        });

        var trimmed = DrawingModifyService.TryPowerTrimOrExtendLine(
            document,
            "target",
            new Point2(0, 5),
            prefix => EntityId.Create($"{prefix}-split"),
            out var next);

        trimmed.Should().BeTrue();
        next.Entities.Should().BeEmpty();
    }

    [Fact]
    public void PowerTrimEllipticalArcDeletesOpenTargetWhenPickedWithoutCutters()
    {
        var target = new EllipseEntity(EntityId.Create("target"), new Point2(0, 0), new Point2(5, 0), 0.5, 0, 180);
        var document = new DrawingDocument(new DrawingEntity[] { target });

        var trimmed = DrawingModifyService.TryPowerTrimOrExtendLine(
            document,
            "target",
            target.PointAtParameterDegrees(90),
            prefix => EntityId.Create($"{prefix}-split"),
            out var next);

        trimmed.Should().BeTrue();
        next.Entities.Should().BeEmpty();
    }

    [Fact]
    public void PowerTrimArcUsesArcIntersectionsAsCutters()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new ArcEntity(EntityId.Create("target"), new Point2(0, 0), 5, 0, 180),
            new ArcEntity(EntityId.Create("arc-cutter"), new Point2(0, 5), 5, 180, 360)
        });

        var trimmed = DrawingModifyService.TryPowerTrimOrExtendLine(
            document,
            "target",
            new Point2(0, 5),
            prefix => EntityId.Create($"{prefix}-split"),
            out var next);

        trimmed.Should().BeTrue();
        next.Entities.Should().HaveCount(3);
        AssertArc(next.Entities[0], "target", new Point2(0, 0), 5, 0, 30);
        next.Entities[1].Should().Be(new ArcEntity(EntityId.Create("arc-cutter"), new Point2(0, 5), 5, 180, 360));
        AssertArc(next.Entities[2], "arc-split", new Point2(0, 0), 5, 150, 180);
    }

    [Fact]
    public void PowerTrimArcUsesTangentArcTouchAsCutter()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new ArcEntity(EntityId.Create("target"), new Point2(0, 0), 5, 0, 180),
            new ArcEntity(EntityId.Create("tangent-cutter"), new Point2(0, 10), 5, 180, 360),
            new LineEntity(EntityId.Create("line-cutter"), new Point2(-2.5, -10), new Point2(-2.5, 10))
        });

        var trimmed = DrawingModifyService.TryPowerTrimOrExtendLine(
            document,
            "target",
            PointOnCircle(new Point2(0, 0), 5, 105),
            prefix => EntityId.Create($"{prefix}-split"),
            out var next);

        trimmed.Should().BeTrue();
        next.Entities.Should().HaveCount(4);
        AssertArc(next.Entities[0], "target", new Point2(0, 0), 5, 0, 90);
        next.Entities[1].Should().Be(new ArcEntity(EntityId.Create("tangent-cutter"), new Point2(0, 10), 5, 180, 360));
        next.Entities[2].Should().Be(new LineEntity(EntityId.Create("line-cutter"), new Point2(-2.5, -10), new Point2(-2.5, 10)));
        AssertArc(next.Entities[3], "arc-split", new Point2(0, 0), 5, 120, 180);
    }

    [Fact]
    public void PowerTrimArcUsesRefinedSplineCutterIntersections()
    {
        var cutterPoints = new[]
        {
            new Point2(-6, 1),
            new Point2(-2, 3),
            new Point2(2, 3),
            new Point2(6, 1)
        };
        var expectedAngles = FindCatmullRomCircleCrossings(cutterPoints, new Point2(0, 0), 5)
            .Select(point => NormalizeTestAngleDegrees(Math.Atan2(point.Y, point.X) * 180.0 / Math.PI))
            .OrderBy(angle => angle)
            .ToArray();
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new ArcEntity(EntityId.Create("target"), new Point2(0, 0), 5, 0, 180),
            SplineEntity.FromFitPoints(EntityId.Create("spline-cutter"), cutterPoints)
        });

        var trimmed = DrawingModifyService.TryPowerTrimOrExtendLine(
            document,
            "target",
            new Point2(0, 5),
            prefix => EntityId.Create($"{prefix}-split"),
            out var next);

        trimmed.Should().BeTrue();
        expectedAngles.Should().HaveCount(2);
        AssertArc(next.Entities[0], "target", new Point2(0, 0), 5, 0, expectedAngles[0]);
        AssertArc(next.Entities[2], "arc-split", new Point2(0, 0), 5, expectedAngles[1], 180);
    }

    [Theory]
    [MemberData(nameof(ApplicableArcTargetCutters))]
    public void PowerTrimArcUsesApplicableEntityTypesAsCutters(DrawingEntity cutter)
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new ArcEntity(EntityId.Create("target"), new Point2(0, 0), 5, 0, 180),
            cutter
        });

        var trimmed = DrawingModifyService.TryPowerTrimOrExtendLine(
            document,
            "target",
            new Point2(0, 5),
            prefix => EntityId.Create($"{prefix}-split"),
            out var next);

        trimmed.Should().BeTrue(cutter.Kind);
        next.Entities.Should().HaveCount(3);
        var firstArc = next.Entities[0].Should().BeOfType<ArcEntity>().Subject;
        firstArc.Id.Should().Be(EntityId.Create("target"));
        firstArc.Center.Should().Be(new Point2(0, 0));
        firstArc.Radius.Should().BeApproximately(5, 0.000001);
        firstArc.StartAngleDegrees.Should().BeApproximately(0, 0.000001);
        firstArc.EndAngleDegrees.Should().BeLessThan(90);
        next.Entities[1].Should().Be(cutter);
        var splitArc = next.Entities[2].Should().BeOfType<ArcEntity>().Subject;
        splitArc.Id.Should().Be(EntityId.Create("arc-split"));
        splitArc.Center.Should().Be(new Point2(0, 0));
        splitArc.Radius.Should().BeApproximately(5, 0.000001);
        splitArc.StartAngleDegrees.Should().BeGreaterThan(90);
        splitArc.EndAngleDegrees.Should().BeApproximately(180, 0.000001);
    }

    [Fact]
    public void PowerTrimArcUsesPointEntitiesAsCutters()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new ArcEntity(EntityId.Create("target"), new Point2(0, 0), 5, 0, 180),
            new PointEntity(EntityId.Create("left-point"), PointOnCircle(new Point2(0, 0), 5, 120)),
            new PointEntity(EntityId.Create("right-point"), PointOnCircle(new Point2(0, 0), 5, 60))
        });

        var trimmed = DrawingModifyService.TryPowerTrimOrExtendLine(
            document,
            "target",
            new Point2(0, 5),
            prefix => EntityId.Create($"{prefix}-split"),
            out var next);

        trimmed.Should().BeTrue();
        next.Entities.Should().HaveCount(4);
        AssertArc(next.Entities[0], "target", new Point2(0, 0), 5, 0, 60);
        next.Entities[1].Should().Be(new PointEntity(EntityId.Create("left-point"), PointOnCircle(new Point2(0, 0), 5, 120)));
        next.Entities[2].Should().Be(new PointEntity(EntityId.Create("right-point"), PointOnCircle(new Point2(0, 0), 5, 60)));
        AssertArc(next.Entities[3], "arc-split", new Point2(0, 0), 5, 120, 180);
    }

    [Fact]
    public void PowerTrimBatchDeletesOpenCrossedTargetsAndLeavesClosedTargetsUnchanged()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new EllipseEntity(EntityId.Create("ellipse"), new Point2(0, 0), new Point2(5, 0), 0.5),
            SplineEntity.FromFitPoints(EntityId.Create("spline"), new[] { new Point2(8, 0), new Point2(12, 4), new Point2(16, 0) })
        });

        var applied = DrawingModifyService.PowerTrimOrExtendLines(
            document,
            new[]
            {
                new PowerTrimLinePick("ellipse", new Point2(5, 0)),
                new PowerTrimLinePick("spline", new Point2(12, 4))
            },
            prefix => EntityId.Create($"{prefix}-split"),
            out var next);

        applied.Should().Be(1);
        next.Entities.Should().ContainSingle()
            .Which.Should().Be(new EllipseEntity(EntityId.Create("ellipse"), new Point2(0, 0), new Point2(5, 0), 0.5));
    }

    [Fact]
    public void PowerTrimDoesNotDeleteLineWhenEndpointIsPickedForExtend()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("target"), new Point2(0, 0), new Point2(10, 0)),
            new LineEntity(EntityId.Create("cutter"), new Point2(15, -1), new Point2(15, 1))
        });

        var trimmed = DrawingModifyService.TryPowerTrimOrExtendLine(
            document,
            "target",
            new Point2(10, 0),
            prefix => EntityId.Create($"{prefix}-split"),
            out var next);

        trimmed.Should().BeFalse();
        next.Should().BeSameAs(document);
        next.Entities.Should().Equal(document.Entities);
    }

    [Fact]
    public void PowerTrimExtendsLineEndToLineBoundaryWhenPickedPastEndpoint()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("target"), new Point2(0, 0), new Point2(10, 0)),
            new LineEntity(EntityId.Create("boundary"), new Point2(15, -2), new Point2(15, 2))
        });

        var trimmed = DrawingModifyService.TryPowerTrimOrExtendLine(
            document,
            "target",
            new Point2(10.35, 0),
            prefix => EntityId.Create($"{prefix}-split"),
            out var next);

        trimmed.Should().BeTrue();
        next.Entities.Should().HaveCount(2);
        next.Entities[0].Should().Be(new LineEntity(EntityId.Create("target"), new Point2(0, 0), new Point2(15, 0)));
        next.Entities[1].Should().Be(new LineEntity(EntityId.Create("boundary"), new Point2(15, -2), new Point2(15, 2)));
    }

    [Theory]
    [MemberData(nameof(ApplicableLineExtendBoundaries))]
    public void PowerTrimExtendsLineEndToApplicableBoundaryTypesWhenPickedPastEndpoint(
        DrawingEntity boundary,
        Point2 expectedEnd)
    {
        var document = new DrawingDocument(new[]
        {
            new LineEntity(EntityId.Create("target"), new Point2(0, 0), new Point2(10, 0)),
            boundary
        });

        var trimmed = DrawingModifyService.TryPowerTrimOrExtendLine(
            document,
            "target",
            new Point2(10.35, 0),
            prefix => EntityId.Create($"{prefix}-split"),
            out var next);

        trimmed.Should().BeTrue(boundary.GetType().Name);
        next.Entities.Should().HaveCount(2);
        AssertLine(next.Entities[0], "target", new Point2(0, 0), expectedEnd);
        next.Entities[1].Should().Be(boundary);
    }

    [Theory]
    [MemberData(nameof(ApplicablePolylineExtendBoundaries))]
    public void PowerTrimExtendsPolylineEndToApplicableBoundaryTypesWhenPickedPastEndpoint(
        DrawingEntity boundary,
        Point2 expectedEnd)
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new PolylineEntity(
                EntityId.Create("target"),
                new[] { new Point2(0, 0), new Point2(5, 0), new Point2(10, 0) }),
            boundary
        });

        var trimmed = DrawingModifyService.TryPowerTrimOrExtendLine(
            document,
            "target",
            new Point2(10.35, 0),
            prefix => EntityId.Create($"{prefix}-split"),
            out var next);

        trimmed.Should().BeTrue(boundary.GetType().Name);
        next.Entities.Should().HaveCount(2);
        AssertPolyline(next.Entities[0], "target", new Point2(0, 0), expectedEnd);
        next.Entities[1].Should().Be(boundary);
    }

    [Fact]
    public void PowerTrimExtendsPolylineStartToLineBoundaryWhenPickedPastStartpoint()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new PolylineEntity(
                EntityId.Create("target"),
                new[] { new Point2(0, 0), new Point2(5, 0), new Point2(10, 0) }),
            new LineEntity(EntityId.Create("boundary"), new Point2(-5, -2), new Point2(-5, 2))
        });

        var trimmed = DrawingModifyService.TryPowerTrimOrExtendLine(
            document,
            "target",
            new Point2(-0.35, 0),
            prefix => EntityId.Create($"{prefix}-split"),
            out var next);

        trimmed.Should().BeTrue();
        next.Entities.Should().HaveCount(2);
        AssertPolyline(next.Entities[0], "target", new Point2(-5, 0), new Point2(10, 0));
        next.Entities[1].Should().Be(new LineEntity(EntityId.Create("boundary"), new Point2(-5, -2), new Point2(-5, 2)));
    }

    [Theory]
    [MemberData(nameof(ApplicablePolylineExtendBoundaries))]
    public void PowerTrimExtendsSplineEndToApplicableBoundaryTypesWhenPickedPastEndpoint(
        DrawingEntity boundary,
        Point2 expectedEnd)
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            SplineEntity.FromFitPoints(
                EntityId.Create("target"),
                new[] { new Point2(0, 0), new Point2(5, 0), new Point2(10, 0) }),
            boundary
        });

        var trimmed = DrawingModifyService.TryPowerTrimOrExtendLine(
            document,
            "target",
            new Point2(10.35, 0),
            prefix => EntityId.Create($"{prefix}-split"),
            out var next);

        trimmed.Should().BeTrue(boundary.GetType().Name);
        next.Entities.Should().HaveCount(2);
        AssertSpline(next.Entities[0], "target", new Point2(0, 0), expectedEnd);
        next.Entities[1].Should().Be(boundary);
    }

    [Fact]
    public void PowerTrimExtendsSplineStartToLineBoundaryWhenPickedPastStartpoint()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            SplineEntity.FromFitPoints(
                EntityId.Create("target"),
                new[] { new Point2(0, 0), new Point2(5, 0), new Point2(10, 0) }),
            new LineEntity(EntityId.Create("boundary"), new Point2(-5, -2), new Point2(-5, 2))
        });

        var trimmed = DrawingModifyService.TryPowerTrimOrExtendLine(
            document,
            "target",
            new Point2(-0.35, 0),
            prefix => EntityId.Create($"{prefix}-split"),
            out var next);

        trimmed.Should().BeTrue();
        next.Entities.Should().HaveCount(2);
        AssertSpline(next.Entities[0], "target", new Point2(-5, 0), new Point2(10, 0));
        next.Entities[1].Should().Be(new LineEntity(EntityId.Create("boundary"), new Point2(-5, -2), new Point2(-5, 2)));
    }

    [Theory]
    [MemberData(nameof(ApplicableArcExtendBoundaries))]
    public void PowerTrimExtendsArcEndToApplicableBoundaryTypesWhenPickedPastEndpoint(
        DrawingEntity boundary,
        double expectedEndAngle)
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new ArcEntity(EntityId.Create("target"), new Point2(0, 0), 5, 0, 90),
            boundary
        });

        var trimmed = DrawingModifyService.TryPowerTrimOrExtendLine(
            document,
            "target",
            PointOnCircle(new Point2(0, 0), 5, 100),
            prefix => EntityId.Create($"{prefix}-split"),
            out var next);

        trimmed.Should().BeTrue(boundary.GetType().Name);
        next.Entities.Should().HaveCount(2);
        AssertArc(next.Entities[0], "target", new Point2(0, 0), 5, 0, expectedEndAngle);
        next.Entities[1].Should().Be(boundary);
    }

    [Fact]
    public void PowerTrimExtendsArcEndToEllipseBoundaryPastLargeAngle()
    {
        var center = new Point2(0, 0);
        const double expectedAngle = 300;
        var expected = PointOnCircle(center, 5, expectedAngle);
        var radial = UnitRadial(expectedAngle);
        var boundary = new EllipseEntity(
            EntityId.Create("ellipse-boundary"),
            Offset(expected, radial, 0.5),
            Multiply(radial, 0.5),
            0.5,
            180,
            360);
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new ArcEntity(EntityId.Create("target"), center, 5, 0, 40),
            boundary
        });

        var trimmed = DrawingModifyService.TryPowerTrimOrExtendLine(
            document,
            "target",
            PointOnCircle(center, 5, 50),
            prefix => EntityId.Create($"{prefix}-split"),
            out var next);

        trimmed.Should().BeTrue();
        next.Entities.Should().HaveCount(2);
        AssertArc(next.Entities[0], "target", center, 5, 0, expectedAngle);
        next.Entities[1].Should().Be(boundary);
    }

    [Fact]
    public void PowerTrimExtendsArcStartToLineBoundaryWhenPickedPastStartpoint()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new ArcEntity(EntityId.Create("target"), new Point2(0, 0), 5, 0, 90),
            new LineEntity(EntityId.Create("boundary"), new Point2(0, 0), PointOnCircle(new Point2(0, 0), 5, 330))
        });

        var trimmed = DrawingModifyService.TryPowerTrimOrExtendLine(
            document,
            "target",
            PointOnCircle(new Point2(0, 0), 5, 350),
            prefix => EntityId.Create($"{prefix}-split"),
            out var next);

        trimmed.Should().BeTrue();
        next.Entities.Should().HaveCount(2);
        AssertArc(next.Entities[0], "target", new Point2(0, 0), 5, -30, 90);
    }

    [Theory]
    [MemberData(nameof(ApplicableEllipseArcExtendBoundaries))]
    public void PowerTrimExtendsEllipseArcEndToApplicableBoundaryTypesWhenPickedPastEndpoint(
        DrawingEntity boundary,
        double expectedEndParameter)
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new EllipseEntity(EntityId.Create("target"), new Point2(0, 0), new Point2(5, 0), 0.5, 0, 90),
            boundary
        });

        var trimmed = DrawingModifyService.TryPowerTrimOrExtendLine(
            document,
            "target",
            PointOnEllipse(new Point2(0, 0), new Point2(5, 0), 0.5, 100),
            prefix => EntityId.Create($"{prefix}-split"),
            out var next);

        trimmed.Should().BeTrue(boundary.GetType().Name);
        next.Entities.Should().HaveCount(2);
        AssertEllipse(next.Entities[0], "target", new Point2(0, 0), new Point2(5, 0), 0.5, 0, expectedEndParameter);
        next.Entities[1].Should().Be(boundary);
    }

    [Fact]
    public void PowerTrimExtendsEllipseArcStartToLineBoundaryWhenPickedPastStartpoint()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new EllipseEntity(EntityId.Create("target"), new Point2(0, 0), new Point2(5, 0), 0.5, 0, 90),
            new LineEntity(
                EntityId.Create("boundary"),
                new Point2(0, 0),
                PointOnEllipse(new Point2(0, 0), new Point2(5, 0), 0.5, 330))
        });

        var trimmed = DrawingModifyService.TryPowerTrimOrExtendLine(
            document,
            "target",
            PointOnEllipse(new Point2(0, 0), new Point2(5, 0), 0.5, 350),
            prefix => EntityId.Create($"{prefix}-split"),
            out var next);

        trimmed.Should().BeTrue();
        next.Entities.Should().HaveCount(2);
        AssertEllipse(next.Entities[0], "target", new Point2(0, 0), new Point2(5, 0), 0.5, -30, 90);
    }

    [Fact]
    public void PowerTrimDoesNotDeleteLineWhenEndpointIsPickedWithoutResolvedCutters()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("target"), new Point2(0, 0), new Point2(10, 0)),
            new CircleEntity(EntityId.Create("other"), new Point2(20, 0), 2)
        });

        var trimmed = DrawingModifyService.TryPowerTrimOrExtendLine(
            document,
            "target",
            new Point2(10, 0),
            prefix => EntityId.Create($"{prefix}-split"),
            out var next);

        trimmed.Should().BeFalse();
        next.Should().BeSameAs(document);
        next.Entities.Should().Equal(document.Entities);
    }

    [Fact]
    public void PowerTrimBatchAppliesDragCrossedLinePicks()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("top"), new Point2(0, 2), new Point2(10, 2)),
            new LineEntity(EntityId.Create("bottom"), new Point2(0, -2), new Point2(10, -2)),
            new LineEntity(EntityId.Create("left"), new Point2(3, -4), new Point2(3, 4)),
            new LineEntity(EntityId.Create("right"), new Point2(7, -4), new Point2(7, 4))
        });

        var applied = DrawingModifyService.PowerTrimOrExtendLines(
            document,
            new[]
            {
                new PowerTrimLinePick("top", new Point2(5, 2)),
                new PowerTrimLinePick("bottom", new Point2(5, -2))
            },
            prefix => EntityId.Create($"{prefix}-split-{Guid.NewGuid():N}"),
            out var next);

        applied.Should().Be(2);
        next.Entities.Should().HaveCount(6);
        next.Entities.OfType<LineEntity>().Should().Contain(line =>
            line.Id.Value == "top" && line.Start == new Point2(0, 2) && line.End == new Point2(3, 2));
        next.Entities.OfType<LineEntity>().Should().Contain(line =>
            line.Id.Value == "bottom" && line.Start == new Point2(0, -2) && line.End == new Point2(3, -2));
    }

    public static TheoryData<DrawingEntity> ApplicableLineTargetCutters() => new()
    {
        new PolylineEntity(
            EntityId.Create("polyline-cutter"),
            new[]
            {
                new Point2(-5, -2),
                new Point2(-5, 2),
                new Point2(5, 2),
                new Point2(5, -2)
            }),
        new PolygonEntity(EntityId.Create("polygon-cutter"), new Point2(0, 0), 5, 0, 4, Circumscribed: false),
        new ArcEntity(EntityId.Create("arc-cutter"), new Point2(0, 0), 5, 0, 180),
        new EllipseEntity(EntityId.Create("ellipse-cutter"), new Point2(0, 0), new Point2(5, 0), 0.5),
        new SplineEntity(
            EntityId.Create("spline-cutter"),
            1,
            new[] { new Point2(-5, -2), new Point2(-5, 2), new Point2(5, 2), new Point2(5, -2) },
            Array.Empty<double>())
    };

    public static TheoryData<DrawingEntity> ApplicableCircleTargetCutters() => new()
    {
        new CircleEntity(EntityId.Create("circle-cutter"), new Point2(0, 5), 5),
        new ArcEntity(EntityId.Create("arc-cutter"), new Point2(0, 5), 5, 180, 360),
        new PolylineEntity(EntityId.Create("polyline-cutter"), new[] { new Point2(-6, 0), new Point2(6, 0) }),
        new PolygonEntity(EntityId.Create("polygon-cutter"), new Point2(0, 0), 7, 45, 4),
        new EllipseEntity(EntityId.Create("ellipse-cutter"), new Point2(0, 0), new Point2(8, 0), 0.25),
        new SplineEntity(
            EntityId.Create("spline-cutter"),
            1,
            new[] { new Point2(-6, 0), new Point2(6, 0) },
            Array.Empty<double>())
    };

    public static TheoryData<DrawingEntity> ApplicableArcTargetCutters() => new()
    {
        new PolylineEntity(
            EntityId.Create("polyline-cutter"),
            new[]
            {
                new Point2(-2.5, 0),
                new Point2(-2.5, 6),
                new Point2(2.5, 6),
                new Point2(2.5, 0)
            }),
        new PolygonEntity(EntityId.Create("polygon-cutter"), new Point2(0, 0), 4.330127018922193, 0, 4, Circumscribed: true),
        new EllipseEntity(EntityId.Create("ellipse-cutter"), new Point2(0, 0), new Point2(6, 0), 0.8),
        new SplineEntity(
            EntityId.Create("spline-cutter"),
            1,
            new[]
            {
                new Point2(-2.5, 0),
                new Point2(-2.5, 6),
                new Point2(2.5, 6),
                new Point2(2.5, 0)
            },
            Array.Empty<double>())
    };

    public static TheoryData<DrawingEntity> ApplicableEllipseTargetCutters() => new()
    {
        new CircleEntity(EntityId.Create("circle-cutter"), new Point2(0, 0), Math.Sqrt(10.9375)),
        new ArcEntity(EntityId.Create("arc-cutter"), new Point2(0, 0), Math.Sqrt(10.9375), 0, 180),
        new PolylineEntity(
            EntityId.Create("polyline-cutter"),
            new[]
            {
                new Point2(-2.5, -3),
                new Point2(-2.5, 3),
                new Point2(2.5, 3),
                new Point2(2.5, -3)
            }),
        new PolygonEntity(EntityId.Create("polygon-cutter"), new Point2(0, 0), 2.5, 0, 4, Circumscribed: true),
        new EllipseEntity(EntityId.Create("ellipse-cutter"), new Point2(0, 0), new Point2(Math.Sqrt(10.9375), 0), 1),
        new SplineEntity(
            EntityId.Create("spline-cutter"),
            1,
            new[]
            {
                new Point2(-2.5, -3),
                new Point2(-2.5, 3),
                new Point2(2.5, 3),
                new Point2(2.5, -3)
            },
            Array.Empty<double>())
    };

    public static TheoryData<DrawingEntity> ApplicableSplineTargetCutters() => new()
    {
        new CircleEntity(EntityId.Create("circle-cutter"), new Point2(0, 0), 5),
        new ArcEntity(EntityId.Create("arc-cutter"), new Point2(0, 0), 5, 0, 180),
        new PolylineEntity(
            EntityId.Create("polyline-cutter"),
            new[]
            {
                new Point2(-5, -2),
                new Point2(-5, 2),
                new Point2(5, 2),
                new Point2(5, -2)
            }),
        new PolygonEntity(EntityId.Create("polygon-cutter"), new Point2(0, 0), 5, 0, 4, Circumscribed: true),
        new EllipseEntity(EntityId.Create("ellipse-cutter"), new Point2(0, 0), new Point2(5, 0), 0.5),
        new SplineEntity(
            EntityId.Create("spline-cutter"),
            1,
            new[]
            {
                new Point2(-5, -2),
                new Point2(-5, 2),
                new Point2(5, 2),
                new Point2(5, -2)
            },
            Array.Empty<double>())
    };

    public static TheoryData<DrawingEntity> ApplicablePolylineTargetCutters() => new()
    {
        new CircleEntity(EntityId.Create("circle-cutter"), new Point2(0, 0), 5),
        new ArcEntity(EntityId.Create("arc-cutter"), new Point2(0, 0), 5, 0, 180),
        new PolylineEntity(
            EntityId.Create("polyline-cutter"),
            new[]
            {
                new Point2(-5, -2),
                new Point2(-5, 2),
                new Point2(5, 2),
                new Point2(5, -2)
            }),
        new PolygonEntity(EntityId.Create("polygon-cutter"), new Point2(0, 0), 5, 0, 4, Circumscribed: true),
        new EllipseEntity(EntityId.Create("ellipse-cutter"), new Point2(0, 0), new Point2(5, 0), 0.5),
        new SplineEntity(
            EntityId.Create("spline-cutter"),
            1,
            new[]
            {
                new Point2(-5, -2),
                new Point2(-5, 2),
                new Point2(5, 2),
                new Point2(5, -2)
            },
            Array.Empty<double>())
    };

    public static TheoryData<DrawingEntity> ApplicablePolygonTargetCutters() => new()
    {
        new LineEntity(EntityId.Create("line-cutter"), new Point2(-2, 3), new Point2(2, 3)),
        new CircleEntity(EntityId.Create("circle-cutter"), new Point2(0, 0), 4),
        new ArcEntity(EntityId.Create("arc-cutter"), new Point2(0, 0), 4, 0, 180),
        new EllipseEntity(EntityId.Create("ellipse-cutter"), new Point2(0, 0), new Point2(6, 0), 0.8),
        new PolylineEntity(
            EntityId.Create("polyline-cutter"),
            new[] { new Point2(-2, 3), new Point2(2, 3) }),
        new PolygonEntity(EntityId.Create("polygon-cutter"), new Point2(0, 3), 2, 45, 4),
        SplineEntity.FromFitPoints(
            EntityId.Create("spline-cutter"),
            new[] { new Point2(-2, 3), new Point2(2, 3) })
    };

    public static TheoryData<DrawingEntity, Point2> ApplicableLineExtendBoundaries() => new()
    {
        {
            new PolylineEntity(EntityId.Create("polyline-boundary"), new[] { new Point2(15, -2), new Point2(15, 2) }),
            new Point2(15, 0)
        },
        {
            new PolygonEntity(EntityId.Create("polygon-boundary"), new Point2(15, 0), 5, 45, 4, Circumscribed: false),
            new Point2(15 - (5 / Math.Sqrt(2)), 0)
        },
        {
            new CircleEntity(EntityId.Create("circle-boundary"), new Point2(15, 0), 5),
            new Point2(20, 0)
        },
        {
            new ArcEntity(EntityId.Create("arc-boundary"), new Point2(15, 0), 5, 0, 180),
            new Point2(20, 0)
        },
        {
            new EllipseEntity(EntityId.Create("ellipse-boundary"), new Point2(15, 0), new Point2(5, 0), 0.5),
            new Point2(20, 0)
        },
        {
            new SplineEntity(
                EntityId.Create("spline-boundary"),
                1,
                new[] { new Point2(15, -2), new Point2(15, 2) },
                Array.Empty<double>()),
            new Point2(15, 0)
        },
        {
            new PointEntity(EntityId.Create("point-boundary"), new Point2(15, 0)),
            new Point2(15, 0)
        }
    };

    public static TheoryData<DrawingEntity, Point2> ApplicablePolylineExtendBoundaries() => new()
    {
        {
            new PolylineEntity(EntityId.Create("polyline-boundary"), new[] { new Point2(15, -2), new Point2(15, 2) }),
            new Point2(15, 0)
        },
        {
            new PolygonEntity(EntityId.Create("polygon-boundary"), new Point2(15, 0), 5, 45, 4, Circumscribed: false),
            new Point2(15 - (5 / Math.Sqrt(2)), 0)
        },
        {
            new CircleEntity(EntityId.Create("circle-boundary"), new Point2(15, 0), 5),
            new Point2(20, 0)
        },
        {
            new ArcEntity(EntityId.Create("arc-boundary"), new Point2(15, 0), 5, 0, 180),
            new Point2(20, 0)
        },
        {
            new EllipseEntity(EntityId.Create("ellipse-boundary"), new Point2(15, 0), new Point2(5, 0), 0.5),
            new Point2(20, 0)
        },
        {
            new SplineEntity(
                EntityId.Create("spline-boundary"),
                1,
                new[] { new Point2(15, -2), new Point2(15, 2) },
                Array.Empty<double>()),
            new Point2(15, 0)
        },
        {
            new PointEntity(EntityId.Create("point-boundary"), new Point2(15, 0)),
            new Point2(15, 0)
        }
    };

    public static TheoryData<DrawingEntity, double> ApplicableArcExtendBoundaries()
    {
        var center = new Point2(0, 0);
        const double radius = 5;
        const double expectedAngle = 120;
        var expected = PointOnCircle(center, radius, expectedAngle);
        var radial = UnitRadial(expectedAngle);
        var tangent = UnitTangent(expectedAngle);
        var tangentCircleCenter = Offset(expected, radial, 2);
        var ellipseBoundaryCenter = Offset(expected, radial, 0.5);
        var polygonCenter = Offset(expected, radial, Math.Sqrt(2));

        return new TheoryData<DrawingEntity, double>
        {
            { new LineEntity(EntityId.Create("line-boundary"), new Point2(expected.X - 2, expected.Y), new Point2(expected.X + 2, expected.Y)), expectedAngle },
            { new PolylineEntity(EntityId.Create("polyline-boundary"), new[] { Offset(expected, tangent, -1), Offset(expected, tangent, 1) }), expectedAngle },
            { new PolygonEntity(EntityId.Create("polygon-boundary"), polygonCenter, 2, expectedAngle + 135, 4), expectedAngle },
            { new CircleEntity(EntityId.Create("circle-boundary"), tangentCircleCenter, 2), expectedAngle },
            { new ArcEntity(EntityId.Create("arc-boundary"), tangentCircleCenter, 2, 260, 340), expectedAngle },
            { new EllipseEntity(EntityId.Create("ellipse-boundary"), ellipseBoundaryCenter, Multiply(radial, 0.5), 0.5, 180, 360), expectedAngle },
            { SplineEntity.FromFitPoints(EntityId.Create("spline-boundary"), new[] { Offset(expected, tangent, -1), Offset(expected, tangent, 1) }), expectedAngle },
            { new PointEntity(EntityId.Create("point-boundary"), expected), expectedAngle }
        };
    }

    public static TheoryData<DrawingEntity, double> ApplicableEllipseArcExtendBoundaries()
    {
        var center = new Point2(0, 0);
        var major = new Point2(5, 0);
        const double ratio = 0.5;
        const double expectedParameter = 120;
        var expected = PointOnEllipse(center, major, ratio, expectedParameter);
        var tangent = UnitEllipseTangent(major, ratio, expectedParameter);
        var normal = UnitEllipseNormal(major, ratio, expectedParameter);
        var circleCenter = Offset(expected, normal, 1);
        var ellipseBoundaryCenter = Offset(expected, normal, 0.5);
        var polygonCenter = Offset(expected, normal, Math.Sqrt(2));
        var normalAngle = Math.Atan2(normal.Y, normal.X) * 180.0 / Math.PI;

        return new TheoryData<DrawingEntity, double>
        {
            { new LineEntity(EntityId.Create("line-boundary"), new Point2(expected.X - 2, expected.Y), new Point2(expected.X + 2, expected.Y)), expectedParameter },
            { new PolylineEntity(EntityId.Create("polyline-boundary"), new[] { Offset(expected, tangent, -1), Offset(expected, tangent, 1) }), expectedParameter },
            { new PolygonEntity(EntityId.Create("polygon-boundary"), polygonCenter, 2, normalAngle + 135, 4), expectedParameter },
            { new CircleEntity(EntityId.Create("circle-boundary"), circleCenter, 1), expectedParameter },
            { new ArcEntity(EntityId.Create("arc-boundary"), circleCenter, 1, normalAngle + 135, normalAngle + 225), expectedParameter },
            { new EllipseEntity(EntityId.Create("ellipse-boundary"), ellipseBoundaryCenter, Multiply(normal, 0.5), 0.5), expectedParameter },
            { SplineEntity.FromFitPoints(EntityId.Create("spline-boundary"), new[] { Offset(expected, tangent, -1), Offset(expected, tangent, 1) }), expectedParameter },
            { new PointEntity(EntityId.Create("point-boundary"), expected), expectedParameter }
        };
    }

    private static void AssertLine(DrawingEntity entity, string id, Point2 expectedStart, Point2 expectedEnd)
    {
        var line = entity.Should().BeOfType<LineEntity>().Subject;
        line.Id.Should().Be(EntityId.Create(id));
        line.Start.X.Should().BeApproximately(expectedStart.X, 0.000001);
        line.Start.Y.Should().BeApproximately(expectedStart.Y, 0.000001);
        line.End.X.Should().BeApproximately(expectedEnd.X, 0.000001);
        line.End.Y.Should().BeApproximately(expectedEnd.Y, 0.000001);
    }

    private static void AssertSpline(DrawingEntity entity, string id, Point2 expectedStart, Point2 expectedEnd)
    {
        var spline = entity.Should().BeOfType<SplineEntity>().Subject;
        spline.Id.Should().Be(EntityId.Create(id));
        var samples = spline.GetSamplePoints();
        samples.Should().HaveCountGreaterThanOrEqualTo(2);
        samples[0].X.Should().BeApproximately(expectedStart.X, 0.000001);
        samples[0].Y.Should().BeApproximately(expectedStart.Y, 0.000001);
        samples[^1].X.Should().BeApproximately(expectedEnd.X, 0.000001);
        samples[^1].Y.Should().BeApproximately(expectedEnd.Y, 0.000001);
    }

    private static SplineEntity CreateBezierSpline(string id, IReadOnlyList<Point2> controlPoints) =>
        new(
            EntityId.Create(id),
            3,
            controlPoints,
            new[] { 0d, 0d, 0d, 0d, 1d, 1d, 1d, 1d });

    private static Point2 EvaluateCubicBezier(IReadOnlyList<Point2> controlPoints, double parameter)
    {
        var oneMinusT = 1.0 - parameter;
        var b0 = oneMinusT * oneMinusT * oneMinusT;
        var b1 = 3.0 * oneMinusT * oneMinusT * parameter;
        var b2 = 3.0 * oneMinusT * parameter * parameter;
        var b3 = parameter * parameter * parameter;
        return new Point2(
            (controlPoints[0].X * b0) + (controlPoints[1].X * b1) + (controlPoints[2].X * b2) + (controlPoints[3].X * b3),
            (controlPoints[0].Y * b0) + (controlPoints[1].Y * b1) + (controlPoints[2].Y * b2) + (controlPoints[3].Y * b3));
    }

    private static void AssertPolyline(DrawingEntity entity, string id, Point2 expectedStart, Point2 expectedEnd)
    {
        var polyline = entity.Should().BeOfType<PolylineEntity>().Subject;
        polyline.Id.Should().Be(EntityId.Create(id));
        polyline.Vertices.Should().HaveCountGreaterThanOrEqualTo(2);
        polyline.Vertices[0].X.Should().BeApproximately(expectedStart.X, 0.000001);
        polyline.Vertices[0].Y.Should().BeApproximately(expectedStart.Y, 0.000001);
        polyline.Vertices[^1].X.Should().BeApproximately(expectedEnd.X, 0.000001);
        polyline.Vertices[^1].Y.Should().BeApproximately(expectedEnd.Y, 0.000001);
    }

    private static void AssertArc(
        DrawingEntity entity,
        string id,
        Point2 expectedCenter,
        double expectedRadius,
        double expectedStartAngle,
        double expectedEndAngle)
    {
        var arc = entity.Should().BeOfType<ArcEntity>().Subject;
        arc.Id.Should().Be(EntityId.Create(id));
        arc.Center.Should().Be(expectedCenter);
        arc.Radius.Should().BeApproximately(expectedRadius, 0.000001);
        arc.StartAngleDegrees.Should().BeApproximately(expectedStartAngle, 0.000001);
        arc.EndAngleDegrees.Should().BeApproximately(expectedEndAngle, 0.000001);
    }

    private static void AssertEllipse(
        DrawingEntity entity,
        string id,
        Point2 expectedCenter,
        Point2 expectedMajorAxisEndPoint,
        double expectedMinorRadiusRatio,
        double expectedStartParameter,
        double expectedEndParameter)
    {
        var ellipse = entity.Should().BeOfType<EllipseEntity>().Subject;
        ellipse.Id.Should().Be(EntityId.Create(id));
        ellipse.Center.Should().Be(expectedCenter);
        ellipse.MajorAxisEndPoint.Should().Be(expectedMajorAxisEndPoint);
        ellipse.MinorRadiusRatio.Should().BeApproximately(expectedMinorRadiusRatio, 0.000001);
        ellipse.StartParameterDegrees.Should().BeApproximately(expectedStartParameter, 0.000001);
        ellipse.EndParameterDegrees.Should().BeApproximately(expectedEndParameter, 0.000001);
    }

    private static Point2 PointOnCircle(Point2 center, double radius, double angleDegrees)
    {
        var radians = angleDegrees * Math.PI / 180.0;
        return new Point2(
            center.X + Math.Cos(radians) * radius,
            center.Y + Math.Sin(radians) * radius);
    }

    private static Point2 PointOnEllipse(Point2 center, Point2 majorAxisEndPoint, double minorRadiusRatio, double parameterDegrees)
    {
        var majorLength = Math.Sqrt((majorAxisEndPoint.X * majorAxisEndPoint.X) + (majorAxisEndPoint.Y * majorAxisEndPoint.Y));
        var majorUnit = new Point2(majorAxisEndPoint.X / majorLength, majorAxisEndPoint.Y / majorLength);
        var minorUnit = new Point2(-majorUnit.Y, majorUnit.X);
        var minorLength = majorLength * minorRadiusRatio;
        var radians = parameterDegrees * Math.PI / 180.0;
        return new Point2(
            center.X + (majorUnit.X * majorLength * Math.Cos(radians)) + (minorUnit.X * minorLength * Math.Sin(radians)),
            center.Y + (majorUnit.Y * majorLength * Math.Cos(radians)) + (minorUnit.Y * minorLength * Math.Sin(radians)));
    }

    private static Point2 UnitTangent(double angleDegrees)
    {
        var radians = angleDegrees * Math.PI / 180.0;
        return new Point2(-Math.Sin(radians), Math.Cos(radians));
    }

    private static Point2 UnitRadial(double angleDegrees)
    {
        var radians = angleDegrees * Math.PI / 180.0;
        return new Point2(Math.Cos(radians), Math.Sin(radians));
    }

    private static Point2 UnitEllipseTangent(Point2 majorAxisEndPoint, double minorRadiusRatio, double parameterDegrees)
    {
        var majorLength = Math.Sqrt((majorAxisEndPoint.X * majorAxisEndPoint.X) + (majorAxisEndPoint.Y * majorAxisEndPoint.Y));
        var majorUnit = new Point2(majorAxisEndPoint.X / majorLength, majorAxisEndPoint.Y / majorLength);
        var minorUnit = new Point2(-majorUnit.Y, majorUnit.X);
        var minorLength = majorLength * minorRadiusRatio;
        var radians = parameterDegrees * Math.PI / 180.0;
        var tangent = new Point2(
            (-majorUnit.X * majorLength * Math.Sin(radians)) + (minorUnit.X * minorLength * Math.Cos(radians)),
            (-majorUnit.Y * majorLength * Math.Sin(radians)) + (minorUnit.Y * minorLength * Math.Cos(radians)));
        return Normalize(tangent);
    }

    private static Point2 UnitEllipseNormal(Point2 majorAxisEndPoint, double minorRadiusRatio, double parameterDegrees)
    {
        var majorLength = Math.Sqrt((majorAxisEndPoint.X * majorAxisEndPoint.X) + (majorAxisEndPoint.Y * majorAxisEndPoint.Y));
        var minorLength = majorLength * minorRadiusRatio;
        var radians = parameterDegrees * Math.PI / 180.0;
        return Normalize(new Point2(Math.Cos(radians) / majorLength, Math.Sin(radians) / minorLength));
    }

    private static Point2 Offset(Point2 point, Point2 direction, double distance) =>
        new(point.X + (direction.X * distance), point.Y + (direction.Y * distance));

    private static Point2 Multiply(Point2 vector, double scale) =>
        new(vector.X * scale, vector.Y * scale);

    private static Point2 Normalize(Point2 vector)
    {
        var length = Math.Sqrt((vector.X * vector.X) + (vector.Y * vector.Y));
        return new Point2(vector.X / length, vector.Y / length);
    }

    private static Point2 FindCatmullRomHorizontalCrossing(IReadOnlyList<Point2> fitPoints, int spanIndex)
    {
        var previous = spanIndex == 0 ? fitPoints[spanIndex] : fitPoints[spanIndex - 1];
        var start = fitPoints[spanIndex];
        var end = fitPoints[spanIndex + 1];
        var next = spanIndex + 2 < fitPoints.Count ? fitPoints[spanIndex + 2] : end;
        var low = 0.0;
        var high = 1.0;

        for (var index = 0; index < 80; index++)
        {
            var middle = (low + high) / 2.0;
            var point = EvaluateCatmullRom(previous, start, end, next, middle);
            if (Math.Sign(point.Y) == Math.Sign(start.Y))
            {
                low = middle;
            }
            else
            {
                high = middle;
            }
        }

        return EvaluateCatmullRom(previous, start, end, next, (low + high) / 2.0);
    }

    private static Point2 FindCatmullRomVerticalCrossing(IReadOnlyList<Point2> fitPoints, int spanIndex, double x)
    {
        var previous = spanIndex == 0 ? fitPoints[spanIndex] : fitPoints[spanIndex - 1];
        var start = fitPoints[spanIndex];
        var end = fitPoints[spanIndex + 1];
        var next = spanIndex + 2 < fitPoints.Count ? fitPoints[spanIndex + 2] : end;
        var low = 0.0;
        var high = 1.0;
        for (var index = 0; index < 80; index++)
        {
            var middle = (low + high) / 2.0;
            var point = EvaluateCatmullRom(previous, start, end, next, middle);
            if (point.X < x)
            {
                low = middle;
            }
            else
            {
                high = middle;
            }
        }

        return EvaluateCatmullRom(previous, start, end, next, (low + high) / 2.0);
    }

    private static IReadOnlyList<Point2> FindCatmullRomCircleCrossings(
        IReadOnlyList<Point2> fitPoints,
        Point2 center,
        double radius)
    {
        var points = new List<Point2>();
        for (var spanIndex = 0; spanIndex < fitPoints.Count - 1; spanIndex++)
        {
            var previous = spanIndex == 0 ? fitPoints[spanIndex] : fitPoints[spanIndex - 1];
            var start = fitPoints[spanIndex];
            var end = fitPoints[spanIndex + 1];
            var next = spanIndex + 2 < fitPoints.Count ? fitPoints[spanIndex + 2] : end;
            const int searchIntervals = 128;
            var low = 0.0;
            var lowValue = CircleImplicit(EvaluateCatmullRom(previous, start, end, next, low), center, radius);

            for (var interval = 1; interval <= searchIntervals; interval++)
            {
                var high = (double)interval / searchIntervals;
                var highValue = CircleImplicit(EvaluateCatmullRom(previous, start, end, next, high), center, radius);
                if ((lowValue < 0 && highValue > 0) || (lowValue > 0 && highValue < 0))
                {
                    points.Add(FindCatmullRomCircleRoot(previous, start, end, next, center, radius, low, high));
                }

                low = high;
                lowValue = highValue;
            }
        }

        return points;
    }

    private static Point2 FindCatmullRomCircleRoot(
        Point2 previous,
        Point2 start,
        Point2 end,
        Point2 next,
        Point2 center,
        double radius,
        double low,
        double high)
    {
        var lowValue = CircleImplicit(EvaluateCatmullRom(previous, start, end, next, low), center, radius);
        for (var index = 0; index < 80; index++)
        {
            var middle = (low + high) / 2.0;
            var middleValue = CircleImplicit(EvaluateCatmullRom(previous, start, end, next, middle), center, radius);
            if ((lowValue < 0 && middleValue > 0) || (lowValue > 0 && middleValue < 0))
            {
                high = middle;
            }
            else
            {
                low = middle;
                lowValue = middleValue;
            }
        }

        return EvaluateCatmullRom(previous, start, end, next, (low + high) / 2.0);
    }

    private static double CircleImplicit(Point2 point, Point2 center, double radius)
    {
        var deltaX = point.X - center.X;
        var deltaY = point.Y - center.Y;
        return (deltaX * deltaX) + (deltaY * deltaY) - (radius * radius);
    }

    private static IReadOnlyList<Point2> FindCatmullRomEllipseCrossings(
        IReadOnlyList<Point2> fitPoints,
        EllipseEntity ellipse)
    {
        var points = new List<Point2>();
        for (var spanIndex = 0; spanIndex < fitPoints.Count - 1; spanIndex++)
        {
            var previous = spanIndex == 0 ? fitPoints[spanIndex] : fitPoints[spanIndex - 1];
            var start = fitPoints[spanIndex];
            var end = fitPoints[spanIndex + 1];
            var next = spanIndex + 2 < fitPoints.Count ? fitPoints[spanIndex + 2] : end;
            const int searchIntervals = 128;
            var low = 0.0;
            var lowValue = EllipseImplicit(EvaluateCatmullRom(previous, start, end, next, low), ellipse);

            for (var interval = 1; interval <= searchIntervals; interval++)
            {
                var high = (double)interval / searchIntervals;
                var highValue = EllipseImplicit(EvaluateCatmullRom(previous, start, end, next, high), ellipse);
                if ((lowValue < 0 && highValue > 0) || (lowValue > 0 && highValue < 0))
                {
                    points.Add(FindCatmullRomEllipseRoot(previous, start, end, next, ellipse, low, high));
                }

                low = high;
                lowValue = highValue;
            }
        }

        return points;
    }

    private static Point2 FindCatmullRomEllipseRoot(
        Point2 previous,
        Point2 start,
        Point2 end,
        Point2 next,
        EllipseEntity ellipse,
        double low,
        double high)
    {
        var lowValue = EllipseImplicit(EvaluateCatmullRom(previous, start, end, next, low), ellipse);
        for (var index = 0; index < 80; index++)
        {
            var middle = (low + high) / 2.0;
            var middleValue = EllipseImplicit(EvaluateCatmullRom(previous, start, end, next, middle), ellipse);
            if ((lowValue < 0 && middleValue > 0) || (lowValue > 0 && middleValue < 0))
            {
                high = middle;
            }
            else
            {
                low = middle;
                lowValue = middleValue;
            }
        }

        return EvaluateCatmullRom(previous, start, end, next, (low + high) / 2.0);
    }

    private static double EllipseImplicit(Point2 point, EllipseEntity ellipse)
    {
        var majorLength = Math.Sqrt((ellipse.MajorAxisEndPoint.X * ellipse.MajorAxisEndPoint.X)
            + (ellipse.MajorAxisEndPoint.Y * ellipse.MajorAxisEndPoint.Y));
        var minorLength = majorLength * ellipse.MinorRadiusRatio;
        var majorUnit = new Point2(ellipse.MajorAxisEndPoint.X / majorLength, ellipse.MajorAxisEndPoint.Y / majorLength);
        var minorUnit = new Point2(-majorUnit.Y, majorUnit.X);
        var offset = new Point2(point.X - ellipse.Center.X, point.Y - ellipse.Center.Y);
        var major = ((offset.X * majorUnit.X) + (offset.Y * majorUnit.Y)) / majorLength;
        var minor = ((offset.X * minorUnit.X) + (offset.Y * minorUnit.Y)) / minorLength;
        return (major * major) + (minor * minor) - 1.0;
    }

    private static double GetAxisAlignedEllipseParameterDegrees(Point2 point, double majorRadius, double minorRadius) =>
        NormalizeTestAngleDegrees(Math.Atan2(point.Y / minorRadius, point.X / majorRadius) * 180.0 / Math.PI);

    private static double NormalizeTestAngleDegrees(double degrees)
    {
        var normalized = degrees % 360.0;
        if (normalized < 0)
        {
            normalized += 360.0;
        }

        return normalized;
    }

    private static Point2 EvaluateCatmullRom(Point2 previous, Point2 start, Point2 end, Point2 next, double t)
    {
        var t2 = t * t;
        var t3 = t2 * t;
        return new Point2(
            0.5 * ((2 * start.X)
                + (-previous.X + end.X) * t
                + ((2 * previous.X) - (5 * start.X) + (4 * end.X) - next.X) * t2
                + (-previous.X + (3 * start.X) - (3 * end.X) + next.X) * t3),
            0.5 * ((2 * start.Y)
                + (-previous.Y + end.Y) * t
                + ((2 * previous.Y) - (5 * start.Y) + (4 * end.Y) - next.Y) * t2
                + (-previous.Y + (3 * start.Y) - (3 * end.Y) + next.Y) * t3));
    }
}

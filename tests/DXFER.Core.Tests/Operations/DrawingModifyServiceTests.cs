using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Operations;
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
    public void PowerTrimDeletesPickedCurveWhenNoCurveTrimSolverExists()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new ArcEntity(EntityId.Create("target"), new Point2(0, 0), 5, 0, 90),
            new LineEntity(EntityId.Create("other"), new Point2(20, 0), new Point2(30, 0))
        });

        var trimmed = DrawingModifyService.TryPowerTrimOrExtendLine(
            document,
            "target",
            new Point2(3, 3),
            prefix => EntityId.Create($"{prefix}-split"),
            out var next);

        trimmed.Should().BeTrue();
        next.Entities.Should().ContainSingle()
            .Which.Should().Be(new LineEntity(EntityId.Create("other"), new Point2(20, 0), new Point2(30, 0)));
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
}

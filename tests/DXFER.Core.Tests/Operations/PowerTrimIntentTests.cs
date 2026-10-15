using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Operations;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Operations;

public sealed class PowerTrimIntentTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void SameCountReorderingCannotRebindEqualLengthSegmentIntent(bool closed, bool driving)
    {
        // This model encodes closure by repeating the first vertex; it has no IsClosed flag.
        var vertices = closed
            ? new Point2[] { new(0, 0), new(10, 0), new(10, 10), new(0, 10), new(0, 0) }
            : new Point2[] { new(0, 0), new(10, 0), new(10, 10), new(0, 10) };
        var rotated = closed
            ? new Point2[] { new(10, 0), new(10, 10), new(0, 10), new(0, 0), new(0, 5) }
            : new Point2[] { new(10, 0), new(10, 10), new(0, 10), new(0, 0) };
        var original = new DrawingDocument(new DrawingEntity[] { new PolylineEntity(EntityId.Create("target"), vertices) },
            new[] { new SketchDimension("bottom", SketchDimensionKind.LinearDistance,
                new[] { "target|segment|0:start", "target|segment|0:end" }, 10, isDriving: driving) }, Array.Empty<SketchConstraint>());
        var candidate = new DrawingDocument(new DrawingEntity[] { new PolylineEntity(EntityId.Create("target"), rotated) },
            original.Dimensions, original.Constraints);
        SketchDimensionSolverService.IsDimensionSatisfied(candidate, original.Dimensions[0]).Should().BeTrue(
            "the unrelated right edge also measures 10, so a value-only check cannot protect identity");
        var args = new object?[] { original, candidate, "target", null };
        var finalize = typeof(DrawingModifyService).GetMethod("TryFinalizeTrimReplacement",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        finalize.Invoke(null, args).Should().Be(false);
        args[3].Should().BeSameAs(original);
    }

    [Fact]
    public void ClosedRectanglePublicTrimPreservesAttachedBottomDimension()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new PolylineEntity(EntityId.Create("target"), new Point2[]
                { new(0, 0), new(10, 0), new(10, 10), new(0, 10), new(0, 0) })
        }, new[] { new SketchDimension("bottom", SketchDimensionKind.LinearDistance,
            new[] { "target|segment|0:start", "target|segment|0:end" }, 10, isDriving: true) }, Array.Empty<SketchConstraint>());
        Apply(document, new(5, 0), out var next).Should().BeFalse();
        next.Should().BeSameAs(document);
    }

    public static TheoryData<string> ChangedSpans => new()
    {
        "line-trim", "line-split", "line-extend", "polyline-trim", "arc-trim", "arc-extend",
        "ellipse-trim", "ellipse-extend", "spline-trim", "spline-linear-extend"
    };

    [Theory]
    [MemberData(nameof(ChangedSpans))]
    public void ChangedDrivingSpanRefusesAtomically(string operation)
    {
        var (document, pick) = Fixture(operation, true);
        DrawingModifyService.TryPowerTrimOrExtendLine(document, "target", pick,
            name => EntityId.Create(name + "-new"), out var next, out var status).Should().BeFalse();
        status.Should().Contain("dimensions, constraints, or their references").And.Contain("Geometry unchanged");
        next.Should().BeSameAs(document);
    }

    [Theory]
    [MemberData(nameof(ChangedSpans))]
    public void ReferenceSpanRefreshesAndKeepsExactUnconstrainedGeometry(string operation)
    {
        var (document, pick) = Fixture(operation, false);
        Apply(new DrawingDocument(document.Entities), pick, out var expected).Should().BeTrue();
        Apply(document, pick, out var next).Should().BeTrue();
        next.Entities.Should().BeEquivalentTo(expected.Entities);
        var original = document.Dimensions.Single();
        var dimension = next.Dimensions.Single();
        dimension.Id.Should().Be(original.Id);
        dimension.Kind.Should().Be(original.Kind);
        dimension.Anchor.Should().Be(original.Anchor);
        dimension.IsDriving.Should().BeFalse();
        SketchDimensionSolverService.TryGetDimensionMeasurement(next, dimension, out var value).Should().BeTrue();
        dimension.Value.Should().BeApproximately(value, 1e-9);
        dimension.Value.Should().NotBe(original.Value);
    }

    [Theory]
    [InlineData("line")]
    [InlineData("point")]
    [InlineData("polygon")]
    [InlineData("polyline")]
    [InlineData("arc")]
    [InlineData("ellipse")]
    [InlineData("spline")]
    public void RemovingTargetCannotSilentlyRemoveDimensionsOrConstraints(string type)
    {
        DrawingEntity target = type switch
        {
            "point" => new PointEntity(EntityId.Create("target"), new(0, 0)),
            "polygon" => new PolygonEntity(EntityId.Create("target"), new(0, 0), 5, 0, 4),
            "polyline" => new PolylineEntity(EntityId.Create("target"), new Point2[] { new(0, 0), new(10, 0) }, false),
            "arc" => new ArcEntity(EntityId.Create("target"), new(0, 0), 5, 0, 180),
            "ellipse" => new EllipseEntity(EntityId.Create("target"), new(0, 0), new(5, 0), .6, 0, 180),
            "spline" => new SplineEntity(EntityId.Create("target"), 1, new Point2[] { new(0, 0), new(10, 0) },
                new double[] { 0, 0, 1, 1 }),
            _ => new LineEntity(EntityId.Create("target"), new(0, 0), new(10, 0))
        };
        var pick = type switch { "polygon" => new Point2(3, 2), "arc" => new(0, 5), "ellipse" => new(0, 3), _ => new(5, 0) };
        foreach (var intent in new[] { "driving", "reference", "fix", "horizontal" })
        {
            var dimension = new SketchDimension("intent", type == "polygon" ? SketchDimensionKind.Radius : SketchDimensionKind.LinearDistance,
                new[] { "target" }, 5, isDriving: intent == "driving");
            var constraint = new SketchConstraint("intent", intent == "fix" ? SketchConstraintKind.Fix : SketchConstraintKind.Horizontal,
                new[] { "target" }, SketchConstraintState.Satisfied);
            var entities = type == "polygon"
                ? new DrawingEntity[] { target, Cutter("first", 4), Cutter("second", 2) }
                : new[] { target };
            Apply(new DrawingDocument(entities), pick, out _).Should().BeTrue("the unprotected edit must actually be applicable");
            var document = new DrawingDocument(entities,
                intent is "driving" or "reference" ? new[] { dimension } : Array.Empty<SketchDimension>(),
                intent is "fix" or "horizontal" ? new[] { constraint } : Array.Empty<SketchConstraint>());
            Apply(document, pick, out var next).Should().BeFalse();
            next.Should().BeSameAs(document);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnresolvableAffectedDimensionRejectsInsteadOfDisappearing(bool driving)
    {
        var (source, pick) = Fixture("line-trim", false);
        var document = new DrawingDocument(source.Entities, new[]
        {
            new SketchDimension("bad", SketchDimensionKind.LinearDistance,
                new[] { "target|point|missing|0|0", "target:end" }, 10, isDriving: driving)
        }, Array.Empty<SketchConstraint>());
        Apply(document, pick, out var next).Should().BeFalse();
        next.Should().BeSameAs(document);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompatibleZeroAxisDimensionSurvives(bool driving)
    {
        var (source, pick) = Fixture("line-trim", false);
        var dimension = new SketchDimension("zero", SketchDimensionKind.VerticalDistance,
            new[] { "target:start", "target:end" }, 0, isDriving: driving);
        var document = new DrawingDocument(source.Entities, new[] { dimension }, Array.Empty<SketchConstraint>());
        Apply(document, pick, out var next).Should().BeTrue();
        next.Dimensions.Single().Should().BeEquivalentTo(dimension);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CircleTrimKeepsCompatibleRadiusIntent(bool driving)
    {
        var dimension = new SketchDimension("radius", SketchDimensionKind.Radius, new[] { "target" }, 5, isDriving: driving);
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new CircleEntity(EntityId.Create("target"), new(0, 0), 5), Cutter("cut", 0)
        }, new[] { dimension }, Array.Empty<SketchConstraint>());
        Apply(document, new(5, 0), out var next).Should().BeTrue();
        next.Entities[0].Should().BeOfType<ArcEntity>();
        next.Dimensions.Single().Should().BeEquivalentTo(dimension);
    }

    [Theory]
    [InlineData("fix-edge")]
    [InlineData("fix-canvas")]
    [InlineData("equal")]
    [InlineData("coincident")]
    public void PreviouslySatisfiedConstraintsCannotBeBroken(string intent)
    {
        var (source, pick) = Fixture("line-trim", false);
        var constraint = intent switch
        {
            "fix-edge" => new SketchConstraint("c", SketchConstraintKind.Fix, new[] { "target" }),
            "fix-canvas" => new SketchConstraint("c", SketchConstraintKind.Fix, new[] { "target|point|end|10|0" }),
            "equal" => new SketchConstraint("c", SketchConstraintKind.Equal, new[] { "target", "peer" }),
            _ => new SketchConstraint("c", SketchConstraintKind.Coincident, new[] { "target:end", "peer:end" })
        };
        var document = new DrawingDocument(source.Entities.Append(new LineEntity(EntityId.Create("peer"), new(0, 0), new(10, 0))),
            Array.Empty<SketchDimension>(), new[] { constraint });
        Apply(document, pick, out var next).Should().BeFalse();
        next.Should().BeSameAs(document);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PolylineExtensionCannotRebindAnIndexedReferenceToANewSegment(bool driving)
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new PolylineEntity(EntityId.Create("target"), new Point2[] { new(0, 0), new(10, 0) }, false), Cutter("boundary", -10)
        }, new[] { new SketchDimension("length", SketchDimensionKind.LinearDistance,
            new[] { "target|segment|0:start", "target|segment|0:end" }, 10, isDriving: driving) }, Array.Empty<SketchConstraint>());
        Apply(document, new(-1, 0), out var next).Should().BeFalse();
        next.Should().BeSameAs(document);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StandaloneLineSplitUsesTheSameIntentPolicy(bool driving)
    {
        var (document, _) = Fixture("line-trim", driving);
        LineSplitService.TrySplitLineAtPoint(document, "target", new(3, 0), EntityId.Create("second"), out var next)
            .Should().Be(!driving);
        if (driving) next.Should().BeSameAs(document);
        else next.Dimensions.Single().Value.Should().Be(3);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StandaloneArcSplitUsesTheSameIntentPolicy(bool driving)
    {
        var (document, _) = Fixture("arc-trim", driving);
        CurveSplitService.TrySplitArcAtPoint(document, "target", new(0, 5), EntityId.Create("second"), out var next)
            .Should().Be(!driving);
        if (driving) next.Should().BeSameAs(document);
        else next.Dimensions.Single().Value.Should().BeApproximately(Math.Sqrt(50), 1e-9);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StandaloneCircleSplitKeepsRadiusButRefusesFixedGeometry(bool fixedGeometry)
    {
        var dimension = new SketchDimension("radius", SketchDimensionKind.Radius, new[] { "target" }, 5, isDriving: true);
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new CircleEntity(EntityId.Create("target"), new(0, 0), 5)
        }, new[] { dimension }, fixedGeometry
            ? new[] { new SketchConstraint("fixed", SketchConstraintKind.Fix, new[] { "target" }) }
            : Array.Empty<SketchConstraint>());
        CurveSplitService.TrySplitCircleAtPoints(document, "target", new(5, 0), new(-5, 0),
            EntityId.Create("second"), out var next).Should().Be(!fixedGeometry);
        if (fixedGeometry) next.Should().BeSameAs(document);
        else
        {
            next.Entities.Should().HaveCount(2).And.OnlyContain(e => e is ArcEntity);
            next.Dimensions.Single().Should().BeEquivalentTo(dimension);
        }
    }

    [Fact]
    public void UnaffectedIntentAndCompatibleActiveConstraintsStayIntact()
    {
        var (source, pick) = Fixture("line-trim", false);
        var unrelated = new SketchDimension("other-length", SketchDimensionKind.LinearDistance, new[] { "other" }, 10, isDriving: true);
        var constraints = new[]
        {
            new SketchConstraint("other-fixed", SketchConstraintKind.Fix, new[] { "other" }),
            new SketchConstraint("horizontal", SketchConstraintKind.Horizontal, new[] { "target" }),
            new SketchConstraint("start-fixed", SketchConstraintKind.Fix, new[] { "target:start" })
        };
        var document = new DrawingDocument(source.Entities.Append(new LineEntity(EntityId.Create("other"), new(20, 20), new(30, 20))),
            source.Dimensions.Append(unrelated), constraints);
        Apply(document, pick, out var next).Should().BeTrue();
        next.Entities.Last().Should().BeSameAs(document.Entities.Last());
        next.Dimensions.Last().Should().BeSameAs(unrelated);
        next.Constraints.Should().BeEquivalentTo(constraints);
        next.Constraints.Select(c => SketchConstraintService.ValidateConstraint(next, c).State)
            .Should().OnlyContain(state => state == SketchConstraintState.Satisfied);
    }

    [Fact]
    public void BatchSkipsRefusedPickWithoutLosingEarlierOrLaterValidEdits()
    {
        var (source, _) = Fixture("line-trim", true);
        var document = new DrawingDocument(source.Entities.Concat(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("first"), new(20, 0), new(30, 0)),
            new LineEntity(EntityId.Create("last"), new(40, 0), new(50, 0))
        }), source.Dimensions, source.Constraints);
        DrawingModifyService.PowerTrimOrExtendLines(document, new[]
        {
            new PowerTrimLinePick("first", new(25, 0)), new PowerTrimLinePick("target", new(7, 0)),
            new PowerTrimLinePick("last", new(45, 0))
        }, name => EntityId.Create(name + "-new"), out var next).Should().Be(2);
        next.Entities.Should().Equal(source.Entities);
        next.Dimensions.Should().Equal(source.Dimensions);
    }

    private static bool Apply(DrawingDocument document, Point2 pick, out DrawingDocument next) =>
        DrawingModifyService.TryPowerTrimOrExtendLine(document, "target", pick, name => EntityId.Create(name + "-new"), out next);

    private static LineEntity Cutter(string id, double x) => new(EntityId.Create(id), new(x, -10), new(x, 10));

    private static (DrawingDocument Document, Point2 Pick) Fixture(string operation, bool driving)
    {
        DrawingEntity target = new LineEntity(EntityId.Create("target"), new(0, 0), new(10, 0));
        var cutters = new List<DrawingEntity> { Cutter("cut", 3) };
        var pick = new Point2(7, 0);
        var keys = new[] { "target:start", "target:end" };
        var length = 10.0;
        switch (operation)
        {
            case "line-split": cutters.Add(Cutter("right", 7)); pick = new(5, 0); break;
            case "line-extend": cutters = new() { Cutter("boundary", 15) }; pick = new(11, 0); break;
            case "polyline-trim":
                target = new PolylineEntity(target.Id, new Point2[] { new(0, 0), new(10, 0) }, false);
                keys = new[] { "target|segment|0:start", "target|segment|0:end" }; break;
            case "arc-trim":
                target = new ArcEntity(target.Id, new(0, 0), 5, 0, 180);
                cutters = new() { Cutter("cut", 0) }; pick = new(3, 4); break;
            case "arc-extend":
                target = new ArcEntity(target.Id, new(0, 0), 5, 0, 90);
                cutters = new() { Cutter("boundary", -3) }; pick = new(-2.5, Math.Sqrt(18.75)); length = Math.Sqrt(50); break;
            case "ellipse-trim":
                var ellipse = new EllipseEntity(target.Id, new(0, 0), new(5, 0), .6, 0, 180);
                target = ellipse; cutters = new() { Cutter("cut", 0) }; pick = ellipse.PointAtParameterDegrees(45);
                keys = new[] { "target|point|start|5|0", "target|point|end|-5|0" }; break;
            case "ellipse-extend":
                var partial = new EllipseEntity(target.Id, new(0, 0), new(5, 0), .6, 0, 90);
                target = partial; cutters = new() { Cutter("boundary", -3) }; pick = partial.PointAtParameterDegrees(120);
                length = Math.Sqrt(34); keys = new[] { "target|point|start|5|0", "target|point|end|0|3" }; break;
            case "spline-linear-extend":
                target = new SplineEntity(target.Id, 1, new Point2[] { new(0, 0), new(10, 0) }, new double[] { 0, 0, 1, 1 });
                cutters = new() { Cutter("boundary", 15) }; pick = new(11, 0);
                keys = new[] { "target|point|start|0|0", "target|point|end|10|0" }; break;
            case "spline-trim":
                var spline = new SplineEntity(target.Id, 3, new Point2[] { new(0, 0), new(0, 4), new(4, 4), new(4, 0) },
                    new double[] { 0, 0, 0, 0, 1, 1, 1, 1 });
                target = spline; cutters = new() { Cutter("cut", 2) }; pick = spline.GetSamplePoints()[5]; length = 4;
                keys = new[] { "target|point|start|0|0", "target|point|end|4|0" }; break;
        }
        return (new DrawingDocument(new[] { target }.Concat(cutters), new[]
        {
            new SketchDimension("length", SketchDimensionKind.LinearDistance, keys, length, new(5, -2), driving)
        }, Array.Empty<SketchConstraint>()), pick);
    }
}

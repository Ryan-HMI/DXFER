using DXFER.Blazor.Sketching;
using DXFER.CadIO;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.IO;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class SketchCreationCircleTangentTests
{
    [Theory]
    [InlineData("line")]
    [InlineData("polyline")]
    [InlineData("circle")]
    [InlineData("arc")]
    public void ExplicitSnapCreatesOneSatisfiedTangentWithoutMovingGeometry(string kind)
    {
        var (target, circle, key) = Fixture(kind);
        var constraints = Infer(new[] { target }, circle, key);
        var tangent = constraints.Should().ContainSingle(c => c.Kind == SketchConstraintKind.Tangent).Which;
        tangent.ReferenceKeys.Should().Equal(key, "new-circle");
        tangent.State.Should().Be(SketchConstraintState.Satisfied);
        var dimension = new SketchDimension("radius", SketchDimensionKind.Radius, new[] { "new-circle" }, circle.Radius, isDriving: true);
        var document = new DrawingDocument(new[] { target, circle }, new[] { dimension }, Array.Empty<SketchConstraint>());
        var result = new LegacySketchSolverAdapter().Solve(new SketchSolveRequest(document, constraints, Array.Empty<SketchDimension>()));
        result.Status.Should().Be(SketchSolveStatus.Solved);
        result.Document.Entities.Should().Equal(document.Entities);
        result.Document.Dimensions.Should().Equal(document.Dimensions);
        var reopened = DxfDocumentReader.Read(DxfDocumentWriter.Write(result.Document, new DxfWriteOptions(IncludeNativeDocument: true)));
        reopened.Constraints.Single(c => c.Kind == SketchConstraintKind.Tangent).ReferenceKeys.Should().Equal(key, "new-circle");
        DxferDocumentSerializer.Write(reopened).Should().Be(DxferDocumentSerializer.Write(result.Document));
    }

    [Theory]
    [InlineData("line")]
    [InlineData("polyline")]
    [InlineData("circle")]
    [InlineData("arc")]
    public void ExactTangencyWithoutExplicitIntentDoesNotPersistConstraint(string kind)
    {
        var (target, circle, _) = Fixture(kind);
        Tangents(Infer(new[] { target }, circle, null)).Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("missing")]
    [InlineData("target:start")]
    [InlineData("target:center")]
    [InlineData("target|point|center|0|0")]
    [InlineData("target|segment|0")]
    [InlineData("new-circle")]
    [InlineData("$origin:center")]
    public void InvalidOrMissingTargetDoesNotProduceTangent(string? key)
    {
        var (target, circle, _) = Fixture("line");
        Tangents(Infer(new[] { target }, circle, key)).Should().BeEmpty();
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(.0000005, true)]
    [InlineData(-.0000005, true)]
    [InlineData(.000002, false)]
    [InlineData(-.000002, false)]
    [InlineData(.01, false)]
    public void ExactGeometryToleranceIsIndependentOfVisualSnapRadius(double radiusOffset, bool accepted)
    {
        var (target, circle, key) = Fixture("line");
        Tangents(Infer(new[] { target }, circle with { Radius = circle.Radius + radiusOffset }, key)).Should().HaveCount(accepted ? 1 : 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FiniteLineContactMustLieWithinSelectedSegment(bool reverse)
    {
        var line = new LineEntity(EntityId.Create("target"), new(1, 0), new(8, 0));
        if (reverse) line = line with { Start = line.End, End = line.Start };
        var circle = new CircleEntity(EntityId.Create("new-circle"), new(0, 3), 3);
        Tangents(Infer(new[] { line }, circle, "target")).Should().BeEmpty();
        Tangents(Infer(new[] { line }, circle with { Center = new(1, 3) }, "target")).Should().ContainSingle();
    }

    [Theory]
    [InlineData(0, 90, true)]
    [InlineData(300, 60, true)]
    [InlineData(180, 270, false)]
    [InlineData(0, 0, false)]
    [InlineData(0, 360, true)]
    public void ArcContactMustBeWithinSweep(double start, double end, bool accepted)
    {
        var (target, circle, key) = Fixture("arc");
        var arc = (ArcEntity)target;
        Tangents(Infer(new DrawingEntity[] { arc with { StartAngleDegrees = start, EndAngleDegrees = end } }, circle, key))
            .Should().HaveCount(accepted ? 1 : 0);
    }

    [Theory]
    [InlineData(8, 3)]
    [InlineData(2, 3)]
    [InlineData(2, 7)]
    [InlineData(-8, 3)]
    [InlineData(-2, 7)]
    public void CircleSupportsExternalAndBothInternalContacts(double centerX, double radius)
    {
        var target = new CircleEntity(EntityId.Create("target"), new(0, 0), 5);
        var circle = new CircleEntity(EntityId.Create("new-circle"), new(centerX, 0), radius);
        Tangents(Infer(new[] { target }, circle, "target")).Should().ContainSingle();
    }

    [Fact]
    public void DoesNotChooseOtherTangentNeighborsOrDuplicateNamedTarget()
    {
        var (target, circle, key) = Fixture("line");
        var neighbor = new LineEntity(EntityId.Create("neighbor"), new(-4, 6), new(4, 6));
        foreach (var entities in new[] { new[] { target, neighbor, target }, new[] { neighbor, target, target } })
            Tangents(Infer(entities, circle, key)).Should().ContainSingle().Which.ReferenceKeys.Should().Equal("target", "new-circle");
    }

    [Theory]
    [InlineData("threepointcircle")]
    [InlineData("line")]
    [InlineData("centerpointarc")]
    public void DoesNotReuseCircleSnapIntentForOtherTools(string tool)
    {
        var (target, circle, key) = Fixture("line");
        Tangents(Infer(new[] { target }, circle, key, tool)).Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void RejectsInvalidCircleRadius(double radius)
    {
        var (target, circle, key) = Fixture("line");
        Tangents(Infer(new[] { target }, circle with { Radius = radius }, key)).Should().BeEmpty();
    }

    [Fact]
    public void CoincidentCirclesAreNotUniqueTangentContacts()
    {
        var target = new CircleEntity(EntityId.Create("target"), new(0, 0), 5);
        Tangents(Infer(new[] { target }, target with { Id = EntityId.Create("new-circle") }, "target")).Should().BeEmpty();
    }

    [Fact]
    public void RejectsInvalidAndUnsupportedPeersWithoutAllocatingTangentIntent()
    {
        var (_, circle, _) = Fixture("line");
        DrawingEntity[] targets =
        {
            new LineEntity(EntityId.Create("target"), new(0, 0), new(0, 0)),
            new LineEntity(EntityId.Create("target"), new(double.NaN, 0), new(4, 0)),
            new CircleEntity(EntityId.Create("target"), new(0, 0), double.PositiveInfinity),
            new ArcEntity(EntityId.Create("target"), new(0, 0), 5, double.NaN, 90),
            new EllipseEntity(EntityId.Create("target"), new(0, 0), new(5, 0), 1),
            new PointEntity(EntityId.Create("target"), new(0, 0)),
            new PolygonEntity(EntityId.Create("target"), new(0, 0), 5, 0, 6),
            new SplineEntity(EntityId.Create("target"), 1, new Point2[] { new(-4, 0), new(4, 0) }, new double[] { 0, 0, 1, 1 })
        };
        foreach (var target in targets)
            Tangents(Infer(new[] { target }, circle, "target")).Should().BeEmpty();
        Tangents(Infer(new[] { targets[0] }, circle with { Center = new(double.NaN, 3) }, "target")).Should().BeEmpty();
    }

    private static (DrawingEntity Target, CircleEntity Circle, string Key) Fixture(string kind) => kind switch
    {
        "circle" => (new CircleEntity(EntityId.Create("target"), new(0, 0), 5),
            new(EntityId.Create("new-circle"), new(8, 0), 3), "target"),
        "arc" => (new ArcEntity(EntityId.Create("target"), new(0, 0), 5, 0, 90),
            new(EntityId.Create("new-circle"), new(8, 0), 3), "target"),
        "polyline" => (new PolylineEntity(EntityId.Create("target"), new Point2[] { new(-4, 0), new(4, 0), new(4, -4) }),
            new(EntityId.Create("new-circle"), new(0, 3), 3), "target|segment|0"),
        _ => (new LineEntity(EntityId.Create("target"), new(-4, 0), new(4, 0)),
            new(EntityId.Create("new-circle"), new(0, 3), 3), "target")
    };

    private static IReadOnlyList<SketchConstraint> Infer(IReadOnlyList<DrawingEntity> existing, CircleEntity circle, string? key, string tool = "centercircle")
    {
        var id = 0;
        return SketchCreationConstraintFactory.CreateConstraintsForInsertion(tool, existing, new[] { circle }, kind => $"{kind}-{++id}", key);
    }
    private static SketchConstraint[] Tangents(IEnumerable<SketchConstraint> constraints) => constraints.Where(c => c.Kind == SketchConstraintKind.Tangent).ToArray();
}

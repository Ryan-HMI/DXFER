using DXFER.Blazor.Sketching;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class SketchRelationCommandFactoryTests
{
    private static LineEntity Line(string id, double x1, double y1, double x2, double y2) =>
        new(EntityId.Create(id), new(x1, y1), new(x2, y2));

    [Theory]
    [InlineData(null)]
    [InlineData("a")]
    [InlineData("b")]
    [InlineData("axis")]
    public void SymmetryKeepsThirdAxisDespiteStaleActivePickAndStoresReflectedEndpointPairing(string? active)
    {
        var document = new DrawingDocument([Line("a", -2, 0, -4, 3), Line("b", 4, 3, 2, 0), Line("axis", 0, -5, 0, 5)]);
        SketchCommandFactory.TryBuildConstraint(document, ["a", "b", "axis"], SketchConstraintKind.Symmetric,
            "sym", out var relation, out var status, active).Should().BeTrue(status);
        relation.ReferenceKeys.Should().Equal("a:start", "b:end", "a:end", "b:start", "axis");
        SketchConstraintService.ValidateConstraint(document, relation).State.Should().Be(SketchConstraintState.Satisfied);
    }

    [Theory]
    [InlineData("a|point|start|-2|0", "b|point|end|2|0")]
    [InlineData("a:start", "b:end")]
    public void SymmetryPointPairsAreCanonical(string first, string second)
    {
        var document = new DrawingDocument([Line("a", -2, 0, -4, 3), Line("b", 4, 3, 2, 0), Line("axis", 0, -5, 0, 5)]);
        SketchCommandFactory.TryBuildConstraint(document, [first, second, "axis|point|mid|0|0"],
            SketchConstraintKind.Symmetric, "sym", out var relation, out var status).Should().BeTrue(status);
        relation.ReferenceKeys.Should().Equal("a:start", "b:end", "axis");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SymmetryCurveBodiesNormalizePerimeterPicks(bool arcs)
    {
        DrawingEntity a = arcs ? new ArcEntity(EntityId.Create("a"), new(-3, 0), 1, 0, 90)
            : new CircleEntity(EntityId.Create("a"), new(-3, 0), 1);
        DrawingEntity b = arcs ? new ArcEntity(EntityId.Create("b"), new(3, 0), 1, 90, 180)
            : new CircleEntity(EntityId.Create("b"), new(3, 0), 1);
        var document = new DrawingDocument([a, b, Line("axis", 0, -5, 0, 5)]);
        SketchCommandFactory.TryBuildConstraint(document, ["a|point|quadrant-0|-2|0", "b", "axis"],
            SketchConstraintKind.Symmetric, "sym", out var relation, out var status).Should().BeTrue(status);
        relation.ReferenceKeys.Should().Equal("a", "b", "axis");
        SketchConstraintService.ValidateConstraint(document, relation).State.Should().Be(SketchConstraintState.Satisfied);
    }

    [Theory]
    [InlineData("line", "line:start")]
    [InlineData("line|point|start|999|999", "line:start")]
    [InlineData("line:end", "line:end")]
    public void NormalPreservesExplicitEndpointOrChoosesNearestCurveEndpoint(string pick, string expected)
    {
        var document = new DrawingDocument([Line("line", 3, 0, 8, 0), new CircleEntity(EntityId.Create("circle"), new(0, 0), 3)]);
        foreach (var keys in new[] { new[] { pick, "circle|point|quadrant-0|3|0" }, new[] { "circle", pick } })
        {
            SketchCommandFactory.TryBuildConstraint(document, keys, SketchConstraintKind.Normal,
                "normal", out var relation, out var status).Should().BeTrue(status);
            relation.ReferenceKeys.Should().Equal(expected, "circle");
        }
    }

    [Fact]
    public void NormalSegmentUsesCanonicalEndpointAndTieChoosesStart()
    {
        var poly = new PolylineEntity(EntityId.Create("poly"), [new(-3, 0), new(3, 0), new(8, 0)]);
        var document = new DrawingDocument([poly, new CircleEntity(EntityId.Create("circle"), new(0, 0), 3)]);
        SketchCommandFactory.TryBuildConstraint(document, ["poly|segment|0", "circle"], SketchConstraintKind.Normal,
            "normal", out var relation, out var status).Should().BeTrue(status);
        relation.ReferenceKeys.Should().Equal("poly|segment|0:start", "circle");
    }

    [Theory]
    [InlineData("poly|point|vertex-0|-3|0", "poly|segment|0:start")]
    [InlineData("poly|point|vertex-1|3|0", "poly|segment|0:end")]
    [InlineData("poly|point|vertex-2|8|0", "poly|segment|1:end")]
    public void NormalCanvasVertexKeepsStableSegmentEndpoint(string pick, string expected)
    {
        var document = new DrawingDocument([
            new PolylineEntity(EntityId.Create("poly"), [new(-3, 0), new(3, 0), new(8, 0)]),
            new CircleEntity(EntityId.Create("circle"), new(0, 0), 3)]);
        SketchCommandFactory.TryBuildConstraint(document, [pick, "circle"], SketchConstraintKind.Normal,
            "normal", out var relation, out var status).Should().BeTrue(status);
        relation.ReferenceKeys.Should().Equal(expected, "circle");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PolylineSymmetryStoresEveryUniqueVertexInTopologyOrder(bool closed)
    {
        Point2[] a = closed ? [new(-2, 0), new(-4, 0), new(-3, 3), new(-2, 0)] : [new(-2, 0), new(-4, 0), new(-3, 3)];
        Point2[] b = closed ? [new(3, 3), new(4, 0), new(2, 0), new(3, 3)] : [new(3, 3), new(4, 0), new(2, 0)];
        var document = new DrawingDocument([new PolylineEntity(EntityId.Create("a"), a), new PolylineEntity(EntityId.Create("b"), b), Line("axis", 0, -5, 0, 5)]);
        SketchCommandFactory.TryBuildConstraint(document, ["a", "b", "axis"], SketchConstraintKind.Symmetric,
            "sym", out var relation, out var status).Should().BeTrue(status);
        relation.ReferenceKeys.Should().Equal("a|segment|0:start", "b|segment|1:end", "a|segment|0:end", "b|segment|0:end",
            "a|segment|1:end", "b|segment|0:start", "axis");
        SketchConstraintService.ValidateConstraint(document, relation).State.Should().Be(SketchConstraintState.Satisfied);
    }

    [Fact]
    public void PolylineMismatchedClosureIsExplicitlyUnsupported()
    {
        var document = new DrawingDocument([
            new PolylineEntity(EntityId.Create("a"), [new(-2, 0), new(-4, 0), new(-2, 0)]),
            new PolylineEntity(EntityId.Create("b"), [new(2, 0), new(4, 0), new(2, 3)]), Line("axis", 0, -5, 0, 5)]);
        SketchCommandFactory.TryBuildConstraint(document, ["a", "b", "axis"], SketchConstraintKind.Symmetric,
            "sym", out _, out var status).Should().BeFalse();
        status.Should().Contain("matching topology");
    }

    [Fact]
    public void CircleCenterPointPairDoesNotBecomeWholeCurveSymmetry()
    {
        var document = new DrawingDocument([new CircleEntity(EntityId.Create("a"), new(-3, 0), 2),
            new CircleEntity(EntityId.Create("b"), new(3, 0), 5), Line("axis", 0, -5, 0, 5)]);
        SketchCommandFactory.TryBuildConstraint(document, ["a|point|center|-3|0", "b:center", "axis"], SketchConstraintKind.Symmetric,
            "sym", out var relation, out var status).Should().BeTrue(status);
        relation.ReferenceKeys.Should().Equal("a:center", "b:center", "axis");
        SketchConstraintService.ValidateConstraint(document, relation).State.Should().Be(SketchConstraintState.Satisfied);
    }

    [Theory]
    [InlineData("a", "b", "a")]
    [InlineData("a", "a", "axis")]
    [InlineData("a", "circle", "axis")]
    [InlineData("a", "b", "zero")]
    [InlineData("a", "missing", "axis")]
    public void SymmetryRefusesInvalidSelectionWithoutFilteringReferences(string a, string b, string axis)
    {
        var document = new DrawingDocument([Line("a", -2, 0, -4, 3), Line("b", 4, 3, 2, 0),
            Line("axis", 0, -5, 0, 5), Line("zero", 0, 0, 0, 0), new CircleEntity(EntityId.Create("circle"), new(0, 0), 3)]);
        SketchCommandFactory.TryBuildConstraint(document, [a, b, axis], SketchConstraintKind.Symmetric,
            "sym", out _, out _).Should().BeFalse();
    }
}

using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class SketchFailureExplanationTests
{
    [Theory]
    [InlineData(11, "redundant")]
    [InlineData(12, "conflict")]
    [InlineData(11.0005, "redundant")]
    [InlineData(11.002, "conflict")]
    public void EqualRectangleExplainsCompatibleAndConflictingOppositeDimensions(double value, string explanation)
    {
        var original = Rectangle();
        var incoming = new SketchDimension("opposing", SketchDimensionKind.LinearDistance,
            new[] { "box|segment|2:start", "box|segment|2:end" }, value, new(5.5, 14), true);
        var result = new LegacySketchSolverAdapter().Solve(new SketchSolveRequest(original, Array.Empty<SketchConstraint>(), new[] { incoming }));
        result.Status.Should().NotBe(SketchSolveStatus.Solved);
        result.Document.Entities.Should().Equal(original.Entities);
        result.Document.Dimensions.Should().Contain(d => d.Id == incoming.Id && d.Value == value);
        result.Document.Constraints.Should().OnlyContain(c => c.State == SketchConstraintState.Satisfied);
        result.AffectedDiagnostics.Should().Contain(d => d.Message.ToLowerInvariant().Contains(explanation));
    }

    [Fact]
    public void UnprovenMeasurementMismatchDoesNotClaimMathematicalConflict()
    {
        var original = new DrawingDocument(new DrawingEntity[] { new PointEntity(EntityId.Create("a"), new(0, 0)), new PointEntity(EntityId.Create("b"), new(3, 4)) },
            new[] { new SketchDimension("length", SketchDimensionKind.LinearDistance, new[] { "a", "b" }, 8, isDriving: true) }, Array.Empty<SketchConstraint>());
        var diagnostics = SketchSolveDiagnostics.FromDocument(original);
        diagnostics.Should().ContainSingle().Which.Reason.Should().Be(SketchSolveDiagnosticReason.Unresolved);
        SketchSolveDiagnostics.DescribeFailure(diagnostics).Should().Contain("supported solver").And.NotContain("conflicts").And.NotContain("Redundant");
    }

    public static DrawingDocument Rectangle()
    {
        var poly = new PolylineEntity(EntityId.Create("box"), new[] { new Point2(0, 0), new Point2(11, 0), new Point2(11, 12.5), new Point2(0, 12.5), new Point2(0, 0) });
        var constraints = new[] {
            C("h0", SketchConstraintKind.Horizontal, "box|segment|0"),
            C("v1", SketchConstraintKind.Vertical, "box|segment|1"),
            C("h2", SketchConstraintKind.Horizontal, "box|segment|2"),
            C("v3", SketchConstraintKind.Vertical, "box|segment|3"),
            C("closed", SketchConstraintKind.Coincident, "box|segment|0:start", "box|segment|3:end"),
            C("equal", SketchConstraintKind.Equal, "box|segment|0", "box|segment|2"),
            C("parallel", SketchConstraintKind.Parallel, "box|segment|0", "box|segment|2"),
            C("perpendicular", SketchConstraintKind.Perpendicular, "box|segment|0", "box|segment|1") };
        return new DrawingDocument(new DrawingEntity[] { poly }, new[] {
            new SketchDimension("original", SketchDimensionKind.LinearDistance,
                new[] { "box|segment|0:start", "box|segment|0:end" }, 11, new(5.5, -2), true) }, constraints);
    }
    private static SketchConstraint C(string id, SketchConstraintKind kind, params string[] keys) => new(id, kind, keys, SketchConstraintState.Satisfied);
}

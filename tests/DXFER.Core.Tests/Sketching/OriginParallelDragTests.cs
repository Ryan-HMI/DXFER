using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class OriginParallelDragTests
{
    [Theory]
    [InlineData(false, 0.00001)]
    [InlineData(true, 0.00001)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 10000)]
    [InlineData(true, 10000)]
    public void AnchoredRotationPreservesPeerLengthAndRespectsFixedPeer(bool perpendicular, double scale)
    {
        var a = new LineEntity(EntityId.Create("a"), new(-5 * scale, 10 * scale), new(0, 0));
        var b = new LineEntity(EntityId.Create("b"), new(-14 * scale, 9 * scale),
            perpendicular ? new(-4 * scale, 14 * scale) : new(-9 * scale, -1 * scale));
        var constraints = new[] {
            new SketchConstraint("anchor", SketchConstraintKind.Coincident, new[] { "a:end", "$origin:center" }, SketchConstraintState.Satisfied),
            new SketchConstraint("relation", perpendicular ? SketchConstraintKind.Perpendicular : SketchConstraintKind.Parallel,
                new[] { "a", "b" }, SketchConstraintState.Satisfied) };
        var free = new DrawingDocument(new DrawingEntity[] { a, b }, Array.Empty<SketchDimension>(), constraints);
        var locked = new DrawingDocument(free.Entities, free.Dimensions, constraints.Append(new SketchConstraint("fixed", SketchConstraintKind.Fix,
            new[] { "b" }, SketchConstraintState.Satisfied)).ToArray());
        var target = new Point2(-3 * scale, 11 * scale);
        SketchGeometryDragService.TryApplyDrag(locked, $"a|point|start|{a.Start.X}|{a.Start.Y}", a.Start, target, false,
            out var refused, out _).Should().BeFalse();
        refused.Entities.Should().Equal(locked.Entities);
        SketchGeometryDragService.TryApplyDrag(free, $"a|point|start|{a.Start.X}|{a.Start.Y}", a.Start, target, false,
            out var changed, out var status).Should().BeTrue(status);
        var peer = (LineEntity)changed.Entities[1];
        double Length(LineEntity line) => Math.Sqrt(Math.Pow(line.End.X - line.Start.X, 2) + Math.Pow(line.End.Y - line.Start.Y, 2));
        Length(peer).Should().BeApproximately(Length(b), scale * 1e-8);
        ((LineEntity)changed.Entities[0]).End.Should().Be(new Point2(0, 0));
        changed.Constraints.Should().OnlyContain(c => c.State == SketchConstraintState.Satisfied);
    }

    [Theory]
    [InlineData("a", "start", false)]
    [InlineData("a", "end", false)]
    [InlineData("b", "start", false)]
    [InlineData("b", "end", false)]
    [InlineData("a", "start", true)]
    [InlineData("a", "end", true)]
    [InlineData("b", "start", true)]
    [InlineData("b", "end", true)]
    public void FreeEndpointsMoveWhileOriginStaysAnchored(string id, string endpoint, bool reverse)
    {
        var a = new LineEntity(EntityId.Create("a"), new(-5, 10), new(0, 0));
        var b = new LineEntity(EntityId.Create("b"), new(-14, 9), new(-9, -1));
        var constraints = new[] {
            new SketchConstraint("anchor", SketchConstraintKind.Coincident, new[] { "a:end", "$origin:center" }, SketchConstraintState.Satisfied),
            new SketchConstraint("parallel", SketchConstraintKind.Parallel, reverse ? new[] { "b", "a" } : new[] { "a", "b" }, SketchConstraintState.Satisfied) };
        var document = new DrawingDocument(new DrawingEntity[] { a, b }, Array.Empty<SketchDimension>(), constraints);
        var line = id == "a" ? a : b;
        var start = endpoint == "start" ? line.Start : line.End;
        var end = new Point2(start.X + 2, start.Y + 1);
        var accepted = SketchGeometryDragService.TryApplyDrag(document, $"{id}|point|{endpoint}|{start.X}|{start.Y}", start, end, false,
            out var changed, out var status);
        if (id == "a" && endpoint == "end")
        {
            accepted.Should().BeFalse();
            changed.Entities.Should().Equal(document.Entities);
            return;
        }
        accepted.Should().BeTrue(status);
        ((LineEntity)changed.Entities[0]).End.Should().Be(new Point2(0, 0));
        var moved = (LineEntity)changed.Entities.Single(e => e.Id.Value == id);
        (endpoint == "start" ? moved.Start : moved.End).Should().Be(end);
        changed.Constraints.Should().OnlyContain(c => c.State == SketchConstraintState.Satisfied);
    }
}

using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class JoinedRectangleDragTests
{
    [Theory]
    [InlineData(false, "a1")]
    [InlineData(true, "a1")]
    [InlineData(false, "a1|point|mid|6|4.5")]
    [InlineData(true, "a1|point|mid|6|4.5")]
    public void CornerWeldDoesNotDisableIndependentEdgeResize(bool reverse, string key)
    {
        var document = Create(reverse);
        SketchGeometryDragService.TryApplyDrag(document, key, new(6, 4.5), new(8, 5.5), false,
            out var changed, out var status).Should().BeTrue(status);
        var edge = (LineEntity)changed.Entities[1];
        edge.Start.Should().Be(new Point2(8, 0));
        edge.End.Should().Be(new Point2(8, 9));
        ((LineEntity)changed.Entities[0]).Start.Should().Be(new Point2(0, 0));
        changed.Entities.Skip(4).Should().Equal(document.Entities.Skip(4));
        changed.Constraints.Should().OnlyContain(c => c.State == SketchConstraintState.Satisfied);
    }

    [Fact]
    public void FixedEdgeStillRefusesUnchanged()
    {
        var original = Create(false);
        var document = new DrawingDocument(original.Entities, original.Dimensions,
            original.Constraints.Append(Constraint(SketchConstraintKind.Fix, "a1")).ToArray());
        SketchGeometryDragService.TryApplyDrag(document, "a1", new(6, 4.5), new(8, 5.5), false,
            out var changed, out _).Should().BeFalse();
        changed.Entities.Should().Equal(document.Entities);
    }

    private static SketchConstraint Constraint(SketchConstraintKind kind, params string[] keys) =>
        new(kind + string.Join(",", keys), kind, keys, SketchConstraintState.Satisfied);

    private static DrawingDocument Create(bool reverse)
    {
        var entities = new List<DrawingEntity>();
        var constraints = new List<SketchConstraint>();
        void Rectangle(string prefix, Point2[] points)
        {
            for (var i = 0; i < 4; i++)
            {
                entities.Add(new LineEntity(EntityId.Create(prefix + i), points[i], points[(i + 1) % 4]));
                constraints.Add(Constraint(SketchConstraintKind.Coincident, $"{prefix}{i}:end", $"{prefix}{(i + 1) % 4}:start"));
            }
            constraints.Add(Constraint(SketchConstraintKind.Parallel, prefix + 0, prefix + 2));
            constraints.Add(Constraint(SketchConstraintKind.Parallel, prefix + 1, prefix + 3));
            constraints.Add(Constraint(SketchConstraintKind.Perpendicular, prefix + 0, prefix + 1));
        }
        Rectangle("a", [new(0, 0), new(6, 0), new(6, 9), new(0, 9)]);
        Rectangle("b", [new(0, 0), new(12, -2), new(11.5, -5), new(-0.5, -3)]);
        constraints.Add(Constraint(SketchConstraintKind.Horizontal, "a0"));
        constraints.Add(Constraint(SketchConstraintKind.Vertical, "a1"));
        constraints.Add(Constraint(SketchConstraintKind.Coincident, "a0:start", "$origin:center"));
        constraints.Add(Constraint(SketchConstraintKind.Coincident, "b0:start", "a0:start"));
        if (reverse) constraints.Reverse();
        return new DrawingDocument(entities, Array.Empty<SketchDimension>(), constraints);
    }
}

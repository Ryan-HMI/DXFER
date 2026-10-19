using DXFER.CadIO;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class JunctionEndpointDragTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void RealEndpointDragMaintainsIncidenceAndHonorsFix(bool reverse, bool fixedEndpoint)
    {
        DrawingEntity[] entities = [new LineEntity(EntityId.Create("carrier"), new(2, 4), new(5, -2)),
            new LineEntity(EntityId.Create("branch"), new(0, 1), new(3, 2))];
        if (reverse) Array.Reverse(entities);
        var constraints = new List<SketchConstraint> { new("incidence", SketchConstraintKind.PointOnLine,
            ["carrier", "branch:end"], SketchConstraintState.Satisfied) };
        if (fixedEndpoint) constraints.Add(new("fixed", SketchConstraintKind.Fix, ["branch:end"], SketchConstraintState.Satisfied));
        var original = new DrawingDocument(entities, [], constraints);
        var accepted = SketchGeometryDragService.TryApplyDrag(original, "branch|point|end|3|2", new(3, 2), new(4, 3), false,
            out var next, out var status);
        accepted.Should().Be(!fixedEndpoint, status);
        if (fixedEndpoint) { next.Should().BeSameAs(original); return; }
        next.Entities.OfType<LineEntity>().Single(e => e.Id.Value == "branch").End.Should().Be(new Point2(4, 3));
        next.Constraints.Should().OnlyContain(c => c.State == SketchConstraintState.Satisfied);
        var reopened = DxfDocumentReader.Read(DxfDocumentWriter.Write(next, new(IncludeNativeDocument: true)));
        reopened.Entities.Should().BeEquivalentTo(next.Entities);
        reopened.Constraints.Should().BeEquivalentTo(next.Constraints);
        SketchGeometryDragService.TryApplyDrag(reopened, "branch|point|end|4|3", new(4, 3), new(5, 4), false,
            out var again, out status).Should().BeTrue(status);
        again.Constraints.Should().OnlyContain(c => c.State == SketchConstraintState.Satisfied);
    }
}

using DXFER.Blazor.Interop;
using DXFER.Blazor.Sketching;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class CreationSnapIntentTests
{
    [Theory]
    [InlineData("PointOnLine", "source", 3, 0)]
    [InlineData("Horizontal", "source:start", 3, 0)]
    [InlineData("Vertical", "source:start", 0, 3)]
    public void ExplicitSnapCreatesValidatedRelationWithoutMovingExistingGeometry(string kind, string key, double x, double y)
    {
        var source = new LineEntity(EntityId.Create("source"), new(0, 0), new(10, 0));
        var created = new PointEntity(EntityId.Create("created"), new(x, y));
        var snaps = new[] { new CanvasCreationSnapDto(new(x, y), kind, new[] { key }) };
        var constraints = SketchCreationConstraintFactory.CreateConstraintsForInsertion("point", new[] { source }, new[] { created },
            k => k.ToString(), creationSnaps: snaps);
        constraints.Should().Contain(c => c.Kind.ToString() == kind && c.ReferenceKeys.Contains("created") && c.ReferenceKeys.Contains(key));
        var document = new DrawingDocument(new DrawingEntity[] { source, created });
        constraints.Select(c => SketchConstraintService.ValidateConstraint(document, c).State).Should().OnlyContain(s => s == SketchConstraintState.Satisfied);
    }

    [Fact]
    public void CrossingSnapPersistsBothSupportingLines()
    {
        var sources = new DrawingEntity[] { new LineEntity(EntityId.Create("a"), new(0, 0), new(10, 0)),
            new LineEntity(EntityId.Create("b"), new(3, -5), new(3, 5)) };
        var created = new PointEntity(EntityId.Create("created"), new(3, 0));
        var snaps = new[] { new CanvasCreationSnapDto(created.Location, "PointOnLine", new[] { "a" }),
            new CanvasCreationSnapDto(created.Location, "PointOnLine", new[] { "b" }) };
        var id = 0;
        var constraints = SketchCreationConstraintFactory.CreateConstraintsForInsertion("point", sources, new[] { created },
            _ => $"c{++id}", creationSnaps: snaps);
        constraints.Count(c => c.Kind == SketchConstraintKind.PointOnLine).Should().Be(2);
    }

    [Theory]
    [InlineData("missing", 0)]
    [InlineData("source", 3)]
    public void InvalidSnapCannotSilentlyMoveTheSource(string key, double y)
    {
        var source = new LineEntity(EntityId.Create("source"), new(0, 0), new(10, 0));
        var created = new PointEntity(EntityId.Create("created"), new(3, y));
        var act = () => SketchCreationConstraintFactory.CreateConstraintsForInsertion("point", new[] { source }, new[] { created },
            k => k.ToString(), creationSnaps: new[] { new CanvasCreationSnapDto(created.Location, "PointOnLine", new[] { key }) });
        act.Should().Throw<InvalidOperationException>().WithMessage("*snap*");
    }
}

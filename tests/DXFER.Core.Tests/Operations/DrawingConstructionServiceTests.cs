using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Operations;
using FluentAssertions;

namespace DXFER.Core.Tests.Operations;

public sealed class DrawingConstructionServiceTests
{
    [Fact]
    public void ToggleSelectedEntitiesMakesMixedSelectionConstruction()
    {
        var lineAId = EntityId.Create("line-a");
        var lineBId = EntityId.Create("line-b");
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(lineAId, new Point2(0, 0), new Point2(1, 0), IsConstruction: true),
            new LineEntity(lineBId, new Point2(0, 1), new Point2(1, 1)),
            new LineEntity(EntityId.Create("line-c"), new Point2(0, 2), new Point2(1, 2))
        });

        var result = DrawingConstructionService.ToggleSelected(document, new[] { lineAId.Value, lineBId.Value });

        result.ChangedCount.Should().Be(2);
        result.Document.Entities[0].IsConstruction.Should().BeTrue();
        result.Document.Entities[1].IsConstruction.Should().BeTrue();
        result.Document.Entities[2].IsConstruction.Should().BeFalse();
    }

    [Fact]
    public void ToggleSelectedEntitiesMakesAllConstructionSelectionNormal()
    {
        var lineAId = EntityId.Create("line-a");
        var lineBId = EntityId.Create("line-b");
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(lineAId, new Point2(0, 0), new Point2(1, 0), IsConstruction: true),
            new LineEntity(lineBId, new Point2(0, 1), new Point2(1, 1), IsConstruction: true)
        });

        var result = DrawingConstructionService.ToggleSelected(document, new[] { lineAId.Value, lineBId.Value });

        result.ChangedCount.Should().Be(2);
        result.Document.Entities.Should().OnlyContain(entity => !entity.IsConstruction);
    }
}

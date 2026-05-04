using DXFER.Blazor.Interop;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using FluentAssertions;

namespace DXFER.Core.Tests.Interop;

public sealed class CanvasDocumentDtoTests
{
    [Fact]
    public void ExposesConstructionStateForRendering()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(
                EntityId.Create("construction-line"),
                new Point2(0, 0),
                new Point2(1, 0),
                IsConstruction: true)
        });

        var dto = CanvasDocumentDto.FromDocument(document);

        dto.Entities[0].IsConstruction.Should().BeTrue();
    }
}

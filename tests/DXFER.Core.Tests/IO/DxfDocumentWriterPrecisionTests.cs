using DXFER.CadIO;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Operations;
using FluentAssertions;

namespace DXFER.Core.Tests.IO;

public sealed class DxfDocumentWriterPrecisionTests
{
    [Fact]
    public void ExportDoesNotTurnDistinctCirclesIntoExactDuplicates()
    {
        var center = new Point2(4.123456789012345, -7.123456789012345);
        var source = new DrawingDocument(new DrawingEntity[]
        {
            new CircleEntity(EntityId.Create("one"), center, 0.125),
            new CircleEntity(EntityId.Create("two"), center, Math.BitIncrement(0.125))
        });
        var before = CutPathLengthService.Calculate(source);
        var reopened = DxfDocumentReader.Read(DxfDocumentWriter.Write(source));
        reopened.Entities.OfType<CircleEntity>().Select(c => (c.Center, c.Radius))
            .Should().Equal(source.Entities.OfType<CircleEntity>().Select(c => (c.Center, c.Radius)));
        ExactDuplicateGeometryService.Remove(reopened).RemovedCount.Should().Be(0);
        ProductionDxfPreparation.Prepare(DxfDocumentWriter.Write(source)).Document.Entities.Should().HaveCount(2);
        CutPathLengthService.Calculate(reopened).Should().Be(before);
    }

    [Fact]
    public void ExportPreservesSmallAndLargeFiniteCoordinates()
    {
        var start = new Point2(0.000000000000012345, -123456789.01234567);
        var end = new Point2(8.123456789012345, 2.123456789012345);
        var source = new DrawingDocument(new[] { new LineEntity(EntityId.Create("line"), start, end) });
        var line = DxfDocumentReader.Read(DxfDocumentWriter.Write(source)).Entities.OfType<LineEntity>().Single();
        line.Start.Should().Be(start);
        line.End.Should().Be(end);
    }
}

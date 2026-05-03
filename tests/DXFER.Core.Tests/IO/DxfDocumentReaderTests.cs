using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.IO;
using FluentAssertions;

namespace DXFER.Core.Tests.IO;

public sealed class DxfDocumentReaderTests
{
    [Fact]
    public void ReadsCommonAsciiEntitiesUsedByFlatPatterns()
    {
        const string dxf = """
0
SECTION
2
ENTITIES
0
LINE
8
CUT
10
1
20
2
11
11
21
2
0
CIRCLE
8
ETCH
10
5
20
6
40
2.5
0
ARC
8
BEND
10
20
20
25
40
4
50
0
51
90
0
LWPOLYLINE
8
OUTLINE
90
3
70
1
10
0
20
0
10
3
20
0
10
3
20
4
0
ENDSEC
0
EOF
""";

        var document = DxfDocumentReader.Read(dxf);

        document.Entities.Should().HaveCount(4);
        document.Entities[0].Should().BeOfType<LineEntity>()
            .Which.Start.Should().Be(new Point2(1, 2));
        document.Entities[1].Should().BeOfType<CircleEntity>()
            .Which.Radius.Should().Be(2.5);
        document.Entities[2].Should().BeOfType<ArcEntity>()
            .Which.EndAngleDegrees.Should().Be(90);
        document.Entities[3].Should().BeOfType<PolylineEntity>()
            .Which.Vertices.Should().ContainInOrder(new Point2(0, 0), new Point2(3, 0), new Point2(3, 4), new Point2(0, 0));
    }

    [Fact]
    public void WritesReadableAsciiDxfForSupportedEntities()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("line-a"), new Point2(0, 0), new Point2(10, 0)),
            new CircleEntity(EntityId.Create("circle-a"), new Point2(5, 5), 1.25),
            new ArcEntity(EntityId.Create("arc-a"), new Point2(8, 8), 2, 15, 120),
            new PolylineEntity(EntityId.Create("poly-a"), new[] { new Point2(0, 0), new Point2(2, 0), new Point2(2, 1) })
        });

        var roundTripped = DxfDocumentReader.Read(DxfDocumentWriter.Write(document));

        roundTripped.Entities.Should().HaveCount(4);
        roundTripped.GetBounds().MinX.Should().BeApproximately(0, 0.0001);
        roundTripped.GetBounds().MaxX.Should().BeGreaterThan(9.9);
    }
}

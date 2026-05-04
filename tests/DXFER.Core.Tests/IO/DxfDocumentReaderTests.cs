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
SPLINE
8
SHEETMETAL_CUT_LINES
70
8
71
3
72
8
73
4
74
0
40
0
40
0
40
0
40
0
40
1
40
1
40
1
40
1
10
0
20
0
10
2
20
0
10
4
20
2
10
6
20
2
0
ENDSEC
0
EOF
""";

        var document = DxfDocumentReader.Read(dxf);

        document.Entities.Should().HaveCount(5);
        document.Entities[0].Should().BeOfType<LineEntity>()
            .Which.Start.Should().Be(new Point2(1, 2));
        document.Entities[1].Should().BeOfType<CircleEntity>()
            .Which.Radius.Should().Be(2.5);
        document.Entities[2].Should().BeOfType<ArcEntity>()
            .Which.EndAngleDegrees.Should().Be(90);
        document.Entities[3].Should().BeOfType<PolylineEntity>()
            .Which.Vertices.Should().ContainInOrder(new Point2(0, 0), new Point2(3, 0), new Point2(3, 4), new Point2(0, 0));
        var spline = document.Entities[4].Should().BeOfType<SplineEntity>().Subject;
        spline.Degree.Should().Be(3);
        spline.Knots.Should().ContainInOrder(0, 0, 0, 0, 1, 1, 1, 1);
        spline.ControlPoints.Should().ContainInOrder(
            new Point2(0, 0),
            new Point2(2, 0),
            new Point2(4, 2),
            new Point2(6, 2));
        spline.GetSamplePoints().Should().HaveCountGreaterThan(4);
    }

    [Fact]
    public void WritesReadableAsciiDxfForSupportedEntities()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("line-a"), new Point2(0, 0), new Point2(10, 0)),
            new CircleEntity(EntityId.Create("circle-a"), new Point2(5, 5), 1.25),
            new ArcEntity(EntityId.Create("arc-a"), new Point2(8, 8), 2, 15, 120),
            new PolylineEntity(EntityId.Create("poly-a"), new[] { new Point2(0, 0), new Point2(2, 0), new Point2(2, 1) }),
            new SplineEntity(
                EntityId.Create("spline-a"),
                3,
                new[] { new Point2(0, 0), new Point2(1, 2), new Point2(3, 2), new Point2(4, 0) },
                new[] { 0d, 0d, 0d, 0d, 1d, 1d, 1d, 1d })
        });

        var dxf = DxfDocumentWriter.Write(document);
        var roundTripped = DxfDocumentReader.Read(dxf);

        dxf.Should().Contain("SPLINE");
        roundTripped.Entities.Should().HaveCount(5);
        roundTripped.Entities[4].Should().BeOfType<SplineEntity>()
            .Which.ControlPoints.Should().ContainInOrder(
                new Point2(0, 0),
                new Point2(1, 2),
                new Point2(3, 2),
                new Point2(4, 0));
        roundTripped.GetBounds().MinX.Should().BeApproximately(0, 0.0001);
        roundTripped.GetBounds().MaxX.Should().BeGreaterThan(9.9);
    }
}

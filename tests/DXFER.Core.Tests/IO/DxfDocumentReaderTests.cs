using DXFER.CadIO;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
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
POINT
8
SKETCH
10
7
20
9
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
ELLIPSE
8
CUT
10
10
20
10
11
4
21
0
40
0.5
41
0
42
1.57079632679
0
ENDSEC
0
EOF
""";

        var document = DxfDocumentReader.Read(dxf);

        document.Entities.Should().HaveCount(7);
        document.Entities[0].Should().BeOfType<LineEntity>()
            .Which.Start.Should().Be(new Point2(1, 2));
        document.Entities[1].Should().BeOfType<CircleEntity>()
            .Which.Radius.Should().Be(2.5);
        document.Entities[2].Should().BeOfType<ArcEntity>()
            .Which.EndAngleDegrees.Should().Be(90);
        document.Entities[3].Should().BeOfType<PointEntity>()
            .Which.Location.Should().Be(new Point2(7, 9));
        document.Entities[4].Should().BeOfType<PolylineEntity>()
            .Which.Vertices.Should().ContainInOrder(new Point2(0, 0), new Point2(3, 0), new Point2(3, 4), new Point2(0, 0));
        var spline = document.Entities[5].Should().BeOfType<SplineEntity>().Subject;
        spline.Degree.Should().Be(3);
        spline.Knots.Should().ContainInOrder(0, 0, 0, 0, 1, 1, 1, 1);
        spline.ControlPoints.Should().ContainInOrder(
            new Point2(0, 0),
            new Point2(2, 0),
            new Point2(4, 2),
            new Point2(6, 2));
        spline.GetSamplePoints().Should().HaveCountGreaterThan(4);
        var ellipse = document.Entities[6].Should().BeOfType<EllipseEntity>().Subject;
        ellipse.Center.Should().Be(new Point2(10, 10));
        ellipse.MajorAxisEndPoint.Should().Be(new Point2(4, 0));
        ellipse.MinorRadiusRatio.Should().Be(0.5);
        ellipse.EndParameterDegrees.Should().BeApproximately(90, 0.000001);
    }

    [Fact]
    public void RecordsUnsupportedEntityCountsAndImportWarnings()
    {
        const string dxf = """
0
SECTION
2
ENTITIES
0
LINE
10
0
20
0
11
1
21
0
0
3DSOLID
10
0
20
0
0
HATCH
10
0
20
0
0
3DSOLID
10
2
20
2
0
ENDSEC
0
EOF
""";

        var document = DxfDocumentReader.Read(dxf);

        document.Entities.Should().ContainSingle()
            .Which.Should().BeOfType<LineEntity>();
        document.Metadata.UnsupportedEntityCounts.Should().ContainKey("3DSOLID")
            .WhoseValue.Should().Be(2);
        document.Metadata.UnsupportedEntityCounts.Should().ContainKey("HATCH")
            .WhoseValue.Should().Be(1);
        document.Metadata.Warnings.Should().Contain(warning =>
            warning.Code == "unsupported-entity"
            && warning.Severity == DrawingDocumentWarningSeverity.Warning
            && warning.Message.Contains("3DSOLID (2)", StringComparison.Ordinal)
            && warning.Message.Contains("HATCH (1)", StringComparison.Ordinal));
        document.Metadata.Warnings.Should().Contain(warning =>
            warning.Code == "missing-units"
            && warning.Severity == DrawingDocumentWarningSeverity.Warning);
    }

    [Fact]
    public void WritesReadableAsciiDxfForSupportedEntities()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("line-a"), new Point2(0, 0), new Point2(10, 0)),
            new CircleEntity(EntityId.Create("circle-a"), new Point2(5, 5), 1.25),
            new ArcEntity(EntityId.Create("arc-a"), new Point2(8, 8), 2, 15, 120),
            new PointEntity(EntityId.Create("point-a"), new Point2(3, 4)),
            new EllipseEntity(EntityId.Create("ellipse-a"), new Point2(1, 2), new Point2(4, 0), 0.5),
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
        dxf.Should().Contain("ELLIPSE");
        dxf.Should().Contain("POINT");
        roundTripped.Entities.Should().HaveCount(7);
        roundTripped.Entities[3].Should().BeOfType<PointEntity>()
            .Which.Location.Should().Be(new Point2(3, 4));
        roundTripped.Entities[4].Should().BeOfType<EllipseEntity>()
            .Which.MinorRadiusRatio.Should().Be(0.5);
        roundTripped.Entities[6].Should().BeOfType<SplineEntity>()
            .Which.ControlPoints.Should().ContainInOrder(
                new Point2(0, 0),
                new Point2(1, 2),
                new Point2(3, 2),
                new Point2(4, 0));
        roundTripped.GetBounds().MinX.Should().BeApproximately(-3, 0.0001);
        roundTripped.GetBounds().MaxX.Should().BeGreaterThan(9.9);
    }

    [Fact]
    public void WritesAndReadsFitPointSplines()
    {
        var fitPoints = new[]
        {
            new Point2(0, 0),
            new Point2(1, 2),
            new Point2(3, 1),
            new Point2(5, 4),
            new Point2(7, 0)
        };
        var document = new DrawingDocument(new DrawingEntity[]
        {
            SplineEntity.FromFitPoints(EntityId.Create("fit-spline"), fitPoints)
        });

        var dxf = DxfDocumentWriter.Write(document);
        var roundTripped = DxfDocumentReader.Read(dxf);
        var normalizedDxf = dxf.Replace("\r\n", "\n");

        normalizedDxf.Should().Contain("""
74
5
""");
        normalizedDxf.Should().Contain("""
11
0
21
0
""");
        var spline = roundTripped.Entities.Should().ContainSingle().Subject.Should().BeOfType<SplineEntity>().Subject;
        spline.FitPoints.Should().Equal(fitPoints);
        foreach (var point in fitPoints)
        {
            spline.GetSamplePoints().Should().Contain(point);
        }
    }

    [Fact]
    public void SkipsConstructionGeometryWhenWritingCutDxf()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("cut-line"), new Point2(0, 0), new Point2(10, 0)),
            new LineEntity(
                EntityId.Create("construction-line"),
                new Point2(0, 5),
                new Point2(10, 5),
                IsConstruction: true)
        });

        var dxf = DxfDocumentWriter.Write(document);

        dxf.Should().Contain("cut-line");
        dxf.Should().NotContain("construction-line");
    }
}

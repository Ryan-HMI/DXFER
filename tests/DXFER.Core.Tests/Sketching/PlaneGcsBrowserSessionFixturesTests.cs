using DXFER.CadIO;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.IO;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class PlaneGcsBrowserSessionFixturesTests
{
    [Fact]
    public void WriteUnsolvedPolygonSessionBaselines()
    {
        Write("session-witness", new DrawingDocument([
            new LineEntity(Id("axis"), new(-7, -10), new(-7, 10)),
            new PolygonEntity(Id("a"), new(-14.13372651860118, 3.7773980130441487),
                1.3941710200160742, 266.2151126470417, 6),
            new PolygonEntity(Id("b"), new(1.0080730505287647, -1.6462810314260423),
                1.713422647677362, 72.93348181061447, 6)],
            [new("radius", SketchDimensionKind.Radius, ["a"], 1.3941710200160742, isDriving: true)],
            [new("axis-vertical", SketchConstraintKind.Vertical, ["axis"]),
             new("fixed", SketchConstraintKind.Fix, ["axis"])]));
        foreach (var circumscribed in new[] { false, true })
        {
            var document = new DrawingDocument([
                new LineEntity(Id("axis"), new(0, -10), new(0, 10)),
                new PolygonEntity(Id("a"), new(-5, 1), 3, 20, 5, circumscribed),
                new PolygonEntity(Id("b"), new(6, 2), 2, 130, 5, circumscribed)],
                [new("radius", SketchDimensionKind.Radius, ["a"], 3, isDriving: true)],
                [new("axis-vertical", SketchConstraintKind.Vertical, ["axis"]),
                 new("fixed", SketchConstraintKind.Fix, ["axis"])]);
            Write($"polygon-{circumscribed}", document);
            var vertices = document.Entities.OfType<PolygonEntity>().Select(p => p.GetVertices()).ToArray();
            vertices.Should().OnlyContain(v => v.Count == 5);
        }

        Point2[] points = [new(0, 0), new(15, 0), new(15, 8), new(0, 8)];
        var edges = points.Select((p, i) => new LineEntity(Id($"edge{i}"), p, points[(i + 1) % 4])).ToArray();
        var constraints = Enumerable.Range(0, 4).Select(i => new SketchConstraint($"weld{i}",
            SketchConstraintKind.Coincident, [$"edge{i}:end", $"edge{(i + 1) % 4}:start"]))
            .Concat(new SketchConstraint[] {
                new("parallel0", SketchConstraintKind.Parallel, ["edge0", "edge2"]),
                new("parallel1", SketchConstraintKind.Parallel, ["edge1", "edge3"]),
                new("perpendicular", SketchConstraintKind.Perpendicular, ["edge0", "edge1"]),
                new("horizontal", SketchConstraintKind.Horizontal, ["edge0"]),
                new("vertical", SketchConstraintKind.Vertical, ["edge1"])
            }).ToArray();
        Write("rectangle", new(edges, [new("width", SketchDimensionKind.LinearDistance,
            ["edge0:start", "edge0:end"], 15, isDriving: true)], constraints));
    }

    private static EntityId Id(string id) => EntityId.Create(id);
    private static void Write(string name, DrawingDocument document)
    {
        var text = DxfDocumentWriter.Write(document, new DxfWriteOptions(IncludeNativeDocument: true));
        var restored = DxfDocumentReader.Read(text);
        restored.Entities.Should().BeEquivalentTo(document.Entities);
        restored.Dimensions.Should().BeEquivalentTo(document.Dimensions);
        restored.Constraints.Should().BeEquivalentTo(document.Constraints);
        var output = Environment.GetEnvironmentVariable("DXFER_SESSION_FIXTURES");
        if (string.IsNullOrWhiteSpace(output)) return;
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, name + ".dxf"), text);
    }
}

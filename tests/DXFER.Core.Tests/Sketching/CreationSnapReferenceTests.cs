using DXFER.Blazor.Interop;
using DXFER.Blazor.Sketching;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.IO;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class CreationSnapReferenceTests
{
    public static IEnumerable<object[]> CreationCases()
    {
        foreach (var tool in new[] { "inscribedpolygon", "circumscribedpolygon" })
        foreach (var keyed in new[] { false, true })
        foreach (var vertex in Enumerable.Range(0, 5))
            yield return [tool, $"vertex-{vertex}", keyed];
        foreach (var tool in new[] { "ellipticalarc", "spline" })
        foreach (var keyed in new[] { false, true })
        foreach (var endpoint in new[] { "start", "end" })
            yield return [tool, endpoint, keyed];
        foreach (var tool in new[] { "inscribedpolygon", "circumscribedpolygon" })
        foreach (var keyed in new[] { false, true })
            yield return [tool, "center", keyed];
    }

    [Theory]
    [MemberData(nameof(CreationCases))]
    public void ExplicitCreationSnapResolvesLiveSourceAndPersistsOneRelation(string tool, string label, bool keyed)
    {
        var (source, point) = Source(tool, label);
        var created = CreateLine(point, keyed);
        // Acquisition coordinates can be rounded or stale; the label identifies the live point.
        var key = $"source|point|{label}|999|-999";
        var before = DxferDocumentSerializer.Write(new DrawingDocument([source, created]));
        var constraints = Insert(source, created, point, key);

        var relation = constraints.Should().ContainSingle().Which;
        relation.Kind.Should().Be(SketchConstraintKind.Coincident);
        relation.ReferenceKeys.Should().Contain("created:start");
        SketchReference.TryParse(key, out var reference).Should().BeTrue();
        relation.ReferenceKeys.Any(k => SketchReference.TryParse(k, out var actual) && actual == reference).Should().BeTrue();
        var document = new DrawingDocument([source, created], [], constraints);
        AssertSatisfied(document);
        AssertSatisfied(DxferDocumentSerializer.Read(DxferDocumentSerializer.Write(document)));
        DxferDocumentSerializer.Write(new DrawingDocument([source, created])).Should().Be(before);
        created.Start.Should().Be(point);
        Distance(created.Start, created.End).Should().BeApproximately(keyed ? 12 : 5, 1e-9);
    }

    [Theory]
    [MemberData(nameof(CreationCases))]
    public void AutoOffRetainsPlacementWithoutRelations(string tool, string label, bool keyed)
    {
        var (source, point) = Source(tool, label);
        var created = CreateLine(point, keyed);

        Insert(source, created, point, $"source|point|{label}|999|-999", automatic: false).Should().BeEmpty();

        created.Start.Should().Be(point);
        Distance(created.Start, created.End).Should().BeApproximately(keyed ? 12 : 5, 1e-9);
    }

    [Theory]
    [InlineData("inscribedpolygon", "vertex-0")]
    [InlineData("circumscribedpolygon", "vertex-4")]
    [InlineData("ellipticalarc", "start")]
    [InlineData("ellipticalarc", "end")]
    public void StaleSourceCannotBeValidatedUsingStoredCanvasCoordinates(string tool, string label)
    {
        var (source, point) = Source(tool, label);
        var created = CreateLine(point, false);
        var moved = source.Transform(Transform2.Translation(4, 7));
        var key = FormattableString.Invariant($"source|point|{label}|{point.X:R}|{point.Y:R}");
        var before = DxferDocumentSerializer.Write(new DrawingDocument([moved, created]));

        var act = () => Insert(moved, created, point, key);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Creation snap no longer matches its source. Geometry unchanged.");
        DxferDocumentSerializer.Write(new DrawingDocument([moved, created])).Should().Be(before);
    }

    [Theory]
    [InlineData("inscribedpolygon", "vertex-2", "Horizontal")]
    [InlineData("circumscribedpolygon", "vertex-3", "Vertical")]
    [InlineData("ellipticalarc", "start", "Horizontal")]
    [InlineData("ellipticalarc", "end", "Vertical")]
    public void AxisCreationSnapsUseTheSameLiveReferenceReader(string tool, string label, string kind)
    {
        var (source, point) = Source(tool, label);
        var start = kind == "Horizontal" ? new Point2(point.X + 20, point.Y) : new Point2(point.X, point.Y + 20);
        var created = CreateLine(start, false);
        var constraints = Insert(source, created, start, $"source|point|{label}|999|-999", kind: kind);

        constraints.Should().ContainSingle().Which.Kind.ToString().Should().Be(kind);
        AssertSatisfied(new DrawingDocument([source, created], [], constraints));
    }

    private static (DrawingEntity Entity, Point2 Point) Source(string tool, string label)
    {
        var entities = SketchCreationEntityFactory.CreateEntitiesForTool(tool,
            [new(17, -9), new(23, -1), new(13, -6), new(11, -13)],
            _ => EntityId.Create("source"), false, new Dictionary<string, double> { ["sides"] = 5 });
        var source = entities.Single();
        var point = source switch
        {
            PolygonEntity polygon => label == "center" ? polygon.Center : polygon.GetVertices()[int.Parse(label[7..])],
            EllipseEntity ellipse => ellipse.PointAtParameterDegrees(label == "start"
                ? ellipse.StartParameterDegrees : ellipse.EndParameterDegrees),
            SplineEntity spline => label == "start" ? spline.FitPoints[0] : spline.FitPoints[^1],
            _ => throw new InvalidOperationException()
        };
        return (source, point);
    }

    private static LineEntity CreateLine(Point2 start, bool keyed) =>
        (LineEntity)SketchCreationEntityFactory.CreateEntitiesForTool("line",
            [start, new(start.X + 3, start.Y + 4)], _ => EntityId.Create("created"), false,
            keyed ? new Dictionary<string, double> { ["length"] = 12 } : null).Single();

    private static IReadOnlyList<SketchConstraint> Insert(DrawingEntity source, LineEntity created,
        Point2 point, string key, bool automatic = true, string kind = "Coincident")
    {
        var index = 0;
        return SketchCreationConstraintFactory.CreateConstraintsForInsertion("line", [source], [created],
            _ => $"c{++index}", creationSnaps: [new CanvasCreationSnapDto(point, kind, [key])],
            automaticSnapConstraints: automatic);
    }

    private static void AssertSatisfied(DrawingDocument document) =>
        document.Constraints.Should().AllSatisfy(c =>
            SketchConstraintService.ValidateConstraint(document, c).State.Should().Be(SketchConstraintState.Satisfied));

    private static double Distance(Point2 first, Point2 second) =>
        Math.Sqrt(Math.Pow(second.X - first.X, 2) + Math.Pow(second.Y - first.Y, 2));
}

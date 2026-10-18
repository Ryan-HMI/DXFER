using DXFER.Blazor.Sketching;
using DXFER.CadIO;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class ImplicitSnapPersistenceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PointEntitySnapPersistsWithoutMovingTargetOrDuplicatingRelation(bool midpoint)
    {
        var document = new DrawingDocument(new DrawingEntity[] {
            new PointEntity(EntityId.Create("point"), new(-2, 1)),
            new LineEntity(EntityId.Create("source"), new(2, 3), new(6, 3)) });
        var target = new Point2(midpoint ? 4 : 2, 3);
        SketchGeometryDragService.TryApplyDrag(document, "point", new(-2, 1), target, false,
            out var snapped, out var status).Should().BeTrue(status);
        snapped.Entities[1].Should().Be(document.Entities[1]);
        snapped.Constraints.Should().ContainSingle(c => c.Kind == (midpoint ? SketchConstraintKind.Midpoint : SketchConstraintKind.Coincident));
        var reopened = RoundTrip(snapped);
        SketchGeometryDragService.TryApplyDrag(reopened, "source|point|start|2|3", new(2, 3), new(2, 5), false,
            out var changed, out status).Should().BeTrue(status);
        changed.Constraints.Should().ContainSingle();
        var point = (PointEntity)changed.Entities[0];
        point.Location.Should().Be(new Point2(target.X, midpoint ? 4 : 5));
    }

    [Theory]
    [InlineData("ellipse", false)]
    [InlineData("ellipse", true)]
    [InlineData("spline", false)]
    [InlineData("spline", true)]
    public void CreationAtSupportedCurvePointSavesCoincident(string kind, bool curveCreated)
    {
        var curve = Curve(kind);
        var line = new LineEntity(EntityId.Create("line"), new(2, 3), new(7, 8));
        var existing = new DrawingEntity[] { curveCreated ? line : curve };
        var created = new DrawingEntity[] { curveCreated ? curve : line };
        var count = 0;
        var constraints = SketchCreationConstraintFactory.CreateConstraintsForInsertion(curveCreated ? kind : "line", existing, created, _ => $"c{count++}");
        var reference = kind == "ellipse" ? "curve:center" : "curve:start";
        constraints.Should().Contain(c => c.Kind == SketchConstraintKind.Coincident
            && c.ReferenceKeys.Contains(reference) && c.ReferenceKeys.Contains("line:start"));
        var document = SketchConstraintService.ApplyConstraints(new DrawingDocument(existing.Concat(created)), constraints);
        AssertNativeAndEdit(document, reference, new(2, 3), new(3, 4), "line:start");
    }

    [Theory]
    [InlineData("ellipse")]
    [InlineData("spline")]
    public void DragToSupportedCurvePointPersistsAndFollows(string kind)
    {
        var document = new DrawingDocument(new DrawingEntity[] { Curve(kind), new LineEntity(EntityId.Create("line"), new(-2, 1), new(7, 8)) });
        SketchGeometryDragService.TryApplyDrag(document, "line|point|start|-2|1", new(-2, 1), new(2, 3), false,
            out var snapped, out var status).Should().BeTrue(status);
        snapped.Constraints.Should().ContainSingle(c => c.Kind == SketchConstraintKind.Coincident);
        AssertNativeAndEdit(snapped, kind == "ellipse" ? "curve:center" : "curve:start", new(2, 3), new(3, 4), "line:start");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DragToMidpointPersistsAndFollowsLine(bool polyline)
    {
        DrawingEntity source = polyline
            ? new PolylineEntity(EntityId.Create("source"), new Point2[] { new(2, 3), new(6, 3), new(8, 7) })
            : new LineEntity(EntityId.Create("source"), new(2, 3), new(6, 3));
        var document = new DrawingDocument(new DrawingEntity[] { source, new LineEntity(EntityId.Create("line"), new(-2, 1), new(7, 8)) });
        SketchGeometryDragService.TryApplyDrag(document, "line|point|start|-2|1", new(-2, 1), new(4, 3), false,
            out var snapped, out var status).Should().BeTrue(status);
        snapped.Constraints.Should().ContainSingle(c => c.Kind == SketchConstraintKind.Midpoint);
        var reopened = RoundTrip(snapped);
        var key = polyline ? "source|point|vertex-1|6|3" : "source|point|end|6|3";
        SketchGeometryDragService.TryApplyDrag(reopened, key, new(6, 3), new(8, 3), false,
            out var changed, out status).Should().BeTrue(status);
        SketchReferenceResolver.TryGetPoint(changed, "line:start", out var moved).Should().BeTrue();
        moved.Should().Be(new Point2(5, 3));
        changed.Constraints.Should().OnlyContain(c => c.State == SketchConstraintState.Satisfied);
    }

    private static DrawingEntity Curve(string kind) => kind == "ellipse"
        ? new EllipseEntity(EntityId.Create("curve"), new(2, 3), new(4, 0), .5)
        : new SplineEntity(EntityId.Create("curve"), 3, new Point2[] { new(2, 3), new(3, 3), new(5, 5), new(6, 5) }, new double[] { 0, 0, 0, 0, 1, 1, 1, 1 });

    private static DrawingDocument RoundTrip(DrawingDocument document)
    {
        var reopened = DxfDocumentReader.Read(DxfDocumentWriter.Write(document, new DxfWriteOptions(IncludeNativeDocument: true)));
        reopened.Constraints.Should().BeEquivalentTo(document.Constraints);
        return reopened;
    }

    private static void AssertNativeAndEdit(DrawingDocument document, string reference, Point2 from, Point2 to, string follower)
    {
        document.Constraints.Should().OnlyContain(c => c.State == SketchConstraintState.Satisfied);
        var key = reference.Replace(":", "|point|") + $"|{from.X}|{from.Y}";
        SketchGeometryDragService.TryApplyDrag(RoundTrip(document), key, from, to, false, out var changed, out var status).Should().BeTrue(status);
        SketchReferenceResolver.TryGetPoint(changed, follower, out var moved).Should().BeTrue();
        moved.Should().Be(to);
        changed.Constraints.Should().OnlyContain(c => c.State == SketchConstraintState.Satisfied);
    }
}

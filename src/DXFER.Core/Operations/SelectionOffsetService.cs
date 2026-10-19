using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Buffer;
using NetTopologySuite.Operation.Linemerge;

namespace DXFER.Core.Operations;

/// <summary>One shared contour calculation for preview and commit; originals are never rewritten.</summary>
public static class SelectionOffsetService
{
    private const double Tolerance = 1e-8;
    private static readonly GeometryFactory Factory = new();
    private static readonly BufferParameters Parameters = new() { JoinStyle = JoinStyle.Mitre, MitreLimit = 5, SimplifyFactor = 0 };

    public static bool TryOffset(DrawingDocument document, IEnumerable<string> ids, Point2 through,
        Func<string, EntityId> createId, out DrawingDocument result, out int count, out string reason,
        out Point2 anchor, out double distance, double? requestedDistance = null)
    {
        result = document;
        count = 0;
        anchor = through;
        distance = 0;
        reason = "Select lines, polylines, polygons, circles or arcs and a distinct offset side.";
        var selectedIds = ids.ToHashSet(StringComparer.Ordinal);
        var selected = document.Entities.Where(e => selectedIds.Contains(e.Id.Value)).ToArray();
        if (selected.Length == 0 || selected.Length != selectedIds.Count || selected.Length > 512
            || !Finite(through)) return false;
        if (selected.Length > 1 && selected.Any(e => e is ArcEntity))
        {
            reason = "Joined arc offsets are not supported yet. Offset arcs individually. Nothing changed.";
            return false;
        }
        var paths = new List<(LineString Path, DrawingEntity Source)>();
        var round = new List<DrawingEntity>();
        var vertices = 0;
        foreach (var entity in selected)
        {
            Point2[] points;
            switch (entity)
            {
                case LineEntity line: points = [line.Start, line.End]; break;
                case PolylineEntity polyline: points = polyline.Vertices.ToArray(); break;
                case PolygonEntity polygon:
                    points = polygon.GetVertices().Append(polygon.GetVertices()[0]).ToArray(); break;
                case CircleEntity or ArcEntity: round.Add(entity); continue;
                default:
                    reason = $"Offset does not support {entity.Kind} geometry. Nothing changed.";
                    return false;
            }
            if ((vertices += points.Length) > 32768 || points.Any(p => !Finite(p))) return false;
            for (var i = 1; i < points.Length; i++)
                if (Length(points[i-1], points[i]) <= Tolerance) return false;
            paths.Add((Factory.CreateLineString(points.Select(Coordinate).ToArray()), entity));
        }

        try
        {
            var linework = Factory.CreateMultiLineString(paths.Select(p => p.Path).ToArray());
            // Ambiguous branches and crossings cannot be silently treated as separate edges.
            var endpoints = paths.SelectMany(p => new[] { p.Path.StartPoint.Coordinate, p.Path.EndPoint.Coordinate }).ToArray();
            if (!linework.IsSimple || endpoints.Any(p => endpoints.Count(q => p.Equals2D(q)) > 2))
            {
                reason = "Offset needs unbranched, noncrossing contours. Nothing changed.";
                return false;
            }
            var merger = new LineMerger();
            merger.Add((NetTopologySuite.Geometries.Geometry)linework);
            var contours = merger.GetMergedLineStrings().Cast<LineString>().ToArray();
            var pointer = Coordinate(through);
            var nearest = double.PositiveInfinity;
            foreach (var contour in contours)
            {
                var coords = contour.Coordinates;
                for (var i = 1; i < coords.Length; i++)
                {
                    var segment = new LineSegment(coords[i-1], coords[i]);
                    var projection = segment.ClosestPoint(pointer);
                    var candidate = projection.Distance(pointer);
                    if (candidate < nearest) { nearest = candidate; anchor = Point(contour.IsClosed ? projection : segment.Project(pointer)); }
                }
            }
            foreach (var entity in round)
            {
                var (center, radius) = CircleData(entity);
                var length = Length(center, through);
                if (!Finite(center) || !double.IsFinite(radius) || radius <= Tolerance || length <= Tolerance) return false;
                var edge = new Point2(center.X + (through.X-center.X)*radius/length, center.Y + (through.Y-center.Y)*radius/length);
                var candidate = Length(edge, through);
                if (candidate < nearest) { nearest = candidate; anchor = edge; }
            }
            distance = requestedDistance ?? Length(anchor, through);
            if (!double.IsFinite(distance) || distance <= Tolerance) return false;
            var additions = new List<DrawingEntity>();
            var styles = new Dictionary<string, DxfEntityStyle>(document.Metadata.EntityStyles, StringComparer.Ordinal);
            foreach (var contour in contours)
            {
                var sourcePaths = paths.Where(p => contour.Covers(p.Path)).ToArray();
                if (sourcePaths.Length == 0) return false;
                var source = sourcePaths[0].Source;
                var style = Style(document, source);
                if (sourcePaths.Any(p => Style(document, p.Source) != style || p.Source.IsConstruction != source.IsConstruction))
                {
                    reason = "Connected offset edges must share a layer and style. Nothing changed.";
                    return false;
                }
                NetTopologySuite.Geometries.Geometry offset;
                if (contour.IsClosed)
                {
                    if (contour.NumPoints < 4) return false;
                    var polygon = Factory.CreatePolygon(contour.Coordinates);
                    if (!polygon.IsValid) return false;
                    var signed = polygon.Contains(Factory.CreatePoint(pointer)) ? -distance : distance;
                    offset = BufferOp.Buffer(polygon, signed, Parameters);
                    if (offset is not Polygon output || output.IsEmpty || output.NumInteriorRings != 0)
                    {
                        reason = "Offset collapses or splits this contour. Use a smaller distance. Nothing changed.";
                        return false;
                    }
                    offset = output.ExteriorRing;
                }
                else
                {
                    var segments = contour.Coordinates.Zip(contour.Coordinates.Skip(1), (a,b) => new LineSegment(a,b));
                    var closest = segments.OrderBy(s => s.Distance(pointer)).First();
                    var sign = (closest.P1.X-closest.P0.X)*(pointer.Y-closest.P0.Y)-(closest.P1.Y-closest.P0.Y)*(pointer.X-closest.P0.X);
                    if (Math.Abs(sign) <= Tolerance) return false;
                    offset = new OffsetCurve(contour, Math.CopySign(distance, sign), Parameters).GetCurve();
                }
                if (offset is not LineString outputLine || outputLine.IsEmpty || !outputLine.IsSimple || outputLine.NumPoints < 2)
                {
                    reason = "Offset cannot produce one continuous contour. Nothing changed.";
                    return false;
                }
                var outputPoints = outputLine.Coordinates.Select(Point).ToArray();
                DrawingEntity addition = sourcePaths.Length == 1 && source is LineEntity && outputPoints.Length == 2
                    ? new LineEntity(createId("line"), outputPoints[0], outputPoints[1], source.IsConstruction)
                    : new PolylineEntity(createId("polyline"), outputPoints, source.IsConstruction);
                additions.Add(addition);
                if (document.Metadata.EntityStyles.ContainsKey(source.Id.Value)) styles[addition.Id.Value] = style;
            }
            foreach (var entity in round)
            {
                var (center, radius) = CircleData(entity);
                var newRadius = radius + (Length(center, through) < radius ? -distance : distance);
                if (newRadius <= Tolerance) { reason = "Offset collapses a circle or arc. Nothing changed."; return false; }
                DrawingEntity addition = entity is ArcEntity arc
                    ? new ArcEntity(createId("arc"), center, newRadius, arc.StartAngleDegrees, arc.EndAngleDegrees, arc.IsConstruction)
                    : new CircleEntity(createId("circle"), center, newRadius, entity.IsConstruction);
                additions.Add(addition);
                if (document.Metadata.EntityStyles.ContainsKey(entity.Id.Value)) styles[addition.Id.Value] = Style(document, entity);
            }
            if (additions.Any(e => document.Entities.Any(original => original.Id == e.Id))
                || additions.Select(e => e.Id).Distinct().Count() != additions.Count) return false;
            count = additions.Count;
            result = new DrawingDocument(document.Entities.Concat(additions), document.Dimensions, document.Constraints,
                styles.Count == document.Metadata.EntityStyles.Count ? document.Metadata : document.Metadata with { EntityStyles = styles });
            reason = "";
            return count > 0;
        }
        catch (Exception ex) when (ex is TopologyException or ArgumentException or InvalidOperationException)
        {
            reason = "Offset topology could not be resolved. Nothing changed.";
            return false;
        }
    }

    private static DxfEntityStyle Style(DrawingDocument document, DrawingEntity entity) =>
        document.Metadata.EntityStyles.GetValueOrDefault(entity.Id.Value) ?? new DxfEntityStyle();
    private static (Point2 Center, double Radius) CircleData(DrawingEntity entity) => entity switch
    {
        CircleEntity circle => (circle.Center, circle.Radius),
        ArcEntity arc => (arc.Center, arc.Radius),
        _ => throw new ArgumentException(nameof(entity))
    };
    private static bool Finite(Point2 p) => double.IsFinite(p.X) && double.IsFinite(p.Y) && Math.Abs(p.X) <= 1e6 && Math.Abs(p.Y) <= 1e6;
    private static double Length(Point2 a, Point2 b) => Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Y-b.Y)*(a.Y-b.Y));
    private static Coordinate Coordinate(Point2 p) => new(p.X,p.Y);
    private static Point2 Point(Coordinate p) => new(p.X,p.Y);
}

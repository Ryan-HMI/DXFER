using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Operations;
using System.Globalization;

namespace DXFER.Core.Sketching;

public static class SketchReferenceResolver
{
    public static IEnumerable<(SketchReference Reference, Point2 Point)> EnumeratePersistentPoints(IReadOnlyList<DrawingEntity> entities)
    {
        ArgumentNullException.ThrowIfNull(entities);
        foreach (var entity in entities)
        {
            if (entity is PolylineEntity polyline)
            {
                for (var index = 0; index < polyline.Vertices.Count - 1; index++)
                {
                    yield return (new SketchReference(entity.Id.Value, SketchReferenceTarget.Start, index), polyline.Vertices[index]);
                    yield return (new SketchReference(entity.Id.Value, SketchReferenceTarget.End, index), polyline.Vertices[index + 1]);
                }
                continue;
            }

            var targets = entity is PointEntity
                ? new[] { SketchReferenceTarget.Entity }
                : new[] { SketchReferenceTarget.Start, SketchReferenceTarget.End, SketchReferenceTarget.Center };
            foreach (var target in targets)
            {
                var reference = new SketchReference(entity.Id.Value, target);
                if (SketchGeometryEditor.TryGetPoint(entities, reference, out var point))
                    yield return (reference, point);
            }
        }
    }

    public static bool TryGetEntity(DrawingDocument document, string key, out DrawingEntity entity)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!SketchReference.TryParse(key, out var reference)
            || reference.Target != SketchReferenceTarget.Entity)
        {
            entity = default!;
            return false;
        }

        return SketchGeometryEditor.TryFindEntity(document.Entities, reference.EntityId, out _, out entity);
    }

    public static bool TryGetPoint(DrawingDocument document, string key, out Point2 point)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (SketchReference.TryParse(key, out var midpointReference)
            && midpointReference.Target == SketchReferenceTarget.Midpoint
            && document.Entities.Any(e => e.Id.Value == midpointReference.EntityId && e is PolylineEntity)
            && document.Metadata.Warnings.Any(w => w.Code == CutPathLengthService.UnsafeImportCode))
        {
            point = default;
            return false;
        }

        if (SketchReference.TryParse(key, out var reference)
            && SketchGeometryEditor.TryGetPoint(document.Entities, reference, out point))
        {
            return true;
        }

        if (SketchReference.TryParseCanvasPointCoordinates(key, out var entityId, out var label, out point)
            && SketchGeometryEditor.TryFindEntity(document.Entities, entityId, out _, out var entity))
        {
            if (entity is PolylineEntity polyline) return TryGetPolylineCanvasPoint(polyline, label, out point);
            if (entity is SplineEntity spline) return TryGetSplineCanvasPoint(spline, label, out point);
            return true;
        }

        point = default;
        return false;
    }

    internal static bool TryGetSplineCanvasPoint(SplineEntity spline, string label, out Point2 point)
    {
        point = default;
        var fit = spline.FitPoints.Count >= 2;
        var points = fit ? spline.FitPoints : spline.ControlPoints;
        var prefix = fit ? "fit-" : "control-";
        if (StringComparer.OrdinalIgnoreCase.Equals(label, "start") || StringComparer.OrdinalIgnoreCase.Equals(label, "end"))
        {
            var curve = fit ? points : spline.GetSamplePoints();
            point = curve[StringComparer.OrdinalIgnoreCase.Equals(label, "start") ? 0 : curve.Count - 1];
            return true;
        }
        var index = -1;
        if (label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && !int.TryParse(label[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out index)) return false;
        if (index >= 0 && index < points.Count)
        {
            point = points[index];
            return true;
        }
        var start = StringComparer.OrdinalIgnoreCase.Equals(label, "tangent-start");
        if (!start && !StringComparer.OrdinalIgnoreCase.Equals(label, "tangent-end")) return false;
        if (fit)
        {
            var handle = start ? spline.StartTangentHandle : spline.EndTangentHandle;
            if (handle is not { } current) return false;
            point = current;
            return true;
        }
        if (spline.GetTangentControlPointIndex(start) is not { } adjacent) return false;
        var endpoint = points[start ? 0 : points.Count - 1];
        point = new Point2(endpoint.X + (points[adjacent].X - endpoint.X) * 0.25,
            endpoint.Y + (points[adjacent].Y - endpoint.Y) * 0.25);
        return true;
    }

    internal static bool TryGetPolylineCanvasPoint(PolylineEntity polyline, string label, out Point2 point)
    {
        var vertex = label.StartsWith("vertex-", StringComparison.OrdinalIgnoreCase);
        var midpoint = label.StartsWith("mid-", StringComparison.OrdinalIgnoreCase);
        if ((vertex || midpoint)
            && int.TryParse(label[(vertex ? 7 : 4)..], NumberStyles.None, CultureInfo.InvariantCulture, out var index)
            && index >= 0 && index < polyline.Vertices.Count - (midpoint ? 1 : 0))
        {
            var first = polyline.Vertices[index];
            point = midpoint ? new Point2((first.X + polyline.Vertices[index + 1].X) / 2,
                (first.Y + polyline.Vertices[index + 1].Y) / 2) : first;
            return true;
        }
        point = default;
        return false;
    }

    public static bool TryGetLine(DrawingDocument document, string key, out LineEntity line)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (SketchReference.TryParse(key, out var reference)
            && SketchGeometryEditor.TryGetLine(document.Entities, reference, out _, out line))
        {
            return true;
        }

        line = default!;
        return false;
    }
}

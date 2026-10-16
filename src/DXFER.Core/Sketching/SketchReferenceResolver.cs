using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using System.Globalization;

namespace DXFER.Core.Sketching;

public static class SketchReferenceResolver
{
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

        if (SketchReference.TryParse(key, out var reference)
            && SketchGeometryEditor.TryGetPoint(document.Entities, reference, out point))
        {
            return true;
        }

        if (SketchReference.TryParseCanvasPointCoordinates(key, out var entityId, out var label, out point)
            && SketchGeometryEditor.TryFindEntity(document.Entities, entityId, out _, out var entity))
        {
            return entity is not PolylineEntity polyline || TryGetPolylineCanvasPoint(polyline, label, out point);
        }

        point = default;
        return false;
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

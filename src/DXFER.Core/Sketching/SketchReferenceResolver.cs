using DXFER.Core.Documents;
using DXFER.Core.Geometry;

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

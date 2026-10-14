using System.Globalization;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;

namespace DXFER.Core.Operations;

// Open only the selected vertex; retain the rest of each path without tessellation.
internal sealed class PolylineCornerTopology
{
    private readonly PolylineEntity source;
    private readonly int corner;
    private readonly bool closed;
    private readonly SketchReference[] segments;

    private PolylineCornerTopology(PolylineEntity source, int corner, bool closed,
        IReadOnlyList<PolylineEntity> paths, SketchReference[] segments)
    {
        this.source = source;
        this.corner = corner;
        this.closed = closed;
        this.segments = segments;
        Paths = paths;
    }

    public IReadOnlyList<PolylineEntity> Paths { get; }

    public static bool TryCreate(PolylineEntity source, int firstIndex, int secondIndex,
        LineEntity firstTrimmed, LineEntity secondTrimmed, Func<string, EntityId> createEntityId,
        out PolylineCornerTopology topology)
    {
        topology = default!;
        var count = source.Vertices.Count - 1;
        var closed = source.Vertices[0] == source.Vertices[^1];
        if (firstIndex < 0 || secondIndex < 0 || firstIndex >= count || secondIndex >= count || firstIndex == secondIndex)
            return false;
        var incoming = firstIndex;
        var outgoing = secondIndex;
        var incomingTrim = firstTrimmed;
        var outgoingTrim = secondTrimmed;
        if (secondIndex + 1 == firstIndex || closed && secondIndex == count - 1 && firstIndex == 0)
        {
            (incoming, outgoing) = (secondIndex, firstIndex);
            (incomingTrim, outgoingTrim) = (secondTrimmed, firstTrimmed);
        }
        if (incoming + 1 != outgoing && !(closed && incoming == count - 1 && outgoing == 0))
            return false;

        var map = new SketchReference[count];
        var paths = new List<PolylineEntity>();
        if (closed)
        {
            var points = new List<Point2> { outgoingTrim.Start };
            for (var offset = 0; offset < count; offset++)
            {
                var index = (outgoing + offset) % count;
                points.Add(index == incoming ? incomingTrim.End : source.Vertices[index + 1]);
                map[index] = new SketchReference(source.Id.Value, SketchReferenceTarget.Entity, offset);
            }
            paths.Add(new PolylineEntity(source.Id, points, source.IsConstruction));
        }
        else
        {
            var before = source.Vertices.Take(outgoing + 1).ToArray();
            var after = source.Vertices.Skip(outgoing).ToArray();
            before[^1] = incomingTrim.End;
            after[0] = outgoingTrim.Start;
            var afterId = createEntityId("polyline-corner");
            paths.Add(new PolylineEntity(source.Id, before, source.IsConstruction));
            paths.Add(new PolylineEntity(afterId, after, source.IsConstruction));
            for (var index = 0; index < count; index++)
                map[index] = new SketchReference(index < outgoing ? source.Id.Value : afterId.Value,
                    SketchReferenceTarget.Entity, index < outgoing ? index : index - outgoing);
        }
        topology = new PolylineCornerTopology(source, outgoing, closed, paths, map);
        return true;
    }

    public bool TryMapKeys(IEnumerable<string> keys, out string[] mapped)
    {
        var result = new List<string>();
        foreach (var key in keys)
        {
            if (!TryMapKey(key, out var next))
            {
                mapped = Array.Empty<string>();
                return false;
            }
            result.Add(next);
        }
        mapped = result.ToArray();
        return true;
    }

    private bool TryMapKey(string key, out string mapped)
    {
        mapped = key;
        if (SketchReference.TryParseCanvasPointCoordinates(key, out var entityId, out var label, out _)
            && entityId == source.Id.Value)
        {
            var parts = label.Split('-');
            if (parts.Length != 2 || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var index))
                return false;
            var vertex = parts[0] == "vertex";
            if ((!vertex && parts[0] != "mid") || index < 0 || index >= segments.Length + (vertex ? 1 : 0))
                return false;
            // A removed shared vertex has two different successors; there is no unique point mapping.
            if (vertex && (index == corner || closed && corner == 0 && index == segments.Length))
                return false;
            var segment = segments[Math.Min(index, segments.Length - 1)];
            var path = Paths.Single(p => p.Id.Value == segment.EntityId);
            var newIndex = segment.SegmentIndex!.Value;
            Point2 point;
            if (vertex)
            {
                if (index == segments.Length) newIndex++;
                point = path.Vertices[newIndex];
            }
            else
            {
                var start = path.Vertices[newIndex];
                var end = path.Vertices[newIndex + 1];
                point = new Point2((start.X + end.X) / 2, (start.Y + end.Y) / 2);
            }
            mapped = string.Create(CultureInfo.InvariantCulture,
                $"{segment.EntityId}|point|{parts[0]}-{newIndex}|{point.X}|{point.Y}");
            return true;
        }

        if (!SketchReference.TryParse(key, out var reference))
            return !key.StartsWith(source.Id.Value + "|", StringComparison.Ordinal)
                && !key.StartsWith(source.Id.Value + ":", StringComparison.Ordinal);
        if (reference.EntityId != source.Id.Value)
            return !key.StartsWith(source.Id.Value + "|segment|", StringComparison.Ordinal)
                && !key.StartsWith(source.Id.Value + "|point|", StringComparison.Ordinal);
        // Whole-path references cannot be reassigned to just one resulting piece.
        if (reference.SegmentIndex is not { } oldIndex || oldIndex < 0 || oldIndex >= segments.Length
            || reference.Target == SketchReferenceTarget.Center)
            return false;
        var target = segments[oldIndex];
        mapped = new SketchReference(target.EntityId, reference.Target, target.SegmentIndex).ToString();
        return true;
    }
}

using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Operations;

namespace DXFER.Core.Sketching;

/// <summary>Directional contour dependency. References are source IDs followed by the dependent ID.</summary>
public static class SketchOffsetRelation
{
    private const double Epsilon = 1e-7;
    private sealed record Witness(string SourceId, int Segment, double Side, Point2 Start, Point2 End, double Gap);

    public static bool TryMeasure(IReadOnlyList<DrawingEntity> entities, IReadOnlyList<string> references,
        out double gap, out Point2 start, out Point2 end)
    {
        gap = 0; start = end = default;
        if (!TryWitness(entities, references, out var witness)) return false;
        gap = witness.Gap; start = witness.Start; end = witness.End;
        return true;
    }

    public static bool IsSatisfied(IReadOnlyList<DrawingEntity> entities, IReadOnlyList<string> references) =>
        TryWitness(entities, references, out var witness)
        && TryBuild(ValidationTopology(entities, references), references, witness, witness.Gap, out var expected)
        && SameGeometry(expected, entities.First(e => e.Id.Value == references[^1]));

    private static IReadOnlyList<DrawingEntity> ValidationTopology(
        IReadOnlyList<DrawingEntity> entities, IReadOnlyList<string> references)
    {
        // NTS merges only bit-identical endpoints. Native equations satisfy
        // coincidence numerically; canonicalize the validation copy only.
        const double weldTolerance = 1e-8;
        var sourceIds = references.SkipLast(1).ToHashSet(StringComparer.Ordinal);
        var endpoints = entities.Where(e => sourceIds.Contains(e.Id.Value)).SelectMany(e => e switch
        {
            LineEntity line => new[] { line.Start, line.End },
            PolylineEntity poly => new[] { poly.Vertices[0], poly.Vertices[^1] },
            _ => Array.Empty<Point2>()
        }).ToArray();
        Point2 Canonical(Point2 point)
        {
            var neighbors = endpoints.Where(p => Distance(p, point) <= weldTolerance).ToArray();
            // Do not bridge chains of individually close but collectively distant points.
            if (neighbors.Any(p => neighbors.Any(q => Distance(p, q) > weldTolerance))) return point;
            return neighbors.OrderBy(p => p.X).ThenBy(p => p.Y).FirstOrDefault(point);
        }
        return entities.Select<DrawingEntity, DrawingEntity>(entity =>
        {
            if (!sourceIds.Contains(entity.Id.Value)) return entity;
            if (entity is LineEntity line)
                return line with { Start = Canonical(line.Start), End = Canonical(line.End) };
            if (entity is PolylineEntity poly)
            {
                var points = poly.Vertices.ToArray();
                points[0] = Canonical(points[0]);
                points[^1] = Canonical(points[^1]);
                return new PolylineEntity(poly.Id, points, poly.IsConstruction);
            }
            return entity;
        }).ToArray();
    }

    internal static bool Propagate(IReadOnlyList<DrawingEntity> original, DrawingEntity[] entities,
        SketchFixedReferences fixedReferences, SketchConstraint constraint, bool allowDependentTranslation = false)
    {
        var keys = constraint.ReferenceKeys;
        if (keys.Count < 2) return false;
        if (!keys.SkipLast(1).Any(id =>
            original.FirstOrDefault(e => e.Id.Value == id) is { } before
            && entities.FirstOrDefault(e => e.Id.Value == id) is { } after && !SameGeometry(before, after)))
            return allowDependentTranslation && TryFollowDependentTranslation(original, entities, fixedReferences, keys);
        return IsSatisfied(original, keys) && TryWitness(original, keys, out var witness)
            && TryBuild(entities, keys, witness, witness.Gap, out var output, original)
            && Replace(entities, output, fixedReferences);
    }

    private static bool TryFollowDependentTranslation(IReadOnlyList<DrawingEntity> original, DrawingEntity[] entities,
        SketchFixedReferences fixedReferences, IReadOnlyList<string> keys)
    {
        var before = original.FirstOrDefault(e => e.Id.Value == keys[^1]);
        var after = entities.FirstOrDefault(e => e.Id.Value == keys[^1]);
        if (before is null || after is null || SameGeometry(before, after) || !IsSatisfied(original, keys)) return false;
        var oldBounds = before.GetBounds();
        var newBounds = after.GetBounds();
        var translation = Transform2.Translation(newBounds.MinX - oldBounds.MinX, newBounds.MinY - oldBounds.MinY);
        if (!SameGeometry(before.Transform(translation), after)) return false;

        // A newly applied relation may position a dependent contour. Only a rigid
        // translation can flow back to its sources; never infer a resize or gap edit.
        var candidate = entities.ToArray();
        foreach (var id in keys.SkipLast(1))
        {
            var source = candidate.FirstOrDefault(e => e.Id.Value == id);
            if (source is null || !Replace(candidate, source.Transform(translation), fixedReferences)) return false;
        }
        if (!IsSatisfied(candidate, keys)) return false;
        Array.Copy(candidate, entities, entities.Length);
        return true;
    }

    internal static bool ApplyGap(DrawingEntity[] entities, IReadOnlyList<string> references,
        double gap, SketchFixedReferences fixedReferences) =>
        double.IsFinite(gap) && gap > Epsilon
        && TryWitness(entities, references, out var witness)
        && TryBuild(entities, references, witness, gap, out var output)
        && Replace(entities, output, fixedReferences);

    private static bool Replace(DrawingEntity[] entities, DrawingEntity output, SketchFixedReferences fixedReferences)
    {
        var index = Array.FindIndex(entities, e => e.Id == output.Id);
        if (index < 0 || SameGeometry(entities[index], output)
            || fixedReferences.IsWholeEntityFixed(new(output.Id.Value, SketchReferenceTarget.Entity))) return false;
        if (entities[index] is PolylineEntity previous && output is PolylineEntity rebuilt
            && previous.Vertices.Count != rebuilt.Vertices.Count) return false;
        var candidate = entities.ToArray(); candidate[index] = output;
        // Point fixes do not freeze a circle's radius, but no fixed point may be displaced.
        var checks = new List<SketchReference> { new(output.Id.Value, SketchReferenceTarget.Start),
            new(output.Id.Value, SketchReferenceTarget.End), new(output.Id.Value, SketchReferenceTarget.Center) };
        var segments = Segments(entities[index]).ToArray();
        for (var i = 0; i < segments.Length; i++)
        {
            checks.Add(new(output.Id.Value, SketchReferenceTarget.Start, i));
            checks.Add(new(output.Id.Value, SketchReferenceTarget.End, i));
        }
        if (checks.Any(r => fixedReferences.IsFixed(r)
            && SketchGeometryEditor.TryGetPoint(entities, r, out var before)
            && (!SketchGeometryEditor.TryGetPoint(candidate, r, out var after) || Distance(before,after) > Epsilon))) return false;
        entities[index] = output;
        return true;
    }

    private static bool TryWitness(IReadOnlyList<DrawingEntity> entities, IReadOnlyList<string> keys, out Witness witness)
    {
        witness = null!;
        if (keys.Count < 2 || keys.Count > 513 || keys.Distinct().Count() != keys.Count) return false;
        var sources = keys.SkipLast(1).Select(id => entities.FirstOrDefault(e => e.Id.Value == id)).ToArray();
        var target = entities.FirstOrDefault(e => e.Id.Value == keys[^1]);
        if (sources.Any(e => e is null) || target is null) return false;
        if (sources.Length == 1 && Round(sources[0]!, out var center, out var radius)
            && Round(target, out var targetCenter, out var targetRadius))
        {
            var gap = Math.Abs(targetRadius-radius);
            if (gap <= Epsilon || !double.IsFinite(gap) || Distance(center,targetCenter) > Epsilon) return false;
            witness = new(sources[0]!.Id.Value, -1, Math.Sign(targetRadius-radius),
                new(center.X+radius,center.Y), new(center.X+targetRadius,center.Y), gap);
            return true;
        }
        var nearest = double.PositiveInfinity;
        var targets = Segments(target).ToArray();
        if (targets.Length > 2048 || sources.Sum(e => Segments(e!).Count()) > 2048) return false;
        foreach (var source in sources)
        {
            var index = 0;
            foreach (var (a,b) in Segments(source!))
            {
                var dx=b.X-a.X; var dy=b.Y-a.Y; var length=Distance(a,b);
                if (length <= Epsilon) { index++; continue; }
                foreach (var (c,d) in targets)
                {
                    var targetLength=Distance(c,d);
                    if (targetLength <= Epsilon || Math.Abs(dx*(d.Y-c.Y)-dy*(d.X-c.X)) > Epsilon*length*targetLength) continue;
                    var mid=new Point2((c.X+d.X)/2,(c.Y+d.Y)/2);
                    var t=((mid.X-a.X)*dx+(mid.Y-a.Y)*dy)/(length*length);
                    if (t < -Epsilon || t > 1+Epsilon) continue;
                    var projected=new Point2(a.X+t*dx,a.Y+t*dy);
                    var gap=Distance(mid,projected);
                    if (gap <= Epsilon || gap >= nearest) continue;
                    nearest=gap;
                    witness=new(source!.Id.Value,index,Math.Sign(dx*(mid.Y-a.Y)-dy*(mid.X-a.X)),projected,mid,gap);
                }
                index++;
            }
        }
        return witness is not null;
    }

    private static bool TryBuild(IReadOnlyList<DrawingEntity> entities, IReadOnlyList<string> keys,
        Witness witness, double gap, out DrawingEntity output, IReadOnlyList<DrawingEntity>? previous = null)
    {
        output = null!;
        var source = entities.FirstOrDefault(e => e.Id.Value == witness.SourceId);
        var target = entities.FirstOrDefault(e => e.Id.Value == keys[^1]);
        if (source is null || target is null) return false;
        Point2 side;
        if (witness.Segment < 0)
        {
            if (!Round(source,out var center,out var radius)) return false;
            side = new(center.X+radius+witness.Side*Math.Min(radius/2,gap/2),center.Y);
        }
        else
        {
            var segments=Segments(source).ToArray();
            if (witness.Segment >= segments.Length) return false;
            var (a,b)=segments[witness.Segment];
            var length=Distance(a,b);
            if (length <= Epsilon) return false;
            var step=Math.Min(gap/4,length/1000);
            side=new((a.X+b.X)/2-(b.Y-a.Y)/length*witness.Side*step,
                (a.Y+b.Y)/2+(b.X-a.X)/length*witness.Side*step);
        }
        var sources=entities.Where(e => keys.SkipLast(1).Contains(e.Id.Value)).ToArray();
        if (!SelectionOffsetService.TryOffset(new DrawingDocument(sources), keys.SkipLast(1), side,
            _ => target.Id, out var result,out var count,out _,out _,out _,gap) || count != 1) return false;
        output=result.Entities.Last();
        var before = (previous ?? entities).FirstOrDefault(e => e.Id == target.Id);
        if (before is PolylineEntity oldPolyline && output is PolylineEntity newPolyline && !SameGeometry(before, output))
        {
            if (!TryAlignVertices(previous ?? entities, entities, keys, oldPolyline, newPolyline, witness.Gap, gap, out var aligned)) return false;
            output = aligned;
        }
        return true;
    }

    // Buffering can choose a different ring start/direction. Preserve vertex identity
    // by matching each dependent edge to its original source edge, never by proximity.
    private static bool TryAlignVertices(IReadOnlyList<DrawingEntity> previous, IReadOnlyList<DrawingEntity> current,
        IReadOnlyList<string> keys, PolylineEntity before, PolylineEntity after, double oldGap, double newGap, out PolylineEntity aligned)
    {
        aligned = after;
        if (before.Vertices.Count != after.Vertices.Count) return false;
        string[]? Signatures(IReadOnlyList<DrawingEntity> entities, PolylineEntity polyline, double gap)
        {
            var sources = entities.Where(e => keys.SkipLast(1).Contains(e.Id.Value))
                .SelectMany(e => Segments(e).Select((s,i) => (s.A,s.B,Key:$"{e.Id.Value}/{i}"))).ToArray();
            var result = new List<string>();
            foreach (var (a,b) in Segments(polyline))
            {
                var midpoint = new Point2((a.X+b.X)/2,(a.Y+b.Y)/2);
                var candidates = sources.Where(s =>
                {
                    var length = Distance(s.A,s.B);
                    if (length <= Epsilon || Distance(a,b) <= Epsilon) return false;
                    var cross = (b.X-a.X)*(s.B.Y-s.A.Y)-(b.Y-a.Y)*(s.B.X-s.A.X);
                    var normal = Math.Abs((midpoint.X-s.A.X)*(s.B.Y-s.A.Y)-(midpoint.Y-s.A.Y)*(s.B.X-s.A.X))/length;
                    var t = ((midpoint.X-s.A.X)*(s.B.X-s.A.X)+(midpoint.Y-s.A.Y)*(s.B.Y-s.A.Y))/(length*length);
                    return Math.Abs(cross) <= Epsilon*length*Distance(a,b) && Math.Abs(normal-gap) <= Epsilon && t >= -Epsilon && t <= 1+Epsilon;
                }).ToArray();
                if (candidates.Length != 1) return null;
                result.Add(candidates[0].Key);
            }
            return result.ToArray();
        }
        var oldKeys = Signatures(previous,before,oldGap);
        if (oldKeys is null) return false;
        var closed = Distance(before.Vertices[0],before.Vertices[^1]) <= Epsilon;
        var points = closed ? after.Vertices.SkipLast(1).ToArray() : after.Vertices.ToArray();
        foreach (var reverse in new[]{false,true})
        {
            var ordered = reverse ? points.Reverse().ToArray() : points;
            var orderedKeys = Signatures(current,new PolylineEntity(after.Id,closed ? ordered.Append(ordered[0]) : ordered,after.IsConstruction),newGap);
            if (orderedKeys is null) continue;
            for (var shift=0;shift<(closed ? points.Length : 1);shift++)
            {
                if (!oldKeys.SequenceEqual(orderedKeys.Skip(shift).Concat(orderedKeys.Take(shift)))) continue;
                var rotated = ordered.Skip(shift).Concat(ordered.Take(shift)).ToArray();
                aligned = new PolylineEntity(after.Id,closed ? rotated.Append(rotated[0]) : rotated,after.IsConstruction);
                return true;
            }
        }
        return false;
    }

    private static IEnumerable<(Point2 A,Point2 B)> Segments(DrawingEntity entity)
    {
        var points = entity switch
        {
            LineEntity line => new[] { line.Start,line.End },
            PolylineEntity poly => poly.Vertices.ToArray(),
            PolygonEntity polygon => polygon.GetVertices().Append(polygon.GetVertices()[0]).ToArray(),
            _ => []
        };
        for(var i=1;i<points.Length;i++) yield return (points[i-1],points[i]);
    }

    internal static bool SameGeometry(DrawingEntity a, DrawingEntity b)
    {
        if (a.Kind != b.Kind) return false;
        if (Round(a,out var ac,out var ar) && Round(b,out var bc,out var br))
            return Distance(ac,bc) <= Epsilon && Math.Abs(ar-br) <= Epsilon
                && (a is not ArcEntity aa || b is ArcEntity ba && Math.Abs(aa.StartAngleDegrees-ba.StartAngleDegrees) <= Epsilon && Math.Abs(aa.EndAngleDegrees-ba.EndAngleDegrees) <= Epsilon);
        var first=Segments(a).ToArray(); var second=Segments(b).ToArray();
        return first.Length > 0 && first.Length == second.Length && first.All(s => second.Any(t =>
            Distance(s.A,t.A)<=Epsilon && Distance(s.B,t.B)<=Epsilon || Distance(s.A,t.B)<=Epsilon && Distance(s.B,t.A)<=Epsilon));
    }

    private static bool Round(DrawingEntity entity,out Point2 center,out double radius)
    {
        (center,radius)=entity switch { CircleEntity c => (c.Center,c.Radius), ArcEntity a => (a.Center,a.Radius), _ => (default,0d) };
        return entity is CircleEntity or ArcEntity;
    }
    private static double Distance(Point2 a,Point2 b) => Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Y-b.Y)*(a.Y-b.Y));
}

using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Operations;

namespace DXFER.Core.Sketching;

/// <summary>Stages an endpoint-linked slot. The caller solves and commits the complete candidate atomically.</summary>
public static class LineSlotCreation
{
    public static DrawingDocument Create(DrawingDocument document, string sourceKey, double width, bool keyedWidth,
        Func<string, EntityId> createEntityId, Func<SketchConstraintKind, string> createConstraintId,
        Func<string> createDimensionId)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(createEntityId);
        ArgumentNullException.ThrowIfNull(createConstraintId);
        ArgumentNullException.ThrowIfNull(createDimensionId);
        if (document.Metadata.Mode != DrawingDocumentMode.Editable)
            throw new InvalidOperationException("Reference-only documents cannot create slots.");
        if (!double.IsFinite(width) || width <= 0 || width / 2 == 0)
            throw new InvalidOperationException("Slot width must be a finite positive number.");
        if (document.Entities.Select(e => e.Id.Value).Distinct(StringComparer.Ordinal).Count() != document.Entities.Count)
            throw new InvalidOperationException("Duplicate entity IDs cannot be used to create a slot.");
        if (!SketchReference.TryParse(sourceKey, out var reference) || !reference.IsEntity || reference.SegmentIndex.HasValue
            || document.Entities.FirstOrDefault(e => e.Id.Value == reference.EntityId) is not LineEntity source)
            throw new InvalidOperationException("Select an existing whole line for the slot centerline.");
        if (!DrawingLayerService.CanEdit(document, source.Id.Value))
            throw new InvalidOperationException("Show and unlock the source line before creating a slot.");
        if (!DrawingPatternService.TryValidate(document, out var patternReason))
            throw new InvalidOperationException(patternReason);
        if (DrawingPatternService.IsGeneratedMember(document, source.Id.Value))
            throw new InvalidOperationException("Generated pattern members cannot become slot centerlines. Unlink the pattern first.");
        if (!source.IsConstruction && document.Metadata.Patterns.Any(p => p.SourceEntityIds.Contains(source.Id.Value)))
            throw new InvalidOperationException("Unlink the source pattern before changing its seed to a slot centerline.");
        if (!Finite(source.Start) || !Finite(source.End))
            throw new InvalidOperationException("The slot centerline must have finite endpoints.");
        var dx = source.End.X - source.Start.X;
        var dy = source.End.Y - source.Start.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (!double.IsFinite(length) || length <= 1e-7)
            throw new InvalidOperationException("The slot centerline must have a nonzero finite length.");
        var radius = width / 2;
        var normal = new Point2(-dy / length, dx / length);
        Point2 Offset(Point2 point, double sign) => new(point.X + sign * normal.X * radius, point.Y + sign * normal.Y * radius);
        var startLeft = Offset(source.Start, 1);
        var endLeft = Offset(source.End, 1);
        var startRight = Offset(source.Start, -1);
        var endRight = Offset(source.End, -1);
        if (!Finite(startLeft) || !Finite(endLeft) || !Finite(startRight) || !Finite(endRight)
            || startLeft == startRight || endLeft == endRight || startLeft == endLeft || startRight == endRight)
            throw new InvalidOperationException("Slot width cannot be represented at these coordinates.");

        var entityIds = document.Entities.Select(e => e.Id.Value).ToHashSet(StringComparer.Ordinal);
        EntityId EntityIdForSlot()
        {
            var id = createEntityId("slot");
            if (string.IsNullOrWhiteSpace(id.Value) || id.Value == SketchReference.OriginEntityId || !entityIds.Add(id.Value))
                throw new InvalidOperationException("Slot creation requires unique entity IDs.");
            return id;
        }
        var angle = Math.Atan2(dy, dx) * 180 / Math.PI;
        // Same oriented boundary as CreateSlot: left side, end cap, right side, start cap.
        var left = new LineEntity(EntityIdForSlot(), startLeft, endLeft);
        var endArc = new ArcEntity(EntityIdForSlot(), source.End, radius, angle - 90, angle + 90);
        var right = new LineEntity(EntityIdForSlot(), endRight, startRight);
        var startArc = new ArcEntity(EntityIdForSlot(), source.Start, radius, angle + 90, angle + 270);
        DrawingEntity[] added = [left, endArc, right, startArc];

        var constraintIds = document.Constraints.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        var constraints = new List<SketchConstraint>();
        void Add(SketchConstraintKind kind, params string[] keys)
        {
            var id = createConstraintId(kind);
            if (string.IsNullOrWhiteSpace(id) || !constraintIds.Add(id))
                throw new InvalidOperationException("Slot creation requires unique constraint IDs.");
            constraints.Add(new SketchConstraint(id, kind, keys, SketchConstraintState.Satisfied));
        }
        Add(SketchConstraintKind.Coincident, startArc.Id.Value + ":center", source.Id.Value + ":start");
        Add(SketchConstraintKind.Coincident, endArc.Id.Value + ":center", source.Id.Value + ":end");
        Add(SketchConstraintKind.Equal, startArc.Id.Value, endArc.Id.Value);
        Add(SketchConstraintKind.Coincident, left.Id.Value + ":end", endArc.Id.Value + ":end");
        Add(SketchConstraintKind.Coincident, right.Id.Value + ":start", endArc.Id.Value + ":start");
        Add(SketchConstraintKind.Coincident, right.Id.Value + ":end", startArc.Id.Value + ":end");
        Add(SketchConstraintKind.Coincident, left.Id.Value + ":start", startArc.Id.Value + ":start");
        // Equal radii, center anchors and four joined tangencies already imply parallel sides.
        Add(SketchConstraintKind.Tangent, left.Id.Value, endArc.Id.Value);
        Add(SketchConstraintKind.Tangent, right.Id.Value, endArc.Id.Value);
        Add(SketchConstraintKind.Tangent, left.Id.Value, startArc.Id.Value);
        Add(SketchConstraintKind.Tangent, right.Id.Value, startArc.Id.Value);

        var dimensions = document.Dimensions.ToList();
        if (keyedWidth)
        {
            var id = createDimensionId();
            if (string.IsNullOrWhiteSpace(id) || dimensions.Any(d => d.Id == id))
                throw new InvalidOperationException("Slot creation requires a unique width dimension ID.");
            // Two whole-line references would add a redundant Parallel equation in PlaneGCS.
            dimensions.Add(new SketchDimension(id, SketchDimensionKind.PointToLineDistance,
                [left.Id.Value + ":start", right.Id.Value], width, endLeft, isDriving: true));
        }
        var style = document.Metadata.EntityStyles.GetValueOrDefault(source.Id.Value, new DxfEntityStyle("0"));
        var styles = new Dictionary<string, DxfEntityStyle>(document.Metadata.EntityStyles, StringComparer.Ordinal);
        foreach (var entity in added) styles[entity.Id.Value] = style;
        return new DrawingDocument(document.Entities.Select(e => e.Id == source.Id ? source with { IsConstruction = true } : e).Concat(added),
            dimensions, document.Constraints.Concat(constraints), document.Metadata with { EntityStyles = styles });
    }

    private static bool Finite(Point2 point) => double.IsFinite(point.X) && double.IsFinite(point.Y);
}

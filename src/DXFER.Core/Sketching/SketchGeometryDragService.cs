using System.Globalization;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;

namespace DXFER.Core.Sketching;

public static class SketchGeometryDragService
{
    private const string PointKeySeparator = "|point|";
    private const string SegmentKeySeparator = "|segment|";

    public static bool TryApplyDrag(
        DrawingDocument document,
        string selectionKey,
        Point2 dragStart,
        Point2 dragEnd,
        bool constrainToCurrentVector,
        out DrawingDocument nextDocument,
        out string status)
    {
        ArgumentNullException.ThrowIfNull(document);

        nextDocument = document;
        status = "Select unconstrained geometry to drag.";
        if (string.IsNullOrWhiteSpace(selectionKey))
        {
            return false;
        }

        var delta = new Point2(dragEnd.X - dragStart.X, dragEnd.Y - dragStart.Y);
        if (SketchGeometryEditor.Distance(dragStart, dragEnd) <= SketchGeometryEditor.Tolerance)
        {
            status = "Drag distance was too small.";
            return false;
        }

        var entities = document.Entities.ToArray();
        var fixedReferences = SketchFixedReferences.FromConstraints(document.Constraints);
        if (!TryApplyGeometryDrag(entities, fixedReferences, selectionKey, delta, dragEnd, constrainToCurrentVector, out status))
        {
            return false;
        }

        var dimensions = TryGetTranslatedDimensionEntityId(document, selectionKey, out var translatedEntityId)
            ? TranslateDimensionAnchors(document.Dimensions, translatedEntityId, delta)
            : document.Dimensions;
        var draggedDocument = new DrawingDocument(entities, dimensions, document.Constraints);
        draggedDocument = SketchConstraintService.ApplyConstraints(draggedDocument, document.Constraints);
        draggedDocument = SketchDimensionSolverService.ApplyDimensions(draggedDocument, draggedDocument.Dimensions);
        if (GeometryMatches(document.Entities, draggedDocument.Entities))
        {
            status = "Selected geometry is constrained.";
            return false;
        }

        nextDocument = draggedDocument;
        return true;
    }

    private static bool TryApplyGeometryDrag(
        DrawingEntity[] entities,
        SketchFixedReferences fixedReferences,
        string selectionKey,
        Point2 delta,
        Point2 dragEnd,
        bool constrainToCurrentVector,
        out string status)
    {
        if (TryParseSegmentSelectionKey(selectionKey, out var segmentEntityId, out var segmentIndex))
        {
            return TryTranslatePolylineSegment(
                entities,
                fixedReferences,
                segmentEntityId,
                segmentIndex,
                delta,
                out status);
        }

        if (SketchReference.TryParseCanvasPointCoordinates(selectionKey, out var pointEntityId, out var label, out var targetPoint)
            && SketchGeometryEditor.TryFindEntity(entities, pointEntityId, out var pointEntityIndex, out var pointEntity))
        {
            return TryApplyPointDrag(
                entities,
                fixedReferences,
                pointEntityIndex,
                pointEntity,
                NormalizePointLabel(label),
                targetPoint,
                delta,
                dragEnd,
                constrainToCurrentVector,
                out status);
        }

        if (SketchGeometryEditor.TryFindEntity(entities, selectionKey, out var entityIndex, out var entity))
        {
            return TryApplyEntityDrag(entities, fixedReferences, entityIndex, entity, delta, dragEnd, out status);
        }

        status = "Selected geometry no longer exists.";
        return false;
    }

    private static bool TryApplyPointDrag(
        DrawingEntity[] entities,
        SketchFixedReferences fixedReferences,
        int entityIndex,
        DrawingEntity entity,
        string label,
        Point2 targetPoint,
        Point2 delta,
        Point2 dragEnd,
        bool constrainToCurrentVector,
        out string status)
    {
        switch (entity)
        {
            case LineEntity line:
                return TryApplyLinePointDrag(
                    entities,
                    fixedReferences,
                    entityIndex,
                    line,
                    label,
                    targetPoint,
                    delta,
                    dragEnd,
                    constrainToCurrentVector,
                    out status);
            case PolylineEntity polyline:
                return TryApplyPolylinePointDrag(entities, fixedReferences, entityIndex, polyline, label, delta, out status);
            case CircleEntity circle:
                return TryApplyCirclePointDrag(
                    entities,
                    fixedReferences,
                    entityIndex,
                    circle,
                    label,
                    dragEnd,
                    delta,
                    constrainToCurrentVector,
                    out status);
            case ArcEntity arc:
                return TryApplyArcPointDrag(
                    entities,
                    fixedReferences,
                    entityIndex,
                    arc,
                    label,
                    dragEnd,
                    delta,
                    constrainToCurrentVector,
                    out status);
            case PointEntity pointEntity:
                return TrySetPointEntityLocation(entities, fixedReferences, entityIndex, pointEntity, Add(pointEntity.Location, delta), out status);
            default:
                status = "Selected geometry cannot be dragged yet.";
                return false;
        }
    }

    private static bool TryApplyLinePointDrag(
        DrawingEntity[] entities,
        SketchFixedReferences fixedReferences,
        int entityIndex,
        LineEntity line,
        string label,
        Point2 targetPoint,
        Point2 delta,
        Point2 dragEnd,
        bool constrainToCurrentVector,
        out string status)
    {
        var lineReference = new SketchReference(line.Id.Value, SketchReferenceTarget.Entity);
        if (label == "start")
        {
            if (!fixedReferences.CanChangeLineEndpoint(lineReference, SketchReferenceTarget.Start))
            {
                status = "Line start point is constrained.";
                return false;
            }

            entities[entityIndex] = line with
            {
                Start = constrainToCurrentVector
                    ? ProjectPointToLine(dragEnd, line.Start, line.End)
                    : Add(targetPoint, delta)
            };
            status = "Moved line endpoint.";
            return true;
        }

        if (label == "end")
        {
            if (!fixedReferences.CanChangeLineEndpoint(lineReference, SketchReferenceTarget.End))
            {
                status = "Line end point is constrained.";
                return false;
            }

            entities[entityIndex] = line with
            {
                End = constrainToCurrentVector
                    ? ProjectPointToLine(dragEnd, line.Start, line.End)
                    : Add(targetPoint, delta)
            };
            status = "Moved line endpoint.";
            return true;
        }

        if (label == "mid")
        {
            return TryTranslateLine(entities, fixedReferences, entityIndex, line, delta, out status);
        }

        status = "Drag the line endpoint, midpoint, or edge.";
        return false;
    }

    private static bool TryApplyPolylinePointDrag(
        DrawingEntity[] entities,
        SketchFixedReferences fixedReferences,
        int entityIndex,
        PolylineEntity polyline,
        string label,
        Point2 delta,
        out string status)
    {
        if (TryParseIndexedLabel(label, "vertex-", out var vertexIndex))
        {
            if (vertexIndex < 0 || vertexIndex >= polyline.Vertices.Count)
            {
                status = "Selected polyline vertex no longer exists.";
                return false;
            }

            return TryMovePolylineVertex(
                entities,
                fixedReferences,
                entityIndex,
                polyline,
                vertexIndex,
                Add(polyline.Vertices[vertexIndex], delta),
                out status);
        }

        if (TryParseIndexedLabel(label, "mid-", out var segmentIndex))
        {
            return TryTranslatePolylineSegment(
                entities,
                fixedReferences,
                polyline.Id.Value,
                segmentIndex,
                delta,
                out status);
        }

        status = "Drag a polyline vertex, segment midpoint, or segment.";
        return false;
    }

    private static bool TryApplyCirclePointDrag(
        DrawingEntity[] entities,
        SketchFixedReferences fixedReferences,
        int entityIndex,
        CircleEntity circle,
        string label,
        Point2 dragEnd,
        Point2 delta,
        bool constrainToCurrentVector,
        out string status)
    {
        var reference = new SketchReference(circle.Id.Value, SketchReferenceTarget.Entity);
        if (label == "center")
        {
            if (!fixedReferences.CanMoveCircleLikeCenter(reference))
            {
                status = "Circle center is constrained.";
                return false;
            }

            entities[entityIndex] = circle with { Center = Add(circle.Center, delta) };
            status = "Moved circle center.";
            return true;
        }

        if (!fixedReferences.CanChangeCircleLikeRadius(reference))
        {
            status = "Circle radius is constrained.";
            return false;
        }

        var radius = SketchGeometryEditor.Distance(circle.Center, dragEnd);
        if (radius <= SketchGeometryEditor.Tolerance)
        {
            status = "Circle radius must stay positive.";
            return false;
        }

        entities[entityIndex] = circle with { Radius = radius };
        status = "Changed circle radius.";
        return true;
    }

    private static bool TryApplyArcPointDrag(
        DrawingEntity[] entities,
        SketchFixedReferences fixedReferences,
        int entityIndex,
        ArcEntity arc,
        string label,
        Point2 dragEnd,
        Point2 delta,
        bool constrainToCurrentVector,
        out string status)
    {
        var reference = new SketchReference(arc.Id.Value, SketchReferenceTarget.Entity);
        if (label == "center")
        {
            if (!fixedReferences.CanMoveCircleLikeCenter(reference))
            {
                status = "Arc center is constrained.";
                return false;
            }

            entities[entityIndex] = arc with { Center = Add(arc.Center, delta) };
            status = "Moved arc center.";
            return true;
        }

        if (label is "start" or "end")
        {
            return TryApplyArcEndpointDrag(
                entities,
                fixedReferences,
                entityIndex,
                arc,
                label,
                dragEnd,
                constrainToCurrentVector,
                out status);
        }

        if (!fixedReferences.CanChangeCircleLikeRadius(reference))
        {
            status = "Arc radius is constrained.";
            return false;
        }

        var radius = SketchGeometryEditor.Distance(arc.Center, dragEnd);
        if (radius <= SketchGeometryEditor.Tolerance)
        {
            status = "Arc radius must stay positive.";
            return false;
        }

        entities[entityIndex] = arc with { Radius = radius };
        status = "Changed arc radius.";
        return true;
    }

    private static bool TryApplyArcEndpointDrag(
        DrawingEntity[] entities,
        SketchFixedReferences fixedReferences,
        int entityIndex,
        ArcEntity arc,
        string label,
        Point2 dragEnd,
        bool constrainToCurrentVector,
        out string status)
    {
        var reference = new SketchReference(arc.Id.Value, SketchReferenceTarget.Entity);
        if (!fixedReferences.CanChangeCircleLikeRadius(reference) && !constrainToCurrentVector)
        {
            status = "Arc radius is constrained.";
            return false;
        }

        var radius = constrainToCurrentVector
            ? arc.Radius
            : SketchGeometryEditor.Distance(arc.Center, dragEnd);
        if (radius <= SketchGeometryEditor.Tolerance)
        {
            status = "Arc radius must stay positive.";
            return false;
        }

        var angle = GetPointAngleDegrees(arc.Center, dragEnd);
        entities[entityIndex] = label == "start"
            ? arc with { Radius = radius, StartAngleDegrees = angle }
            : arc with { Radius = radius, EndAngleDegrees = angle };
        status = constrainToCurrentVector
            ? "Changed arc sweep."
            : "Changed arc endpoint.";
        return true;
    }

    private static bool TryApplyEntityDrag(
        DrawingEntity[] entities,
        SketchFixedReferences fixedReferences,
        int entityIndex,
        DrawingEntity entity,
        Point2 delta,
        Point2 dragEnd,
        out string status)
    {
        switch (entity)
        {
            case LineEntity line:
                return TryTranslateLine(entities, fixedReferences, entityIndex, line, delta, out status);
            case PolylineEntity polyline:
                return TryTranslatePolyline(entities, fixedReferences, entityIndex, polyline, delta, out status);
            case CircleEntity circle:
                return TryApplyCirclePointDrag(entities, fixedReferences, entityIndex, circle, "perimeter", dragEnd, delta, constrainToCurrentVector: false, out status);
            case ArcEntity arc:
                return TryApplyArcPointDrag(entities, fixedReferences, entityIndex, arc, "perimeter", dragEnd, delta, constrainToCurrentVector: false, out status);
            case PointEntity pointEntity:
                return TrySetPointEntityLocation(entities, fixedReferences, entityIndex, pointEntity, Add(pointEntity.Location, delta), out status);
            default:
                status = "Selected geometry cannot be dragged yet.";
                return false;
        }
    }

    private static bool TryTranslateLine(
        DrawingEntity[] entities,
        SketchFixedReferences fixedReferences,
        int entityIndex,
        LineEntity line,
        Point2 delta,
        out string status)
    {
        var reference = new SketchReference(line.Id.Value, SketchReferenceTarget.Entity);
        if (!fixedReferences.CanMoveWholeLine(reference))
        {
            status = "Line is constrained.";
            return false;
        }

        entities[entityIndex] = line with
        {
            Start = Add(line.Start, delta),
            End = Add(line.End, delta)
        };
        status = "Moved line.";
        return true;
    }

    private static bool TryTranslatePolyline(
        DrawingEntity[] entities,
        SketchFixedReferences fixedReferences,
        int entityIndex,
        PolylineEntity polyline,
        Point2 delta,
        out string status)
    {
        var reference = new SketchReference(polyline.Id.Value, SketchReferenceTarget.Entity);
        if (fixedReferences.IsWholeEntityFixed(reference))
        {
            status = "Polyline is constrained.";
            return false;
        }

        var vertices = polyline.Vertices.Select(vertex => Add(vertex, delta)).ToArray();
        entities[entityIndex] = new PolylineEntity(polyline.Id, vertices, polyline.IsConstruction);
        status = "Moved polyline.";
        return true;
    }

    private static bool TryTranslatePolylineSegment(
        DrawingEntity[] entities,
        SketchFixedReferences fixedReferences,
        string entityId,
        int segmentIndex,
        Point2 delta,
        out string status)
    {
        status = "Selected polyline segment no longer exists.";
        if (!SketchGeometryEditor.TryFindEntity(entities, entityId, out var entityIndex, out var entity)
            || entity is not PolylineEntity polyline
            || segmentIndex < 0
            || segmentIndex >= polyline.Vertices.Count - 1)
        {
            return false;
        }

        var reference = new SketchReference(entityId, SketchReferenceTarget.Entity, segmentIndex);
        if (!fixedReferences.CanMoveWholeLine(reference))
        {
            status = "Polyline segment is constrained.";
            return false;
        }

        var vertices = polyline.Vertices.ToArray();
        vertices[segmentIndex] = Add(vertices[segmentIndex], delta);
        vertices[segmentIndex + 1] = Add(vertices[segmentIndex + 1], delta);
        entities[entityIndex] = new PolylineEntity(polyline.Id, vertices, polyline.IsConstruction);
        status = "Moved polyline segment.";
        return true;
    }

    private static bool TryMovePolylineVertex(
        DrawingEntity[] entities,
        SketchFixedReferences fixedReferences,
        int entityIndex,
        PolylineEntity polyline,
        int vertexIndex,
        Point2 point,
        out string status)
    {
        if (vertexIndex < 0 || vertexIndex >= polyline.Vertices.Count)
        {
            status = "Selected polyline vertex no longer exists.";
            return false;
        }

        if (!CanMovePolylineVertex(fixedReferences, polyline.Id.Value, vertexIndex, polyline.Vertices.Count))
        {
            status = "Polyline vertex is constrained.";
            return false;
        }

        var vertices = polyline.Vertices.ToArray();
        vertices[vertexIndex] = point;
        entities[entityIndex] = new PolylineEntity(polyline.Id, vertices, polyline.IsConstruction);
        status = "Moved polyline vertex.";
        return true;
    }

    private static bool TrySetPointEntityLocation(
        DrawingEntity[] entities,
        SketchFixedReferences fixedReferences,
        int entityIndex,
        PointEntity pointEntity,
        Point2 point,
        out string status)
    {
        var reference = new SketchReference(pointEntity.Id.Value, SketchReferenceTarget.Entity);
        if (!fixedReferences.CanMovePoint(reference))
        {
            status = "Point is constrained.";
            return false;
        }

        entities[entityIndex] = pointEntity with { Location = point };
        status = "Moved point.";
        return true;
    }

    private static bool CanMovePolylineVertex(
        SketchFixedReferences fixedReferences,
        string entityId,
        int vertexIndex,
        int vertexCount)
    {
        var entityReference = new SketchReference(entityId, SketchReferenceTarget.Entity);
        if (fixedReferences.IsWholeEntityFixed(entityReference))
        {
            return false;
        }

        if (vertexIndex > 0
            && !fixedReferences.CanChangeLineEndpoint(
                new SketchReference(entityId, SketchReferenceTarget.Entity, vertexIndex - 1),
                SketchReferenceTarget.End))
        {
            return false;
        }

        return vertexIndex >= vertexCount - 1
            || fixedReferences.CanChangeLineEndpoint(
                new SketchReference(entityId, SketchReferenceTarget.Entity, vertexIndex),
                SketchReferenceTarget.Start);
    }

    private static bool TryParseSegmentSelectionKey(string selectionKey, out string entityId, out int segmentIndex)
    {
        var separatorIndex = selectionKey.IndexOf(SegmentKeySeparator, StringComparison.Ordinal);
        if (separatorIndex < 0)
        {
            entityId = string.Empty;
            segmentIndex = default;
            return false;
        }

        entityId = selectionKey[..separatorIndex];
        return int.TryParse(
            selectionKey[(separatorIndex + SegmentKeySeparator.Length)..],
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out segmentIndex);
    }

    private static bool TryGetTranslatedDimensionEntityId(
        DrawingDocument document,
        string selectionKey,
        out string entityId)
    {
        if (TryParseSegmentSelectionKey(selectionKey, out entityId, out _))
        {
            return true;
        }

        if (SketchReference.TryParseCanvasPointCoordinates(selectionKey, out var pointEntityId, out var label, out _)
            && document.Entities.FirstOrDefault(entity => StringComparer.Ordinal.Equals(entity.Id.Value, pointEntityId)) is { } entity)
        {
            var normalizedLabel = NormalizePointLabel(label);
            var translated = entity switch
            {
                LineEntity => normalizedLabel == "mid",
                PolylineEntity => normalizedLabel.StartsWith("mid-", StringComparison.Ordinal),
                CircleEntity or ArcEntity => normalizedLabel == "center",
                PointEntity => normalizedLabel == "point",
                _ => false
            };
            entityId = translated ? pointEntityId : string.Empty;
            return translated;
        }

        var wholeEntity = document.Entities.FirstOrDefault(entity => StringComparer.Ordinal.Equals(entity.Id.Value, selectionKey));
        var isWholeEntityTranslation = wholeEntity is LineEntity or PolylineEntity or PointEntity;
        entityId = isWholeEntityTranslation ? selectionKey : string.Empty;
        return isWholeEntityTranslation;
    }

    private static IReadOnlyList<SketchDimension> TranslateDimensionAnchors(
        IReadOnlyList<SketchDimension> dimensions,
        string entityId,
        Point2 delta)
    {
        return dimensions
            .Select(dimension => ShouldTranslateDimensionAnchor(dimension, entityId)
                ? new SketchDimension(
                    dimension.Id,
                    dimension.Kind,
                    dimension.ReferenceKeys,
                    dimension.Value,
                    dimension.Anchor.HasValue ? Add(dimension.Anchor.Value, delta) : null,
                    dimension.IsDriving)
                : dimension)
            .ToArray();
    }

    private static bool ShouldTranslateDimensionAnchor(SketchDimension dimension, string entityId)
    {
        return dimension.Anchor.HasValue
            && dimension.ReferenceKeys.Count > 0
            && dimension.ReferenceKeys
                .Select(GetReferenceEntityId)
                .All(referenceEntityId => StringComparer.Ordinal.Equals(referenceEntityId, entityId));
    }

    private static string GetReferenceEntityId(string referenceKey)
    {
        if (SketchReference.TryParseCanvasPointCoordinates(referenceKey, out var canvasEntityId, out _, out _))
        {
            return canvasEntityId;
        }

        return SketchReference.TryParse(referenceKey, out var reference)
            ? reference.EntityId
            : string.Empty;
    }

    private static bool TryParseIndexedLabel(string label, string prefix, out int index)
    {
        if (label.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(label[prefix.Length..], NumberStyles.Integer, CultureInfo.InvariantCulture, out index))
        {
            return true;
        }

        index = default;
        return false;
    }

    private static string NormalizePointLabel(string label)
    {
        var trimmed = label.Trim();
        var separatorIndex = trimmed.IndexOf('|');
        return separatorIndex >= 0 ? trimmed[..separatorIndex] : trimmed;
    }

    private static Point2 Add(Point2 point, Point2 delta) =>
        new(point.X + delta.X, point.Y + delta.Y);

    private static Point2 ProjectPointToLine(Point2 point, Point2 start, Point2 end)
    {
        var deltaX = end.X - start.X;
        var deltaY = end.Y - start.Y;
        var lengthSquared = (deltaX * deltaX) + (deltaY * deltaY);
        if (lengthSquared <= SketchGeometryEditor.Tolerance * SketchGeometryEditor.Tolerance)
        {
            return start;
        }

        var scalar = (((point.X - start.X) * deltaX) + ((point.Y - start.Y) * deltaY)) / lengthSquared;
        return new Point2(start.X + (deltaX * scalar), start.Y + (deltaY * scalar));
    }

    private static double GetPointAngleDegrees(Point2 center, Point2 point) =>
        Math.Atan2(point.Y - center.Y, point.X - center.X) * 180.0 / Math.PI;

    private static bool GeometryMatches(
        IReadOnlyList<DrawingEntity> first,
        IReadOnlyList<DrawingEntity> second)
    {
        if (first.Count != second.Count)
        {
            return false;
        }

        for (var index = 0; index < first.Count; index++)
        {
            if (!EntityMatches(first[index], second[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool EntityMatches(DrawingEntity first, DrawingEntity second)
    {
        if (!StringComparer.Ordinal.Equals(first.Id.Value, second.Id.Value)
            || !StringComparer.Ordinal.Equals(first.Kind, second.Kind))
        {
            return false;
        }

        return (first, second) switch
        {
            (LineEntity a, LineEntity b) => Close(a.Start, b.Start) && Close(a.End, b.End),
            (PolylineEntity a, PolylineEntity b) => a.Vertices.Count == b.Vertices.Count
                && a.Vertices.Zip(b.Vertices).All(pair => Close(pair.First, pair.Second)),
            (CircleEntity a, CircleEntity b) => Close(a.Center, b.Center) && SketchGeometryEditor.AreClose(a.Radius, b.Radius),
            (ArcEntity a, ArcEntity b) => Close(a.Center, b.Center)
                && SketchGeometryEditor.AreClose(a.Radius, b.Radius)
                && SketchGeometryEditor.AreClose(a.StartAngleDegrees, b.StartAngleDegrees)
                && SketchGeometryEditor.AreClose(a.EndAngleDegrees, b.EndAngleDegrees),
            (PointEntity a, PointEntity b) => Close(a.Location, b.Location),
            _ => first.Equals(second)
        };
    }

    private static bool Close(Point2 first, Point2 second) =>
        SketchGeometryEditor.AreClose(first, second);
}

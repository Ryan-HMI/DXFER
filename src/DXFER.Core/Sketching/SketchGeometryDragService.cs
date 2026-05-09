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

        var originalEntities = document.Entities.ToArray();
        var entities = document.Entities.ToArray();
        var fixedReferences = SketchFixedReferences.FromConstraints(document.Constraints);
        if (TryApplyDimensionedRectangleTranslation(document, selectionKey, delta, out nextDocument, out status))
        {
            return true;
        }

        if (TryApplyPartiallyDimensionedRectangleDrag(document, selectionKey, delta, out nextDocument, out status))
        {
            return true;
        }

        if (TryApplyUndimensionedRectangleEdgeResize(document, selectionKey, delta, out var rectangleResizeHandled, out nextDocument, out status))
        {
            return true;
        }

        if (rectangleResizeHandled)
        {
            return false;
        }

        if (!TryApplyGeometryDrag(entities, fixedReferences, selectionKey, delta, dragEnd, constrainToCurrentVector, out status))
        {
            return false;
        }

        SketchConstraintPropagationService.PropagateFromChanges(originalEntities, entities, document.Constraints, fixedReferences);
        if (!DrivingDimensionsRemainSatisfied(entities, document.Dimensions))
        {
            if (TryApplyDimensionPreservingSelectionTranslation(document, selectionKey, delta, out nextDocument, out status))
            {
                return true;
            }

            status = "Selected geometry is constrained by a driving dimension.";
            return false;
        }

        var dimensions = TryGetTranslatedDimensionEntityId(document, selectionKey, out var translatedEntityId)
            ? TranslateDimensionAnchors(document.Dimensions, translatedEntityId, delta)
            : document.Dimensions;
        var draggedDocument = new DrawingDocument(entities, dimensions, document.Constraints, document.Metadata);
        draggedDocument = new DrawingDocument(
            draggedDocument.Entities,
            draggedDocument.Dimensions,
            SketchConstraintPropagationService.ValidateConstraints(draggedDocument, document.Constraints),
            document.Metadata);
        if (GeometryMatches(document.Entities, draggedDocument.Entities))
        {
            status = "Selected geometry is constrained.";
            return false;
        }

        draggedDocument = AddCoincidentConstraintForSnappedPointDrag(draggedDocument, selectionKey, ref status);
        nextDocument = draggedDocument;
        return true;
    }

    private static bool TryApplyDimensionPreservingSelectionTranslation(
        DrawingDocument document,
        string selectionKey,
        Point2 delta,
        out DrawingDocument nextDocument,
        out string status)
    {
        nextDocument = document;
        status = string.Empty;
        if (!TryGetSelectedEntityId(document, selectionKey, out var entityId))
        {
            return false;
        }

        var entities = document.Entities.ToArray();
        var fixedReferences = SketchFixedReferences.FromConstraints(document.Constraints);
        if (!TryTranslateEntityById(entities, fixedReferences, entityId, delta, out status))
        {
            return false;
        }

        var dimensions = TranslateDimensionAnchors(document.Dimensions, entityId, delta);
        var candidate = BuildValidatedDragDocument(document, entities, dimensions);
        if (!DrivingDimensionsRemainSatisfied(candidate.Entities, candidate.Dimensions)
            || candidate.Constraints.Any(constraint => constraint.State == SketchConstraintState.Unsatisfied)
            || GeometryMatches(document.Entities, candidate.Entities))
        {
            status = "Selected geometry is constrained by a driving dimension.";
            return false;
        }

        nextDocument = candidate;
        status = "Moved dimensioned geometry.";
        return true;
    }

    private static bool TryApplyDimensionedRectangleTranslation(
        DrawingDocument document,
        string selectionKey,
        Point2 delta,
        out DrawingDocument nextDocument,
        out string status)
    {
        nextDocument = document;
        status = string.Empty;
        if (!TryGetSelectedLineEntityId(document, selectionKey, out var selectedEntityId)
            || !TryGetDimensionedRectangleGroup(document, selectedEntityId, out var rectangleEntityIds))
        {
            return false;
        }

        var fixedReferences = SketchFixedReferences.FromConstraints(document.Constraints);
        if (rectangleEntityIds.Any(entityId => !fixedReferences.CanMoveWholeLine(new SketchReference(entityId, SketchReferenceTarget.Entity))))
        {
            status = "Rectangle is constrained.";
            return false;
        }

        var entities = document.Entities.ToArray();
        for (var index = 0; index < entities.Length; index++)
        {
            if (entities[index] is LineEntity line
                && rectangleEntityIds.Contains(line.Id.Value))
            {
                entities[index] = line with
                {
                    Start = Add(line.Start, delta),
                    End = Add(line.End, delta)
                };
            }
        }

        var dimensions = TranslateDimensionAnchors(document.Dimensions, rectangleEntityIds, delta);
        var translatedDocument = new DrawingDocument(entities, dimensions, document.Constraints, document.Metadata);
        translatedDocument = new DrawingDocument(
            translatedDocument.Entities,
            translatedDocument.Dimensions,
            SketchConstraintPropagationService.ValidateConstraints(translatedDocument, document.Constraints),
            document.Metadata);
        if (!DrivingDimensionsRemainSatisfied(translatedDocument.Entities, translatedDocument.Dimensions)
            || GeometryMatches(document.Entities, translatedDocument.Entities))
        {
            status = "Selected geometry is constrained by a driving dimension.";
            return false;
        }

        nextDocument = translatedDocument;
        status = "Moved dimensioned rectangle.";
        return true;
    }

    private static bool TryApplyPartiallyDimensionedRectangleDrag(
        DrawingDocument document,
        string selectionKey,
        Point2 delta,
        out DrawingDocument nextDocument,
        out string status)
    {
        nextDocument = document;
        status = string.Empty;
        if (!TryGetSelectedLineEntityId(document, selectionKey, out var selectedEntityId)
            || !TryGetRectangleGroup(document, selectedEntityId, out var rectangleEntityIds))
        {
            return false;
        }

        var drivingDimensionCount = CountDrivingDimensionsForEntityGroup(document.Dimensions, rectangleEntityIds);
        if (drivingDimensionCount <= 0 || drivingDimensionCount >= 2)
        {
            return false;
        }

        var fixedReferences = SketchFixedReferences.FromConstraints(document.Constraints);
        if (rectangleEntityIds.Any(entityId => !fixedReferences.CanMoveWholeLine(new SketchReference(entityId, SketchReferenceTarget.Entity))))
        {
            status = "Rectangle is constrained.";
            return false;
        }

        if (!TryGetSelectedRectangleEdgeEntityId(document, selectionKey, out _))
        {
            if (TryGetSelectedRectangleCorner(document, selectionKey, out var cornerPoint))
            {
                return TryApplyPartiallyDimensionedRectangleCornerDrag(
                    document,
                    rectangleEntityIds,
                    cornerPoint,
                    delta,
                    out nextDocument,
                    out status);
            }

            return TryTranslateRectangleGroup(document, rectangleEntityIds, delta, "Moved partially dimensioned rectangle.", out nextDocument, out status);
        }

        var selectedLine = document.Entities
            .OfType<LineEntity>()
            .FirstOrDefault(line => StringComparer.Ordinal.Equals(line.Id.Value, selectedEntityId));
        if (selectedLine is null)
        {
            status = "Selected rectangle edge no longer exists.";
            return false;
        }

        var parallelDelta = ProjectDeltaOntoLine(delta, selectedLine.Start, selectedLine.End);
        var perpendicularDelta = new Point2(delta.X - parallelDelta.X, delta.Y - parallelDelta.Y);

        var movedEntities = document.Entities.ToArray();
        TranslateRectangleEntities(movedEntities, rectangleEntityIds, parallelDelta);
        var movedDimensions = TranslateDimensionAnchors(document.Dimensions, rectangleEntityIds, parallelDelta);

        if (SketchGeometryEditor.Distance(new Point2(0, 0), perpendicularDelta) > SketchGeometryEditor.Tolerance)
        {
            var translatedSelectedLine = selectedLine with
            {
                Start = Add(selectedLine.Start, parallelDelta),
                End = Add(selectedLine.End, parallelDelta)
            };
            var resizedEntities = movedEntities.ToArray();
            ResizeRectangleEdgeEntities(resizedEntities, rectangleEntityIds, translatedSelectedLine, perpendicularDelta);
            var resizedDimensions = TranslateDimensionAnchors(movedDimensions, selectedEntityId, perpendicularDelta);
            var resizedDocument = BuildValidatedDragDocument(document, resizedEntities, resizedDimensions);
            if (DrivingDimensionsRemainSatisfied(resizedDocument.Entities, resizedDocument.Dimensions)
                && resizedDocument.Constraints.All(constraint => constraint.State != SketchConstraintState.Unsatisfied))
            {
                nextDocument = resizedDocument;
                status = "Resized partially dimensioned rectangle.";
                return true;
            }

            TranslateRectangleEntities(movedEntities, rectangleEntityIds, perpendicularDelta);
            movedDimensions = TranslateDimensionAnchors(movedDimensions, rectangleEntityIds, perpendicularDelta);
        }

        var translatedDocument = BuildValidatedDragDocument(document, movedEntities, movedDimensions);
        if (!DrivingDimensionsRemainSatisfied(translatedDocument.Entities, translatedDocument.Dimensions)
            || translatedDocument.Constraints.Any(constraint => constraint.State == SketchConstraintState.Unsatisfied)
            || GeometryMatches(document.Entities, translatedDocument.Entities))
        {
            status = "Selected geometry is constrained by a driving dimension.";
            return false;
        }

        nextDocument = translatedDocument;
        status = "Moved partially dimensioned rectangle.";
        return true;
    }

    private static bool TryApplyPartiallyDimensionedRectangleCornerDrag(
        DrawingDocument document,
        IReadOnlySet<string> rectangleEntityIds,
        Point2 cornerPoint,
        Point2 delta,
        out DrawingDocument nextDocument,
        out string status)
    {
        nextDocument = document;
        status = string.Empty;
        if (!TryGetRectangleCornerAxes(document.Entities, rectangleEntityIds, cornerPoint, out var firstAxis, out var secondAxis))
        {
            return false;
        }

        var components = new[]
        {
            ProjectDeltaOntoAxis(delta, firstAxis),
            ProjectDeltaOntoAxis(delta, secondAxis)
        };
        var workingEntities = document.Entities.ToArray();
        var workingDimensions = document.Dimensions;
        var currentCorner = cornerPoint;
        foreach (var component in components)
        {
            if (SketchGeometryEditor.Distance(new Point2(0, 0), component) <= SketchGeometryEditor.Tolerance)
            {
                continue;
            }

            var resizedEntities = workingEntities.ToArray();
            if (TryResizeRectangleCornerEntities(
                resizedEntities,
                rectangleEntityIds,
                currentCorner,
                component,
                out var resizedWholeEntityIds))
            {
                var resizedDimensions = resizedWholeEntityIds.Count > 0
                    ? TranslateDimensionAnchors(workingDimensions, resizedWholeEntityIds, component)
                    : workingDimensions;
                var resizedDocument = BuildValidatedDragDocument(document, resizedEntities, resizedDimensions);
                if (DrivingDimensionsRemainSatisfied(resizedDocument.Entities, resizedDocument.Dimensions)
                    && resizedDocument.Constraints.All(constraint => constraint.State != SketchConstraintState.Unsatisfied))
                {
                    workingEntities = resizedDocument.Entities.ToArray();
                    workingDimensions = resizedDocument.Dimensions;
                    currentCorner = Add(currentCorner, component);
                    continue;
                }
            }

            TranslateRectangleEntities(workingEntities, rectangleEntityIds, component);
            workingDimensions = TranslateDimensionAnchors(workingDimensions, rectangleEntityIds, component);
            currentCorner = Add(currentCorner, component);
        }

        var next = BuildValidatedDragDocument(document, workingEntities, workingDimensions);
        if (!DrivingDimensionsRemainSatisfied(next.Entities, next.Dimensions)
            || next.Constraints.Any(constraint => constraint.State == SketchConstraintState.Unsatisfied)
            || GeometryMatches(document.Entities, next.Entities))
        {
            status = "Selected geometry is constrained by a driving dimension.";
            return false;
        }

        nextDocument = next;
        status = "Resized partially dimensioned rectangle.";
        return true;
    }

    private static bool TryApplyUndimensionedRectangleEdgeResize(
        DrawingDocument document,
        string selectionKey,
        Point2 delta,
        out bool handled,
        out DrawingDocument nextDocument,
        out string status)
    {
        handled = false;
        nextDocument = document;
        status = string.Empty;
        if (!TryGetSelectedRectangleEdgeEntityId(document, selectionKey, out var selectedEntityId)
            || !TryGetRectangleGroup(document, selectedEntityId, out var rectangleEntityIds)
            || CountDrivingDimensionsForEntityGroup(document.Dimensions, rectangleEntityIds) > 0)
        {
            return false;
        }

        handled = true;
        var fixedReferences = SketchFixedReferences.FromConstraints(document.Constraints);
        if (!fixedReferences.CanMoveWholeLine(new SketchReference(selectedEntityId, SketchReferenceTarget.Entity)))
        {
            status = "Rectangle edge is constrained.";
            return false;
        }

        var selectedLine = document.Entities
            .OfType<LineEntity>()
            .FirstOrDefault(line => StringComparer.Ordinal.Equals(line.Id.Value, selectedEntityId));
        if (selectedLine is null)
        {
            status = "Selected rectangle edge no longer exists.";
            return false;
        }

        var resizeDelta = ProjectDeltaPerpendicularToLine(delta, selectedLine.Start, selectedLine.End);
        if (SketchGeometryEditor.Distance(new Point2(0, 0), resizeDelta) <= SketchGeometryEditor.Tolerance)
        {
            status = "Rectangle edge drag was parallel to the edge.";
            return false;
        }

        var entities = document.Entities.ToArray();
        ResizeRectangleEdgeEntities(entities, rectangleEntityIds, selectedLine, resizeDelta);

        var resizedDocument = BuildValidatedDragDocument(document, entities, document.Dimensions);
        if (resizedDocument.Constraints.Any(constraint => constraint.State == SketchConstraintState.Unsatisfied))
        {
            status = "Rectangle edge drag would break constraints.";
            return false;
        }

        nextDocument = resizedDocument;
        status = "Resized rectangle edge.";
        return true;
    }

    private static bool TryGetSelectedRectangleEdgeEntityId(
        DrawingDocument document,
        string selectionKey,
        out string entityId)
    {
        entityId = string.Empty;
        if (TryParseSegmentSelectionKey(selectionKey, out _, out _))
        {
            return false;
        }

        if (SketchReference.TryParseCanvasPointCoordinates(selectionKey, out var pointEntityId, out var label, out _))
        {
            if (!StringComparer.Ordinal.Equals(NormalizePointLabel(label), "mid"))
            {
                return false;
            }

            entityId = pointEntityId;
        }
        else
        {
            entityId = selectionKey;
        }

        var selectedId = entityId;
        return document.Entities.Any(entity =>
            entity is LineEntity
            && StringComparer.Ordinal.Equals(entity.Id.Value, selectedId));
    }

    private static bool TryGetSelectedRectangleCorner(
        DrawingDocument document,
        string selectionKey,
        out Point2 cornerPoint)
    {
        cornerPoint = default;
        if (!SketchReference.TryParseCanvasPointCoordinates(selectionKey, out var entityId, out var label, out _))
        {
            return false;
        }

        var normalizedLabel = NormalizePointLabel(label);
        if (!StringComparer.Ordinal.Equals(normalizedLabel, "start")
            && !StringComparer.Ordinal.Equals(normalizedLabel, "end"))
        {
            return false;
        }

        var line = document.Entities
            .OfType<LineEntity>()
            .FirstOrDefault(entity => StringComparer.Ordinal.Equals(entity.Id.Value, entityId));
        if (line is null)
        {
            return false;
        }

        cornerPoint = StringComparer.Ordinal.Equals(normalizedLabel, "start")
            ? line.Start
            : line.End;
        return true;
    }

    private static bool TryTranslateRectangleGroup(
        DrawingDocument document,
        IReadOnlySet<string> rectangleEntityIds,
        Point2 delta,
        string successStatus,
        out DrawingDocument nextDocument,
        out string status)
    {
        var entities = document.Entities.ToArray();
        TranslateRectangleEntities(entities, rectangleEntityIds, delta);
        var dimensions = TranslateDimensionAnchors(document.Dimensions, rectangleEntityIds, delta);
        var translatedDocument = BuildValidatedDragDocument(document, entities, dimensions);
        if (!DrivingDimensionsRemainSatisfied(translatedDocument.Entities, translatedDocument.Dimensions)
            || translatedDocument.Constraints.Any(constraint => constraint.State == SketchConstraintState.Unsatisfied)
            || GeometryMatches(document.Entities, translatedDocument.Entities))
        {
            nextDocument = document;
            status = "Selected geometry is constrained by a driving dimension.";
            return false;
        }

        nextDocument = translatedDocument;
        status = successStatus;
        return true;
    }

    private static void TranslateRectangleEntities(
        DrawingEntity[] entities,
        IReadOnlySet<string> rectangleEntityIds,
        Point2 delta)
    {
        if (SketchGeometryEditor.Distance(new Point2(0, 0), delta) <= SketchGeometryEditor.Tolerance)
        {
            return;
        }

        for (var index = 0; index < entities.Length; index++)
        {
            if (entities[index] is LineEntity line
                && rectangleEntityIds.Contains(line.Id.Value))
            {
                entities[index] = line with
                {
                    Start = Add(line.Start, delta),
                    End = Add(line.End, delta)
                };
            }
        }
    }

    private static void ResizeRectangleEdgeEntities(
        DrawingEntity[] entities,
        IReadOnlySet<string> rectangleEntityIds,
        LineEntity selectedLine,
        Point2 resizeDelta)
    {
        for (var index = 0; index < entities.Length; index++)
        {
            if (entities[index] is not LineEntity line
                || !rectangleEntityIds.Contains(line.Id.Value))
            {
                continue;
            }

            var start = ShouldMoveRectangleEndpoint(line.Start, selectedLine)
                ? Add(line.Start, resizeDelta)
                : line.Start;
            var end = ShouldMoveRectangleEndpoint(line.End, selectedLine)
                ? Add(line.End, resizeDelta)
                : line.End;
            entities[index] = line with { Start = start, End = end };
        }
    }

    private static bool TryResizeRectangleCornerEntities(
        DrawingEntity[] entities,
        IReadOnlySet<string> rectangleEntityIds,
        Point2 cornerPoint,
        Point2 delta,
        out HashSet<string> resizedWholeEntityIds)
    {
        resizedWholeEntityIds = new HashSet<string>(StringComparer.Ordinal);
        if (!TryGetRectangleCornerGeometry(
            entities,
            rectangleEntityIds,
            cornerPoint,
            out var firstAdjacent,
            out var secondAdjacent,
            out var opposite,
            out var firstAxis,
            out var secondAxis))
        {
            return false;
        }

        var movedCorner = Add(cornerPoint, delta);
        var movedOffset = new Point2(movedCorner.X - opposite.X, movedCorner.Y - opposite.Y);
        var firstLength = Dot(movedOffset, firstAxis);
        var secondLength = Dot(movedOffset, secondAxis);
        var nextFirstAdjacent = new Point2(
            opposite.X + (firstAxis.X * firstLength),
            opposite.Y + (firstAxis.Y * firstLength));
        var nextSecondAdjacent = new Point2(
            opposite.X + (secondAxis.X * secondLength),
            opposite.Y + (secondAxis.Y * secondLength));
        var nextCorner = new Point2(
            nextFirstAdjacent.X + nextSecondAdjacent.X - opposite.X,
            nextFirstAdjacent.Y + nextSecondAdjacent.Y - opposite.Y);

        for (var index = 0; index < entities.Length; index++)
        {
            if (entities[index] is not LineEntity line
                || !rectangleEntityIds.Contains(line.Id.Value))
            {
                continue;
            }

            var start = GetNextRectangleCornerPoint(line.Start, cornerPoint, firstAdjacent, secondAdjacent, nextCorner, nextFirstAdjacent, nextSecondAdjacent);
            var end = GetNextRectangleCornerPoint(line.End, cornerPoint, firstAdjacent, secondAdjacent, nextCorner, nextFirstAdjacent, nextSecondAdjacent);
            if (SketchGeometryEditor.Distance(new Point2(start.X - line.Start.X, start.Y - line.Start.Y), delta) <= SketchGeometryEditor.Tolerance
                && SketchGeometryEditor.Distance(new Point2(end.X - line.End.X, end.Y - line.End.Y), delta) <= SketchGeometryEditor.Tolerance)
            {
                resizedWholeEntityIds.Add(line.Id.Value);
            }

            entities[index] = line with { Start = start, End = end };
        }

        return true;
    }

    private static Point2 GetNextRectangleCornerPoint(
        Point2 point,
        Point2 cornerPoint,
        Point2 firstAdjacent,
        Point2 secondAdjacent,
        Point2 nextCorner,
        Point2 nextFirstAdjacent,
        Point2 nextSecondAdjacent)
    {
        if (SketchGeometryEditor.AreClose(point, cornerPoint))
        {
            return nextCorner;
        }

        if (SketchGeometryEditor.AreClose(point, firstAdjacent))
        {
            return nextFirstAdjacent;
        }

        return SketchGeometryEditor.AreClose(point, secondAdjacent)
            ? nextSecondAdjacent
            : point;
    }

    private static bool TryGetRectangleCornerAxes(
        IReadOnlyList<DrawingEntity> entities,
        IReadOnlySet<string> rectangleEntityIds,
        Point2 cornerPoint,
        out Point2 firstAxis,
        out Point2 secondAxis)
    {
        firstAxis = default;
        secondAxis = default;
        return TryGetRectangleCornerGeometry(
            entities,
            rectangleEntityIds,
            cornerPoint,
            out _,
            out _,
            out _,
            out firstAxis,
            out secondAxis);
    }

    private static bool TryGetRectangleCornerGeometry(
        IReadOnlyList<DrawingEntity> entities,
        IReadOnlySet<string> rectangleEntityIds,
        Point2 cornerPoint,
        out Point2 firstAdjacent,
        out Point2 secondAdjacent,
        out Point2 opposite,
        out Point2 firstAxis,
        out Point2 secondAxis)
    {
        firstAdjacent = default;
        secondAdjacent = default;
        opposite = default;
        firstAxis = default;
        secondAxis = default;
        var adjacentPoints = GetRectangleAdjacentCornerPoints(entities, rectangleEntityIds, cornerPoint);
        if (adjacentPoints.Count != 2)
        {
            return false;
        }

        firstAdjacent = adjacentPoints[0];
        secondAdjacent = adjacentPoints[1];
        opposite = new Point2(
            firstAdjacent.X + secondAdjacent.X - cornerPoint.X,
            firstAdjacent.Y + secondAdjacent.Y - cornerPoint.Y);
        return TryNormalize(new Point2(firstAdjacent.X - opposite.X, firstAdjacent.Y - opposite.Y), out firstAxis)
            && TryNormalize(new Point2(secondAdjacent.X - opposite.X, secondAdjacent.Y - opposite.Y), out secondAxis);
    }

    private static List<Point2> GetRectangleAdjacentCornerPoints(
        IReadOnlyList<DrawingEntity> entities,
        IReadOnlySet<string> rectangleEntityIds,
        Point2 cornerPoint)
    {
        var points = new List<Point2>(capacity: 2);
        foreach (var line in entities.OfType<LineEntity>())
        {
            if (!rectangleEntityIds.Contains(line.Id.Value))
            {
                continue;
            }

            if (SketchGeometryEditor.AreClose(line.Start, cornerPoint))
            {
                AddUniquePoint(points, line.End);
            }
            else if (SketchGeometryEditor.AreClose(line.End, cornerPoint))
            {
                AddUniquePoint(points, line.Start);
            }
        }

        return points;
    }

    private static void AddUniquePoint(List<Point2> points, Point2 point)
    {
        if (!points.Any(existing => SketchGeometryEditor.AreClose(existing, point)))
        {
            points.Add(point);
        }
    }

    private static DrawingDocument BuildValidatedDragDocument(
        DrawingDocument document,
        IReadOnlyList<DrawingEntity> entities,
        IReadOnlyList<SketchDimension> dimensions)
    {
        var draggedDocument = new DrawingDocument(entities, dimensions, document.Constraints, document.Metadata);
        return new DrawingDocument(
            draggedDocument.Entities,
            draggedDocument.Dimensions,
            SketchConstraintPropagationService.ValidateConstraints(draggedDocument, document.Constraints),
            document.Metadata);
    }

    private static bool TryGetSelectedLineEntityId(
        DrawingDocument document,
        string selectionKey,
        out string entityId)
    {
        entityId = string.Empty;
        if (TryParseSegmentSelectionKey(selectionKey, out _, out _))
        {
            return false;
        }

        if (SketchReference.TryParseCanvasPointCoordinates(selectionKey, out var pointEntityId, out _, out _))
        {
            entityId = pointEntityId;
        }
        else
        {
            entityId = selectionKey;
        }

        var selectedId = entityId;
        return document.Entities.Any(entity =>
            entity is LineEntity
            && StringComparer.Ordinal.Equals(entity.Id.Value, selectedId));
    }

    private static bool TryGetDimensionedRectangleGroup(
        DrawingDocument document,
        string selectedEntityId,
        out HashSet<string> rectangleEntityIds)
    {
        return TryGetRectangleGroup(document, selectedEntityId, out rectangleEntityIds)
            && CountDrivingDimensionsForEntityGroup(document.Dimensions, rectangleEntityIds) >= 2;
    }

    private static bool TryGetRectangleGroup(
        DrawingDocument document,
        string selectedEntityId,
        out HashSet<string> rectangleEntityIds)
    {
        rectangleEntityIds = new HashSet<string>(StringComparer.Ordinal);
        var lineIds = document.Entities
            .OfType<LineEntity>()
            .Select(line => line.Id.Value)
            .ToHashSet(StringComparer.Ordinal);
        if (!lineIds.Contains(selectedEntityId))
        {
            return false;
        }

        var adjacency = lineIds.ToDictionary(
            entityId => entityId,
            _ => new HashSet<string>(StringComparer.Ordinal),
            StringComparer.Ordinal);
        foreach (var constraint in document.Constraints)
        {
            if (constraint.Kind != SketchConstraintKind.Coincident
                || constraint.State == SketchConstraintState.Suppressed
                || !TryGetTwoReferenceEntityIds(constraint, out var firstEntityId, out var secondEntityId)
                || !lineIds.Contains(firstEntityId)
                || !lineIds.Contains(secondEntityId)
                || StringComparer.Ordinal.Equals(firstEntityId, secondEntityId))
            {
                continue;
            }

            adjacency[firstEntityId].Add(secondEntityId);
            adjacency[secondEntityId].Add(firstEntityId);
        }

        var queue = new Queue<string>();
        queue.Enqueue(selectedEntityId);
        rectangleEntityIds.Add(selectedEntityId);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var next in adjacency[current])
            {
                if (rectangleEntityIds.Add(next))
                {
                    queue.Enqueue(next);
                }
            }
        }

        return rectangleEntityIds.Count == 4
            && HasRectangleLineRelations(document.Constraints, rectangleEntityIds);
    }

    private static bool HasRectangleLineRelations(
        IReadOnlyList<SketchConstraint> constraints,
        IReadOnlySet<string> entityIds)
    {
        var coincidentCount = 0;
        var parallelPairs = new HashSet<string>(StringComparer.Ordinal);
        var perpendicularCount = 0;
        foreach (var constraint in constraints)
        {
            if (constraint.State == SketchConstraintState.Suppressed)
            {
                continue;
            }

            if (constraint.Kind == SketchConstraintKind.Coincident
                && TryGetTwoReferenceEntityIds(constraint, out var firstCoincidentEntityId, out var secondCoincidentEntityId)
                && entityIds.Contains(firstCoincidentEntityId)
                && entityIds.Contains(secondCoincidentEntityId)
                && !StringComparer.Ordinal.Equals(firstCoincidentEntityId, secondCoincidentEntityId))
            {
                coincidentCount++;
            }

            if ((constraint.Kind == SketchConstraintKind.Parallel || constraint.Kind == SketchConstraintKind.Perpendicular)
                && TryGetTwoReferenceEntityIds(constraint, out var firstLineEntityId, out var secondLineEntityId)
                && entityIds.Contains(firstLineEntityId)
                && entityIds.Contains(secondLineEntityId)
                && !StringComparer.Ordinal.Equals(firstLineEntityId, secondLineEntityId))
            {
                if (constraint.Kind == SketchConstraintKind.Parallel)
                {
                    var pair = string.CompareOrdinal(firstLineEntityId, secondLineEntityId) <= 0
                        ? $"{firstLineEntityId}|{secondLineEntityId}"
                        : $"{secondLineEntityId}|{firstLineEntityId}";
                    parallelPairs.Add(pair);
                }
                else
                {
                    perpendicularCount++;
                }
            }
        }

        return coincidentCount >= 4
            && parallelPairs.Count >= 2
            && perpendicularCount >= 1;
    }

    private static int CountDrivingDimensionsForEntityGroup(
        IReadOnlyList<SketchDimension> dimensions,
        IReadOnlySet<string> entityIds) =>
        dimensions.Count(dimension =>
            dimension.IsDriving
            && dimension.ReferenceKeys.Count > 0
            && dimension.ReferenceKeys
                .Select(GetReferenceEntityId)
                .All(entityId => entityIds.Contains(entityId)));

    private static bool TryGetTwoReferenceEntityIds(
        SketchConstraint constraint,
        out string firstEntityId,
        out string secondEntityId)
    {
        firstEntityId = string.Empty;
        secondEntityId = string.Empty;
        if (constraint.ReferenceKeys.Count < 2
            || !TryGetReferenceEntityId(constraint.ReferenceKeys[0], out firstEntityId)
            || !TryGetReferenceEntityId(constraint.ReferenceKeys[1], out secondEntityId))
        {
            return false;
        }

        return true;
    }

    private static bool TryGetReferenceEntityId(string referenceKey, out string entityId)
    {
        entityId = GetReferenceEntityId(referenceKey);
        return !string.IsNullOrEmpty(entityId);
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
            case EllipseEntity ellipse:
                return TryApplyEllipsePointDrag(
                    entities,
                    fixedReferences,
                    entityIndex,
                    ellipse,
                    label,
                    targetPoint,
                    dragEnd,
                    delta,
                    out status);
            case SplineEntity spline:
                return TryApplySplinePointDrag(
                    entities,
                    fixedReferences,
                    entityIndex,
                    spline,
                    label,
                    delta,
                    dragEnd,
                    out status);
            case PolygonEntity polygon:
                return TryApplyPolygonPointDrag(
                    entities,
                    fixedReferences,
                    entityIndex,
                    polygon,
                    label,
                    delta,
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

    private static bool TryApplyEllipsePointDrag(
        DrawingEntity[] entities,
        SketchFixedReferences fixedReferences,
        int entityIndex,
        EllipseEntity ellipse,
        string label,
        Point2 targetPoint,
        Point2 dragEnd,
        Point2 delta,
        out string status)
    {
        var reference = new SketchReference(ellipse.Id.Value, SketchReferenceTarget.Entity);
        if (label == "center")
        {
            if (!fixedReferences.CanMoveCircleLikeCenter(reference))
            {
                status = "Ellipse center is constrained.";
                return false;
            }

            entities[entityIndex] = ellipse with { Center = Add(ellipse.Center, delta) };
            status = "Moved ellipse center.";
            return true;
        }

        if (!fixedReferences.CanChangeCircleLikeRadius(reference))
        {
            status = "Ellipse axes are constrained.";
            return false;
        }

        var majorLength = SketchGeometryEditor.Distance(new Point2(0, 0), ellipse.MajorAxisEndPoint);
        if (majorLength <= SketchGeometryEditor.Tolerance)
        {
            status = "Ellipse major axis must stay positive.";
            return false;
        }

        if (TryGetDraggedEllipseMajorAxis(ellipse, label, targetPoint, dragEnd, out var majorAxis))
        {
            if (SketchGeometryEditor.Distance(new Point2(0, 0), majorAxis) <= SketchGeometryEditor.Tolerance)
            {
                status = "Ellipse major axis must stay positive.";
                return false;
            }

            entities[entityIndex] = ellipse with { MajorAxisEndPoint = majorAxis };
            status = "Changed ellipse major axis.";
            return true;
        }

        if (label == "quadrant-90" || label == "quadrant-270")
        {
            var majorUnit = new Point2(ellipse.MajorAxisEndPoint.X / majorLength, ellipse.MajorAxisEndPoint.Y / majorLength);
            var minorUnit = new Point2(-majorUnit.Y, majorUnit.X);
            var fromCenter = new Point2(dragEnd.X - ellipse.Center.X, dragEnd.Y - ellipse.Center.Y);
            var minorLength = Math.Abs((fromCenter.X * minorUnit.X) + (fromCenter.Y * minorUnit.Y));
            var ratio = minorLength / majorLength;
            if (ratio <= SketchGeometryEditor.Tolerance)
            {
                status = "Ellipse minor axis must stay positive.";
                return false;
            }

            entities[entityIndex] = ellipse with { MinorRadiusRatio = ratio };
            status = "Changed ellipse minor axis.";
            return true;
        }

        status = "Drag the ellipse center or quadrant point.";
        return false;
    }

    private static bool TryApplySplinePointDrag(
        DrawingEntity[] entities,
        SketchFixedReferences fixedReferences,
        int entityIndex,
        SplineEntity spline,
        string label,
        Point2 delta,
        Point2 dragEnd,
        out string status)
    {
        var reference = new SketchReference(spline.Id.Value, SketchReferenceTarget.Entity);
        if (fixedReferences.IsWholeEntityFixed(reference))
        {
            status = "Spline is constrained.";
            return false;
        }

        if (spline.FitPoints.Count >= 2)
        {
            return TryApplySplineEditablePointDrag(
                entities,
                entityIndex,
                spline,
                spline.FitPoints,
                isFitSpline: true,
                label,
                delta,
                dragEnd,
                out status);
        }

        if (spline.ControlPoints.Count >= 2)
        {
            return TryApplySplineEditablePointDrag(
                entities,
                entityIndex,
                spline,
                spline.ControlPoints,
                isFitSpline: false,
                label,
                delta,
                dragEnd,
                out status);
        }

        status = "Spline has no editable points.";
        return false;
    }

    private static bool TryApplySplineEditablePointDrag(
        DrawingEntity[] entities,
        int entityIndex,
        SplineEntity spline,
        IReadOnlyList<Point2> sourcePoints,
        bool isFitSpline,
        string label,
        Point2 delta,
        Point2 dragEnd,
        out string status)
    {
        var points = sourcePoints.ToArray();
        var pointPrefix = isFitSpline ? "fit-" : "control-";
        if (isFitSpline && (label == "tangent-start" || label == "tangent-end"))
        {
            entities[entityIndex] = WithSplineTangentHandle(spline, label, dragEnd);
            status = label == "tangent-start"
                ? "Changed spline start tangent."
                : "Changed spline end tangent.";
            return true;
        }

        if (TryParseIndexedLabel(label, pointPrefix, out var pointIndex)
            || TryGetSplineEndpointIndex(label, points.Length, out pointIndex))
        {
            if (pointIndex < 0 || pointIndex >= points.Length)
            {
                status = "Selected spline point no longer exists.";
                return false;
            }

            points[pointIndex] = Add(points[pointIndex], delta);
            entities[entityIndex] = WithSplineEditablePoints(spline, points, isFitSpline, pointIndex, delta);
            status = isFitSpline ? "Moved spline fit point." : "Moved spline control point.";
            return true;
        }

        status = "Drag a spline fit point, control point, endpoint, or tangent handle.";
        return false;
    }

    private static bool TryGetSplineEndpointIndex(string label, int pointCount, out int pointIndex)
    {
        pointIndex = label switch
        {
            "start" => 0,
            "end" => pointCount - 1,
            _ => -1
        };
        return pointIndex >= 0;
    }

    private static SplineEntity WithSplineEditablePoints(
        SplineEntity spline,
        IReadOnlyList<Point2> points,
        bool isFitSpline,
        int movedPointIndex,
        Point2 delta)
    {
        if (!isFitSpline)
        {
            return new SplineEntity(
                spline.Id,
                spline.Degree,
                points,
                spline.Knots,
                spline.Weights,
                spline.IsConstruction,
                spline.FitPoints,
                spline.StartTangentHandle,
                spline.EndTangentHandle);
        }

        var startTangentHandle = movedPointIndex == 0 && spline.StartTangentHandle is { } start
            ? Add(start, delta)
            : spline.StartTangentHandle;
        var endTangentHandle = movedPointIndex == points.Count - 1 && spline.EndTangentHandle is { } end
            ? Add(end, delta)
            : spline.EndTangentHandle;
        return SplineEntity.FromFitPoints(
            spline.Id,
            points,
            spline.IsConstruction,
            startTangentHandle,
            endTangentHandle);
    }

    private static SplineEntity WithSplineTangentHandle(
        SplineEntity spline,
        string label,
        Point2 handle)
    {
        var startTangentHandle = label == "tangent-start" ? handle : spline.StartTangentHandle;
        var endTangentHandle = label == "tangent-end" ? handle : spline.EndTangentHandle;
        return SplineEntity.FromFitPoints(
            spline.Id,
            spline.FitPoints,
            spline.IsConstruction,
            startTangentHandle,
            endTangentHandle);
    }

    private static bool TryGetDraggedEllipseMajorAxis(
        EllipseEntity ellipse,
        string label,
        Point2 targetPoint,
        Point2 dragEnd,
        out Point2 majorAxis)
    {
        if (label == "quadrant-0"
            || ((label == "start" || label == "end")
                && SketchGeometryEditor.AreClose(targetPoint, Add(ellipse.Center, ellipse.MajorAxisEndPoint))))
        {
            majorAxis = new Point2(dragEnd.X - ellipse.Center.X, dragEnd.Y - ellipse.Center.Y);
            return true;
        }

        if (label == "quadrant-180"
            || ((label == "start" || label == "end")
                && SketchGeometryEditor.AreClose(
                    targetPoint,
                    new Point2(ellipse.Center.X - ellipse.MajorAxisEndPoint.X, ellipse.Center.Y - ellipse.MajorAxisEndPoint.Y))))
        {
            majorAxis = new Point2(ellipse.Center.X - dragEnd.X, ellipse.Center.Y - dragEnd.Y);
            return true;
        }

        majorAxis = default;
        return false;
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
            case EllipseEntity ellipse:
                return TryTranslateEllipse(entities, fixedReferences, entityIndex, ellipse, delta, out status);
            case SplineEntity spline:
                return TryTranslateSpline(entities, fixedReferences, entityIndex, spline, delta, out status);
            case PolygonEntity polygon:
                return TryTranslatePolygon(entities, fixedReferences, entityIndex, polygon, delta, out status);
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

    private static bool TryTranslateEntityById(
        DrawingEntity[] entities,
        SketchFixedReferences fixedReferences,
        string entityId,
        Point2 delta,
        out string status)
    {
        if (!SketchGeometryEditor.TryFindEntity(entities, entityId, out var entityIndex, out var entity))
        {
            status = "Selected geometry no longer exists.";
            return false;
        }

        switch (entity)
        {
            case LineEntity line:
                return TryTranslateLine(entities, fixedReferences, entityIndex, line, delta, out status);
            case PolylineEntity polyline:
                return TryTranslatePolyline(entities, fixedReferences, entityIndex, polyline, delta, out status);
            case CircleEntity circle:
                return TryTranslateCircle(entities, fixedReferences, entityIndex, circle, delta, out status);
            case ArcEntity arc:
                return TryTranslateArc(entities, fixedReferences, entityIndex, arc, delta, out status);
            case EllipseEntity ellipse:
                return TryTranslateEllipse(entities, fixedReferences, entityIndex, ellipse, delta, out status);
            case SplineEntity spline:
                return TryTranslateSpline(entities, fixedReferences, entityIndex, spline, delta, out status);
            case PolygonEntity polygon:
                return TryTranslatePolygon(entities, fixedReferences, entityIndex, polygon, delta, out status);
            case PointEntity pointEntity:
                return TrySetPointEntityLocation(entities, fixedReferences, entityIndex, pointEntity, Add(pointEntity.Location, delta), out status);
            default:
                status = "Selected geometry cannot be dragged yet.";
                return false;
        }
    }

    private static bool TryTranslateCircle(
        DrawingEntity[] entities,
        SketchFixedReferences fixedReferences,
        int entityIndex,
        CircleEntity circle,
        Point2 delta,
        out string status)
    {
        var reference = new SketchReference(circle.Id.Value, SketchReferenceTarget.Entity);
        if (!fixedReferences.CanMoveCircleLikeCenter(reference))
        {
            status = "Circle is constrained.";
            return false;
        }

        entities[entityIndex] = circle with { Center = Add(circle.Center, delta) };
        status = "Moved circle.";
        return true;
    }

    private static bool TryTranslateArc(
        DrawingEntity[] entities,
        SketchFixedReferences fixedReferences,
        int entityIndex,
        ArcEntity arc,
        Point2 delta,
        out string status)
    {
        var reference = new SketchReference(arc.Id.Value, SketchReferenceTarget.Entity);
        if (!fixedReferences.CanMoveCircleLikeCenter(reference))
        {
            status = "Arc is constrained.";
            return false;
        }

        entities[entityIndex] = arc with { Center = Add(arc.Center, delta) };
        status = "Moved arc.";
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

    private static bool TryTranslateEllipse(
        DrawingEntity[] entities,
        SketchFixedReferences fixedReferences,
        int entityIndex,
        EllipseEntity ellipse,
        Point2 delta,
        out string status)
    {
        var reference = new SketchReference(ellipse.Id.Value, SketchReferenceTarget.Entity);
        if (!fixedReferences.CanMoveCircleLikeCenter(reference))
        {
            status = "Ellipse is constrained.";
            return false;
        }

        entities[entityIndex] = ellipse with { Center = Add(ellipse.Center, delta) };
        status = "Moved ellipse.";
        return true;
    }

    private static bool TryTranslateSpline(
        DrawingEntity[] entities,
        SketchFixedReferences fixedReferences,
        int entityIndex,
        SplineEntity spline,
        Point2 delta,
        out string status)
    {
        var reference = new SketchReference(spline.Id.Value, SketchReferenceTarget.Entity);
        if (fixedReferences.IsWholeEntityFixed(reference))
        {
            status = "Spline is constrained.";
            return false;
        }

        entities[entityIndex] = new SplineEntity(
            spline.Id,
            spline.Degree,
            spline.ControlPoints.Select(point => Add(point, delta)),
            spline.Knots,
            spline.Weights,
            spline.IsConstruction,
            spline.FitPoints.Select(point => Add(point, delta)),
            spline.StartTangentHandle is { } startTangentHandle ? Add(startTangentHandle, delta) : null,
            spline.EndTangentHandle is { } endTangentHandle ? Add(endTangentHandle, delta) : null);
        status = "Moved spline.";
        return true;
    }

    private static bool TryApplyPolygonPointDrag(
        DrawingEntity[] entities,
        SketchFixedReferences fixedReferences,
        int entityIndex,
        PolygonEntity polygon,
        string label,
        Point2 delta,
        out string status)
    {
        if (label == "center" || label.StartsWith("mid-", StringComparison.Ordinal))
        {
            return TryTranslatePolygon(entities, fixedReferences, entityIndex, polygon, delta, out status);
        }

        status = "Drag the polygon center to move it.";
        return false;
    }

    private static bool TryTranslatePolygon(
        DrawingEntity[] entities,
        SketchFixedReferences fixedReferences,
        int entityIndex,
        PolygonEntity polygon,
        Point2 delta,
        out string status)
    {
        var reference = new SketchReference(polygon.Id.Value, SketchReferenceTarget.Entity);
        if (!fixedReferences.CanMoveCircleLikeCenter(reference))
        {
            status = "Polygon is constrained.";
            return false;
        }

        entities[entityIndex] = polygon with { Center = Add(polygon.Center, delta) };
        status = "Moved polygon.";
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

    private static void PropagateCoincidentConstraints(
        IReadOnlyList<DrawingEntity> originalEntities,
        DrawingEntity[] entities,
        IReadOnlyList<SketchConstraint> constraints,
        SketchFixedReferences fixedReferences)
    {
        var queue = new Queue<SketchReference>(GetChangedPointReferences(originalEntities, entities));
        var queued = new HashSet<string>(queue.Select(reference => reference.ToString()), StringComparer.Ordinal);
        var guard = 0;
        var guardLimit = Math.Max(64, constraints.Count * 8);

        while (queue.Count > 0 && guard++ < guardLimit)
        {
            var changedReference = queue.Dequeue();
            queued.Remove(changedReference.ToString());
            if (!SketchGeometryEditor.TryGetPoint(entities, changedReference, out var changedPoint))
            {
                continue;
            }

            foreach (var constraint in constraints)
            {
                if (constraint.Kind != SketchConstraintKind.Coincident
                    || constraint.State == SketchConstraintState.Suppressed
                    || !TryGetCoincidentPair(constraint, out var firstReference, out var secondReference))
                {
                    continue;
                }

                var otherReference = ReferenceEquals(firstReference, changedReference)
                    ? secondReference
                    : ReferenceEquals(secondReference, changedReference)
                        ? firstReference
                        : (SketchReference?)null;
                if (!otherReference.HasValue
                    || !SketchGeometryEditor.TryGetPoint(entities, otherReference.Value, out var otherPoint)
                    || SketchGeometryEditor.AreClose(changedPoint, otherPoint)
                    || !fixedReferences.CanMovePoint(otherReference.Value)
                    || !SketchGeometryEditor.TrySetPoint(entities, otherReference.Value, changedPoint))
                {
                    continue;
                }

                var otherKey = otherReference.Value.ToString();
                if (queued.Add(otherKey))
                {
                    queue.Enqueue(otherReference.Value);
                }
            }
        }
    }

    private static IReadOnlyList<SketchReference> GetChangedPointReferences(
        IReadOnlyList<DrawingEntity> originalEntities,
        IReadOnlyList<DrawingEntity> entities)
    {
        var changedReferences = new List<SketchReference>();
        foreach (var reference in EnumeratePointReferences(originalEntities))
        {
            if (SketchGeometryEditor.TryGetPoint(originalEntities, reference, out var before)
                && SketchGeometryEditor.TryGetPoint(entities, reference, out var after)
                && !SketchGeometryEditor.AreClose(before, after))
            {
                changedReferences.Add(reference);
            }
        }

        return changedReferences;
    }

    private static bool TryGetCoincidentPair(
        SketchConstraint constraint,
        out SketchReference firstReference,
        out SketchReference secondReference)
    {
        if (constraint.ReferenceKeys.Count >= 2
            && SketchReference.TryParse(constraint.ReferenceKeys[0], out firstReference)
            && SketchReference.TryParse(constraint.ReferenceKeys[1], out secondReference))
        {
            return true;
        }

        firstReference = default;
        secondReference = default;
        return false;
    }

    private static IReadOnlyList<SketchConstraint> ValidateConstraints(
        DrawingDocument document,
        IReadOnlyList<SketchConstraint> constraints) =>
        constraints
            .Select(constraint => SketchConstraintService.ValidateConstraint(document, constraint))
            .ToArray();

    private static bool ReferenceEquals(SketchReference first, SketchReference second) =>
        StringComparer.Ordinal.Equals(first.ToString(), second.ToString());

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

    private static DrawingDocument AddCoincidentConstraintForSnappedPointDrag(
        DrawingDocument document,
        string selectionKey,
        ref string status)
    {
        if (!TryGetDraggedPointReference(selectionKey, out var draggedReference)
            || !SketchGeometryEditor.TryGetPoint(document.Entities, draggedReference, out var draggedPoint)
            || !TryFindCoincidentPointReference(document.Entities, draggedReference, draggedPoint, out var anchorReference)
            || HasCoincidentConstraint(document.Constraints, anchorReference, draggedReference))
        {
            return document;
        }

        var constraint = new SketchConstraint(
            $"drag-coincident-{Guid.NewGuid():N}",
            SketchConstraintKind.Coincident,
            new[] { anchorReference.ToString(), draggedReference.ToString() },
            SketchConstraintState.Satisfied);
        var constrainedDocument = SketchConstraintService.ApplyConstraint(document, constraint);
        status = $"{status} Added coincident constraint.";
        return constrainedDocument;
    }

    private static bool TryGetDraggedPointReference(string selectionKey, out SketchReference reference)
    {
        if (SketchReference.TryParse(selectionKey, out reference)
            && reference.Target != SketchReferenceTarget.Entity)
        {
            return true;
        }

        reference = default;
        return false;
    }

    private static bool TryFindCoincidentPointReference(
        IReadOnlyList<DrawingEntity> entities,
        SketchReference draggedReference,
        Point2 draggedPoint,
        out SketchReference anchorReference)
    {
        foreach (var candidate in EnumeratePointReferences(entities))
        {
            if (StringComparer.Ordinal.Equals(candidate.EntityId, draggedReference.EntityId)
                || StringComparer.Ordinal.Equals(candidate.ToString(), draggedReference.ToString())
                || !SketchGeometryEditor.TryGetPoint(entities, candidate, out var candidatePoint)
                || !SketchGeometryEditor.AreClose(candidatePoint, draggedPoint))
            {
                continue;
            }

            anchorReference = candidate;
            return true;
        }

        anchorReference = default;
        return false;
    }

    private static IEnumerable<SketchReference> EnumeratePointReferences(IReadOnlyList<DrawingEntity> entities)
    {
        foreach (var entity in entities)
        {
            switch (entity)
            {
                case LineEntity line:
                    yield return new SketchReference(line.Id.Value, SketchReferenceTarget.Start);
                    yield return new SketchReference(line.Id.Value, SketchReferenceTarget.End);
                    break;
                case PolylineEntity polyline:
                    for (var index = 0; index < polyline.Vertices.Count - 1; index++)
                    {
                        yield return new SketchReference(polyline.Id.Value, SketchReferenceTarget.Start, index);
                        yield return new SketchReference(polyline.Id.Value, SketchReferenceTarget.End, index);
                    }

                    break;
                case CircleEntity circle:
                    yield return new SketchReference(circle.Id.Value, SketchReferenceTarget.Center);
                    break;
                case ArcEntity arc:
                    yield return new SketchReference(arc.Id.Value, SketchReferenceTarget.Start);
                    yield return new SketchReference(arc.Id.Value, SketchReferenceTarget.End);
                    yield return new SketchReference(arc.Id.Value, SketchReferenceTarget.Center);
                    break;
                case PolygonEntity polygon:
                    yield return new SketchReference(polygon.Id.Value, SketchReferenceTarget.Center);
                    break;
                case PointEntity point:
                    yield return new SketchReference(point.Id.Value, SketchReferenceTarget.Entity);
                    break;
            }
        }
    }

    private static bool HasCoincidentConstraint(
        IReadOnlyList<SketchConstraint> constraints,
        SketchReference firstReference,
        SketchReference secondReference)
    {
        var firstKey = firstReference.ToString();
        var secondKey = secondReference.ToString();
        return constraints.Any(constraint =>
            constraint.Kind == SketchConstraintKind.Coincident
            && constraint.ReferenceKeys.Count >= 2
            && ((StringComparer.Ordinal.Equals(constraint.ReferenceKeys[0], firstKey)
                    && StringComparer.Ordinal.Equals(constraint.ReferenceKeys[1], secondKey))
                || (StringComparer.Ordinal.Equals(constraint.ReferenceKeys[0], secondKey)
                    && StringComparer.Ordinal.Equals(constraint.ReferenceKeys[1], firstKey))));
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
                CircleEntity or ArcEntity or EllipseEntity => normalizedLabel == "center",
                PolygonEntity => normalizedLabel == "center" || normalizedLabel.StartsWith("mid-", StringComparison.Ordinal),
                PointEntity => normalizedLabel == "point",
                _ => false
            };
            entityId = translated ? pointEntityId : string.Empty;
            return translated;
        }

        var wholeEntity = document.Entities.FirstOrDefault(entity => StringComparer.Ordinal.Equals(entity.Id.Value, selectionKey));
        var isWholeEntityTranslation = wholeEntity is LineEntity or PolylineEntity or EllipseEntity or SplineEntity or PolygonEntity or PointEntity;
        entityId = isWholeEntityTranslation ? selectionKey : string.Empty;
        return isWholeEntityTranslation;
    }

    private static bool TryGetSelectedEntityId(
        DrawingDocument document,
        string selectionKey,
        out string entityId)
    {
        if (TryParseSegmentSelectionKey(selectionKey, out entityId, out _))
        {
            var selectedId = entityId;
            return document.Entities.Any(entity => StringComparer.Ordinal.Equals(entity.Id.Value, selectedId));
        }

        if (SketchReference.TryParseCanvasPointCoordinates(selectionKey, out entityId, out _, out _))
        {
            var selectedId = entityId;
            return document.Entities.Any(entity => StringComparer.Ordinal.Equals(entity.Id.Value, selectedId));
        }

        entityId = selectionKey;
        return document.Entities.Any(entity => StringComparer.Ordinal.Equals(entity.Id.Value, selectionKey));
    }

    private static IReadOnlyList<SketchDimension> TranslateDimensionAnchors(
        IReadOnlyList<SketchDimension> dimensions,
        string entityId,
        Point2 delta) =>
        TranslateDimensionAnchors(
            dimensions,
            new HashSet<string>(StringComparer.Ordinal) { entityId },
            delta);

    private static IReadOnlyList<SketchDimension> TranslateDimensionAnchors(
        IReadOnlyList<SketchDimension> dimensions,
        IReadOnlySet<string> entityIds,
        Point2 delta)
    {
        return dimensions
            .Select(dimension => ShouldTranslateDimensionAnchor(dimension, entityIds)
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

    private static bool ShouldTranslateDimensionAnchor(SketchDimension dimension, IReadOnlySet<string> entityIds)
    {
        return dimension.Anchor.HasValue
            && dimension.ReferenceKeys.Count > 0
            && dimension.ReferenceKeys
                .Select(GetReferenceEntityId)
                .All(entityIds.Contains);
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

    private static double Dot(Point2 first, Point2 second) =>
        (first.X * second.X) + (first.Y * second.Y);

    private static Point2 ProjectDeltaOntoAxis(Point2 delta, Point2 axis)
    {
        var scalar = Dot(delta, axis);
        return new Point2(axis.X * scalar, axis.Y * scalar);
    }

    private static bool TryNormalize(Point2 vector, out Point2 unit)
    {
        var length = SketchGeometryEditor.Distance(new Point2(0, 0), vector);
        if (length <= SketchGeometryEditor.Tolerance)
        {
            unit = default;
            return false;
        }

        unit = new Point2(vector.X / length, vector.Y / length);
        return true;
    }

    private static bool ShouldMoveRectangleEndpoint(Point2 endpoint, LineEntity selectedLine) =>
        SketchGeometryEditor.AreClose(endpoint, selectedLine.Start)
        || SketchGeometryEditor.AreClose(endpoint, selectedLine.End);

    private static Point2 ProjectDeltaPerpendicularToLine(Point2 delta, Point2 start, Point2 end)
    {
        var axisX = end.X - start.X;
        var axisY = end.Y - start.Y;
        var lengthSquared = (axisX * axisX) + (axisY * axisY);
        if (lengthSquared <= SketchGeometryEditor.Tolerance * SketchGeometryEditor.Tolerance)
        {
            return delta;
        }

        var alongScale = ((delta.X * axisX) + (delta.Y * axisY)) / lengthSquared;
        return new Point2(
            delta.X - (axisX * alongScale),
            delta.Y - (axisY * alongScale));
    }

    private static Point2 ProjectDeltaOntoLine(Point2 delta, Point2 start, Point2 end)
    {
        var axisX = end.X - start.X;
        var axisY = end.Y - start.Y;
        var lengthSquared = (axisX * axisX) + (axisY * axisY);
        if (lengthSquared <= SketchGeometryEditor.Tolerance * SketchGeometryEditor.Tolerance)
        {
            return new Point2(0, 0);
        }

        var alongScale = ((delta.X * axisX) + (delta.Y * axisY)) / lengthSquared;
        return new Point2(axisX * alongScale, axisY * alongScale);
    }

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
            (EllipseEntity a, EllipseEntity b) => Close(a.Center, b.Center)
                && Close(a.MajorAxisEndPoint, b.MajorAxisEndPoint)
                && SketchGeometryEditor.AreClose(a.MinorRadiusRatio, b.MinorRadiusRatio)
                && SketchGeometryEditor.AreClose(a.StartParameterDegrees, b.StartParameterDegrees)
                && SketchGeometryEditor.AreClose(a.EndParameterDegrees, b.EndParameterDegrees),
            (PolygonEntity a, PolygonEntity b) => Close(a.Center, b.Center)
                && SketchGeometryEditor.AreClose(a.Radius, b.Radius)
                && SketchGeometryEditor.AreClose(a.RotationAngleDegrees, b.RotationAngleDegrees)
                && a.NormalizedSideCount == b.NormalizedSideCount
                && a.Circumscribed == b.Circumscribed,
            (SplineEntity a, SplineEntity b) => a.ControlPoints.Count == b.ControlPoints.Count
                && a.ControlPoints.Zip(b.ControlPoints).All(pair => Close(pair.First, pair.Second))
                && a.FitPoints.Count == b.FitPoints.Count
                && a.FitPoints.Zip(b.FitPoints).All(pair => Close(pair.First, pair.Second))
                && OptionalClose(a.StartTangentHandle, b.StartTangentHandle)
                && OptionalClose(a.EndTangentHandle, b.EndTangentHandle),
            (PointEntity a, PointEntity b) => Close(a.Location, b.Location),
            _ => first.Equals(second)
        };
    }

    private static bool OptionalClose(Point2? first, Point2? second)
    {
        if (first is null && second is null)
        {
            return true;
        }

        return first is { } firstPoint
            && second is { } secondPoint
            && Close(firstPoint, secondPoint);
    }

    private static bool DrivingDimensionsRemainSatisfied(
        IReadOnlyList<DrawingEntity> entities,
        IReadOnlyList<SketchDimension> dimensions)
    {
        foreach (var dimension in dimensions)
        {
            if (!dimension.IsDriving
                || !TryMeasureDimension(entities, dimension, out var measured)
                || SketchGeometryEditor.AreClose(Math.Abs(measured), Math.Abs(dimension.Value)))
            {
                continue;
            }

            return false;
        }

        return true;
    }

    private static bool TryMeasureDimension(
        IReadOnlyList<DrawingEntity> entities,
        SketchDimension dimension,
        out double measured)
    {
        switch (dimension.Kind)
        {
            case SketchDimensionKind.LinearDistance:
                return TryMeasurePointDistance(entities, dimension, out measured);
            case SketchDimensionKind.HorizontalDistance:
                return TryMeasureAxisDistance(entities, dimension, isHorizontal: true, out measured);
            case SketchDimensionKind.VerticalDistance:
                return TryMeasureAxisDistance(entities, dimension, isHorizontal: false, out measured);
            case SketchDimensionKind.Radius:
                return TryMeasureCircleLikeRadius(entities, dimension, diameter: false, out measured);
            case SketchDimensionKind.Diameter:
                return TryMeasureCircleLikeRadius(entities, dimension, diameter: true, out measured);
            default:
                measured = default;
                return false;
        }
    }

    private static bool TryMeasurePointDistance(
        IReadOnlyList<DrawingEntity> entities,
        SketchDimension dimension,
        out double measured)
    {
        if (TryGetTwoDimensionPointReferences(dimension, out var firstReference, out var secondReference)
            && SketchGeometryEditor.TryGetPoint(entities, firstReference, out var firstPoint)
            && SketchGeometryEditor.TryGetPoint(entities, secondReference, out var secondPoint))
        {
            measured = SketchGeometryEditor.Distance(firstPoint, secondPoint);
            return true;
        }

        measured = default;
        return false;
    }

    private static bool TryMeasureAxisDistance(
        IReadOnlyList<DrawingEntity> entities,
        SketchDimension dimension,
        bool isHorizontal,
        out double measured)
    {
        if (TryGetTwoDimensionPointReferences(dimension, out var firstReference, out var secondReference)
            && SketchGeometryEditor.TryGetPoint(entities, firstReference, out var firstPoint)
            && SketchGeometryEditor.TryGetPoint(entities, secondReference, out var secondPoint))
        {
            measured = isHorizontal
                ? Math.Abs(secondPoint.X - firstPoint.X)
                : Math.Abs(secondPoint.Y - firstPoint.Y);
            return true;
        }

        measured = default;
        return false;
    }

    private static bool TryMeasureCircleLikeRadius(
        IReadOnlyList<DrawingEntity> entities,
        SketchDimension dimension,
        bool diameter,
        out double measured)
    {
        if (dimension.ReferenceKeys.Count > 0
            && SketchReference.TryParse(dimension.ReferenceKeys[0], out var reference)
            && SketchGeometryEditor.TryGetCircleLike(entities, reference, out _, out _, out var radius))
        {
            measured = diameter ? radius * 2.0 : radius;
            return true;
        }

        measured = default;
        return false;
    }

    private static bool TryGetTwoDimensionPointReferences(
        SketchDimension dimension,
        out SketchReference firstReference,
        out SketchReference secondReference)
    {
        if (dimension.ReferenceKeys.Count >= 2
            && SketchReference.TryParse(dimension.ReferenceKeys[0], out firstReference)
            && SketchReference.TryParse(dimension.ReferenceKeys[1], out secondReference))
        {
            return true;
        }

        firstReference = default;
        secondReference = default;
        return false;
    }

    private static bool Close(Point2 first, Point2 second) =>
        SketchGeometryEditor.AreClose(first, second);
}

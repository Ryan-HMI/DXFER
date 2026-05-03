using System.Globalization;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;

namespace DXFER.Blazor.Selection;

public static class SelectionDeleteResolver
{
    private const string SegmentKeySeparator = "|segment|";
    private const string PointKeySeparator = "|point|";

    public static bool CanDeleteSelection(DrawingDocument document, IEnumerable<string> selectionKeys) =>
        DeleteSelection(document, selectionKeys).DeletedGeometryCount > 0;

    public static SelectionDeleteResult DeleteSelection(
        DrawingDocument document,
        IEnumerable<string> selectionKeys)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(selectionKeys);

        var wholeEntityIds = new HashSet<string>(StringComparer.Ordinal);
        var segmentSelections = new Dictionary<string, SortedSet<int>>(StringComparer.Ordinal);

        foreach (var selectionKey in selectionKeys.Where(key => !string.IsNullOrWhiteSpace(key)))
        {
            if (selectionKey.Contains(PointKeySeparator, StringComparison.Ordinal))
            {
                continue;
            }

            if (TryParseSegmentSelectionKey(selectionKey, out var entityId, out var segmentIndex))
            {
                if (!segmentSelections.TryGetValue(entityId, out var segmentIndexes))
                {
                    segmentIndexes = new SortedSet<int>();
                    segmentSelections.Add(entityId, segmentIndexes);
                }

                segmentIndexes.Add(segmentIndex);
                continue;
            }

            wholeEntityIds.Add(selectionKey);
        }

        if (wholeEntityIds.Count == 0 && segmentSelections.Count == 0)
        {
            return new SelectionDeleteResult(document, 0, 0);
        }

        var usedEntityIds = document.Entities
            .Select(entity => entity.Id.Value)
            .ToHashSet(StringComparer.Ordinal);
        var nextEntities = new List<DrawingEntity>();
        var deletedEntities = 0;
        var deletedSegments = 0;

        foreach (var entity in document.Entities)
        {
            var entityId = entity.Id.Value;
            if (wholeEntityIds.Contains(entityId))
            {
                deletedEntities++;
                usedEntityIds.Remove(entityId);
                continue;
            }

            if (entity is PolylineEntity polyline
                && segmentSelections.TryGetValue(entityId, out var selectedSegments))
            {
                var split = DeletePolylineSegments(polyline, selectedSegments, usedEntityIds);
                if (split.DeletedSegments > 0)
                {
                    deletedSegments += split.DeletedSegments;
                    nextEntities.AddRange(split.Entities);
                    continue;
                }
            }

            nextEntities.Add(entity);
        }

        return deletedEntities == 0 && deletedSegments == 0
            ? new SelectionDeleteResult(document, 0, 0)
            : new SelectionDeleteResult(new DrawingDocument(nextEntities), deletedEntities, deletedSegments);
    }

    private static PolylineDeleteResult DeletePolylineSegments(
        PolylineEntity polyline,
        SortedSet<int> selectedSegments,
        HashSet<string> usedEntityIds)
    {
        var validSegments = selectedSegments
            .Where(segmentIndex => segmentIndex >= 0 && segmentIndex < polyline.Vertices.Count - 1)
            .ToHashSet();
        if (validSegments.Count == 0)
        {
            return new PolylineDeleteResult(Array.Empty<DrawingEntity>(), 0);
        }

        var runs = new List<IReadOnlyList<Point2>>();
        var currentRun = new List<Point2> { polyline.Vertices[0] };

        for (var segmentIndex = 0; segmentIndex < polyline.Vertices.Count - 1; segmentIndex++)
        {
            var nextPoint = polyline.Vertices[segmentIndex + 1];
            if (validSegments.Contains(segmentIndex))
            {
                if (currentRun.Count >= 2)
                {
                    runs.Add(currentRun);
                }

                currentRun = new List<Point2> { nextPoint };
                continue;
            }

            currentRun.Add(nextPoint);
        }

        if (currentRun.Count >= 2)
        {
            runs.Add(currentRun);
        }

        return new PolylineDeleteResult(
            CreatePolylineEntities(polyline, runs, usedEntityIds).ToArray(),
            validSegments.Count);
    }

    private static IEnumerable<DrawingEntity> CreatePolylineEntities(
        PolylineEntity source,
        IReadOnlyList<IReadOnlyList<Point2>> runs,
        HashSet<string> usedEntityIds)
    {
        for (var index = 0; index < runs.Count; index++)
        {
            var id = index == 0
                ? source.Id
                : EntityId.Create(ReserveUniqueEntityId($"{source.Id.Value}-split-{index}", usedEntityIds));

            yield return new PolylineEntity(id, runs[index]);
        }
    }

    private static string ReserveUniqueEntityId(string baseId, HashSet<string> usedEntityIds)
    {
        var candidate = baseId;
        var suffix = 1;
        while (!usedEntityIds.Add(candidate))
        {
            suffix++;
            candidate = string.Create(
                CultureInfo.InvariantCulture,
                $"{baseId}-{suffix}");
        }

        return candidate;
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

    private sealed record PolylineDeleteResult(
        IReadOnlyList<DrawingEntity> Entities,
        int DeletedSegments);
}

public readonly record struct SelectionDeleteResult(
    DrawingDocument Document,
    int DeletedEntities,
    int DeletedSegments)
{
    public int DeletedGeometryCount => DeletedEntities + DeletedSegments;
}

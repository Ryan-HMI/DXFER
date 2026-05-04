using DXFER.Core.Documents;

namespace DXFER.Core.Operations;

public static class DrawingConstructionService
{
    public static ConstructionToggleResult ToggleSelected(DrawingDocument document, IEnumerable<string> selectedEntityIds)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(selectedEntityIds);

        var selected = selectedEntityIds.ToHashSet(StringComparer.Ordinal);
        if (selected.Count == 0)
        {
            return new ConstructionToggleResult(document, 0, false);
        }

        var selectedEntities = document.Entities
            .Where(entity => selected.Contains(entity.Id.Value))
            .ToArray();
        if (selectedEntities.Length == 0)
        {
            return new ConstructionToggleResult(document, 0, false);
        }

        var targetConstructionState = selectedEntities.Any(entity => !entity.IsConstruction);
        var changedCount = 0;
        var nextEntities = document.Entities
            .Select(entity =>
            {
                if (!selected.Contains(entity.Id.Value))
                {
                    return entity;
                }

                changedCount++;
                return entity.WithConstruction(targetConstructionState);
            })
            .ToArray();

        return new ConstructionToggleResult(
            new DrawingDocument(nextEntities, document.Dimensions, document.Constraints),
            changedCount,
            targetConstructionState);
    }
}

public sealed record ConstructionToggleResult(
    DrawingDocument Document,
    int ChangedCount,
    bool IsConstruction);

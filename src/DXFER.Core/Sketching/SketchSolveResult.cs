using System.Collections.ObjectModel;
using DXFER.Core.Documents;

namespace DXFER.Core.Sketching;

public sealed class SketchSolveResult
{
    public SketchSolveResult(
        SketchSolveStatus status,
        DrawingDocument document,
        IEnumerable<string>? diagnostics = null,
        IEnumerable<SketchSolveDiagnostic>? affectedDiagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        Status = status;
        Document = document;
        Diagnostics = Array.AsReadOnly((diagnostics ?? Array.Empty<string>()).ToArray());
        AffectedDiagnostics = Array.AsReadOnly((affectedDiagnostics ?? Array.Empty<SketchSolveDiagnostic>()).ToArray());
    }

    public SketchSolveStatus Status { get; }

    public DrawingDocument Document { get; }

    public ReadOnlyCollection<string> Diagnostics { get; }

    public ReadOnlyCollection<SketchSolveDiagnostic> AffectedDiagnostics { get; }
}

public sealed class SketchSolveDiagnostic
{
    public SketchSolveDiagnostic(
        string itemId,
        string itemKind,
        IEnumerable<string> referenceKeys,
        string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemKind);
        ArgumentNullException.ThrowIfNull(referenceKeys);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        ItemId = itemId;
        ItemKind = itemKind;
        ReferenceKeys = Array.AsReadOnly(referenceKeys.ToArray());
        Message = message;
    }

    public string ItemId { get; }

    public string ItemKind { get; }

    public ReadOnlyCollection<string> ReferenceKeys { get; }

    public string Message { get; }
}

public static class SketchSolveDiagnostics
{
    public static IReadOnlyList<SketchSolveDiagnostic> FromDocument(DrawingDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var diagnostics = new List<SketchSolveDiagnostic>();
        foreach (var constraint in document.Constraints)
        {
            if (constraint.State != SketchConstraintState.Unsatisfied)
            {
                continue;
            }

            diagnostics.Add(new SketchSolveDiagnostic(
                constraint.Id,
                "constraint",
                constraint.ReferenceKeys,
                $"Constraint '{constraint.Id}' is unsatisfied."));
        }

        foreach (var dimension in document.Dimensions)
        {
            if (SketchDimensionSolverService.GetDimensionState(document, dimension) != SketchConstraintState.Unsatisfied)
            {
                continue;
            }

            diagnostics.Add(new SketchSolveDiagnostic(
                dimension.Id,
                "dimension",
                dimension.ReferenceKeys,
                $"Dimension '{dimension.Id}' is unsatisfied."));
        }

        return diagnostics;
    }
}

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

public enum SketchSolveDiagnosticReason
{
    Unresolved,
    RedundantDrivingDimension,
    ConflictingDrivingDimension
}

public sealed class SketchSolveDiagnostic
{
    public SketchSolveDiagnostic(
        string itemId,
        string itemKind,
        IEnumerable<string> referenceKeys,
        string message,
        SketchSolveDiagnosticReason reason = SketchSolveDiagnosticReason.Unresolved)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemKind);
        ArgumentNullException.ThrowIfNull(referenceKeys);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        ItemId = itemId;
        ItemKind = itemKind;
        ReferenceKeys = Array.AsReadOnly(referenceKeys.ToArray());
        Message = message;
        Reason = reason;
    }

    public string ItemId { get; }

    public string ItemKind { get; }

    public ReadOnlyCollection<string> ReferenceKeys { get; }

    public string Message { get; }

    public SketchSolveDiagnosticReason Reason { get; }
}

public static class SketchSolveDiagnostics
{
    public static string DescribeFailure(IReadOnlyList<SketchSolveDiagnostic> diagnostics)
    {
        if (diagnostics.Count > 0 && diagnostics.All(d => d.Reason == SketchSolveDiagnosticReason.RedundantDrivingDimension))
        {
            return "Redundant driving dimension: this value already follows from existing constraints. Remove the duplicate driving dimension.";
        }
        if (diagnostics.Count > 0 && diagnostics.All(d => d.Reason != SketchSolveDiagnosticReason.Unresolved)
            && diagnostics.Any(d => d.Reason == SketchSolveDiagnosticReason.ConflictingDrivingDimension))
        {
            return "Driving dimension conflicts with existing constraints. Change or remove the conflicting dimension.";
        }
        return "Constraints could not be satisfied by the supported solver. Resolve the highlighted unsatisfied constraints or dimensions.";
    }

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

            var reason = SketchDimensionSolverService.GetFailureReason(document, dimension);
            var message = reason switch
            {
                SketchSolveDiagnosticReason.RedundantDrivingDimension => $"Dimension '{dimension.Id}' is redundant: its value already follows from existing constraints.",
                SketchSolveDiagnosticReason.ConflictingDrivingDimension => $"Dimension '{dimension.Id}' conflicts with existing constraints.",
                _ => $"Dimension '{dimension.Id}' is unsatisfied; the supported solver could not resolve it."
            };
            diagnostics.Add(new SketchSolveDiagnostic(
                dimension.Id,
                "dimension",
                dimension.ReferenceKeys,
                message,
                reason));
        }

        return diagnostics;
    }
}

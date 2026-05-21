using DXFER.Core.Documents;

namespace DXFER.Core.Sketching;

public sealed class LegacySketchSolverAdapter : ISketchSolver
{
    public SketchSolveResult Solve(SketchSolveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var constrained = SketchConstraintService.ApplyConstraints(
            request.Document,
            request.Constraints);
        var solved = SketchDimensionSolverService.ApplyDimensions(
            constrained,
            request.Dimensions);
        var affectedDiagnostics = SketchSolveDiagnostics.FromDocument(solved);
        var status = ClassifyStatus(solved, affectedDiagnostics);

        return new SketchSolveResult(
            status,
            solved,
            affectedDiagnostics.Select(diagnostic => diagnostic.Message),
            affectedDiagnostics);
    }

    private static SketchSolveStatus ClassifyStatus(
        DrawingDocument document,
        IReadOnlyList<SketchSolveDiagnostic> affectedDiagnostics)
    {
        if (affectedDiagnostics.Count == 0)
        {
            return SketchSolveStatus.Solved;
        }

        var unsatisfiedDrivingDimensionIds = document.Dimensions
            .Where(dimension => dimension.IsDriving
                && SketchDimensionSolverService.GetDimensionState(document, dimension) == SketchConstraintState.Unsatisfied)
            .Select(dimension => dimension.Id)
            .ToHashSet(StringComparer.Ordinal);
        if (affectedDiagnostics.Any(diagnostic =>
                diagnostic.ItemKind == "dimension"
                && unsatisfiedDrivingDimensionIds.Contains(diagnostic.ItemId)))
        {
            return SketchSolveStatus.OverConstrained;
        }

        if (affectedDiagnostics.Any(diagnostic => diagnostic.ItemKind == "constraint"))
        {
            return SketchSolveStatus.OverConstrained;
        }

        return SketchSolveStatus.Failed;
    }
}

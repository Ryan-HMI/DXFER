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
        var status = affectedDiagnostics.Count == 0
            ? SketchSolveStatus.Solved
            : SketchSolveStatus.Failed;

        return new SketchSolveResult(
            status,
            solved,
            affectedDiagnostics.Select(diagnostic => diagnostic.Message),
            affectedDiagnostics);
    }
}

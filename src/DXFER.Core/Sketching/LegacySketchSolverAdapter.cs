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

        return new SketchSolveResult(SketchSolveStatus.Solved, solved);
    }
}

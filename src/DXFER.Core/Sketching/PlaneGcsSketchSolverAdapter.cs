namespace DXFER.Core.Sketching;

public sealed class PlaneGcsSketchSolverAdapter : ISketchSolver
{
    public SketchSolveResult Solve(SketchSolveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new SketchSolveResult(
            SketchSolveStatus.Unavailable,
            request.Document,
            new[]
            {
                "PlaneGCS solver adapter is isolated behind ISketchSolver and is unavailable until the WASM bridge is wired."
            });
    }
}

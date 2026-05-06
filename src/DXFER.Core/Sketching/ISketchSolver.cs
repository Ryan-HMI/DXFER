namespace DXFER.Core.Sketching;

public interface ISketchSolver
{
    SketchSolveResult Solve(SketchSolveRequest request);
}

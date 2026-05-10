using DXFER.Core.Documents;
using DXFER.Core.Sketching;

namespace DXFER.Blazor.Sketching;

public static class SketchSolveWorkflow
{
    public static SketchSolveWorkflowResult ApplyDimensionEdit(
        DrawingDocument document,
        SketchDimension dimension,
        ISketchSolver solver)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(dimension);
        ArgumentNullException.ThrowIfNull(solver);

        var request = new SketchSolveRequest(
            document,
            document.Constraints,
            UpsertDimension(document.Dimensions, dimension));
        var solveResult = solver.Solve(request);
        if (CanApplyResult(solveResult.Status))
        {
            return new SketchSolveWorkflowResult(
                Applied: true,
                solveResult.Document,
                solveResult,
                FailureMessage: string.Empty);
        }

        return new SketchSolveWorkflowResult(
            Applied: false,
            document,
            solveResult,
            BuildFailureMessage(solveResult));
    }

    private static bool CanApplyResult(SketchSolveStatus status) =>
        status is SketchSolveStatus.Solved or SketchSolveStatus.Underconstrained;

    private static IReadOnlyList<SketchDimension> UpsertDimension(
        IReadOnlyList<SketchDimension> dimensions,
        SketchDimension dimension)
    {
        var nextDimensions = dimensions.ToArray();
        for (var index = 0; index < nextDimensions.Length; index++)
        {
            if (StringComparer.Ordinal.Equals(nextDimensions[index].Id, dimension.Id))
            {
                nextDimensions[index] = dimension;
                return nextDimensions;
            }
        }

        return nextDimensions.Concat(new[] { dimension }).ToArray();
    }

    private static string BuildFailureMessage(SketchSolveResult result)
    {
        var message = $"Sketch solve returned {result.Status}.";
        if (result.Diagnostics.Count == 0)
        {
            return message;
        }

        return $"{message} {string.Join(" ", result.Diagnostics)}";
    }
}

public sealed record SketchSolveWorkflowResult(
    bool Applied,
    DrawingDocument Document,
    SketchSolveResult SolveResult,
    string FailureMessage);

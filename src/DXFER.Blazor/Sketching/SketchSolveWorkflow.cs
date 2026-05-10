using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;

namespace DXFER.Blazor.Sketching;

public static class SketchSolveWorkflow
{
    public static SketchSolveWorkflowResult ApplyDimensionEdit(
        DrawingDocument document,
        SketchDimension dimension,
        ISketchSolver solver) =>
        ApplyDimension(document, dimension, solver);

    public static SketchSolveWorkflowResult ApplyDimension(
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
            UpsertDimension(document.Dimensions, dimension),
            CreateInitialGuesses(document, dimension.ReferenceKeys));
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

    public static SketchSolveWorkflowResult ApplyConstraint(
        DrawingDocument document,
        SketchConstraint constraint,
        ISketchSolver solver) =>
        ApplyConstraints(document, new[] { constraint }, solver);

    public static SketchSolveWorkflowResult ApplyConstraints(
        DrawingDocument document,
        IEnumerable<SketchConstraint> constraints,
        ISketchSolver solver)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(constraints);
        ArgumentNullException.ThrowIfNull(solver);

        var nextConstraints = document.Constraints;
        var referenceKeys = new List<string>();
        foreach (var constraint in constraints)
        {
            ArgumentNullException.ThrowIfNull(constraint);

            nextConstraints = UpsertConstraint(nextConstraints, constraint);
            referenceKeys.AddRange(constraint.ReferenceKeys);
        }

        var request = new SketchSolveRequest(
            document,
            nextConstraints,
            document.Dimensions,
            CreateInitialGuesses(document, referenceKeys));
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

    private static IReadOnlyDictionary<string, SketchInitialGuess> CreateInitialGuesses(
        DrawingDocument document,
        IEnumerable<string> referenceKeys)
    {
        var guesses = new Dictionary<string, SketchInitialGuess>(StringComparer.Ordinal);
        foreach (var referenceKey in referenceKeys)
        {
            if (SketchReferenceResolver.TryGetPoint(document, referenceKey, out var point))
            {
                guesses.TryAdd(referenceKey, SketchInitialGuess.Point(point));
            }

            if (SketchReference.TryParse(referenceKey, out var reference)
                && TryGetEntityGuess(document, reference.EntityId, out var entityGuess))
            {
                guesses.TryAdd(reference.EntityId, entityGuess);
            }
        }

        return guesses;
    }

    private static bool TryGetEntityGuess(
        DrawingDocument document,
        string entityId,
        out SketchInitialGuess guess)
    {
        foreach (var entity in document.Entities)
        {
            if (!StringComparer.Ordinal.Equals(entity.Id.Value, entityId))
            {
                continue;
            }

            return TryCreateEntityGuess(entity, out guess);
        }

        guess = default!;
        return false;
    }

    private static bool TryCreateEntityGuess(DrawingEntity entity, out SketchInitialGuess guess)
    {
        switch (entity)
        {
            case PointEntity point:
                guess = SketchInitialGuess.Point(point.Location);
                return true;
            case LineEntity line:
                guess = SketchInitialGuess.Entity(new[]
                {
                    new KeyValuePair<string, Point2>("start", line.Start),
                    new KeyValuePair<string, Point2>("end", line.End)
                });
                return true;
            case CircleEntity circle:
                guess = SketchInitialGuess.Entity(new[]
                {
                    new KeyValuePair<string, Point2>("center", circle.Center)
                });
                return true;
            case ArcEntity arc:
                guess = SketchInitialGuess.Entity(new[]
                {
                    new KeyValuePair<string, Point2>("center", arc.Center),
                    new KeyValuePair<string, Point2>("start", GetArcPoint(arc, arc.StartAngleDegrees)),
                    new KeyValuePair<string, Point2>("end", GetArcPoint(arc, arc.EndAngleDegrees))
                });
                return true;
            default:
                guess = default!;
                return false;
        }
    }

    private static Point2 GetArcPoint(ArcEntity arc, double angleDegrees)
    {
        var radians = angleDegrees * Math.PI / 180.0;
        return new Point2(
            arc.Center.X + arc.Radius * Math.Cos(radians),
            arc.Center.Y + arc.Radius * Math.Sin(radians));
    }

    private static IReadOnlyList<SketchConstraint> UpsertConstraint(
        IReadOnlyList<SketchConstraint> constraints,
        SketchConstraint constraint)
    {
        var nextConstraints = constraints.ToArray();
        for (var index = 0; index < nextConstraints.Length; index++)
        {
            if (StringComparer.Ordinal.Equals(nextConstraints[index].Id, constraint.Id))
            {
                nextConstraints[index] = constraint;
                return nextConstraints;
            }
        }

        return nextConstraints.Concat(new[] { constraint }).ToArray();
    }

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

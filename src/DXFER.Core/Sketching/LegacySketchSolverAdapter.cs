using DXFER.Core.Documents;
using DXFER.Core.Operations;

namespace DXFER.Core.Sketching;

public sealed class LegacySketchSolverAdapter : ISketchSolver
{
    public SketchSolveResult Solve(SketchSolveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = SolveCore(request);
        if (!DrawingPatternService.TryReconcile(request.Document, result.Document, out var reconciled, out var reason))
            return new SketchSolveResult(SketchSolveStatus.Failed, request.Document, new[] { reason });
        return new SketchSolveResult(result.Status, reconciled, result.Diagnostics, result.AffectedDiagnostics);
    }

    private static SketchSolveResult SolveCore(SketchSolveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = SolveAttempt(request);
        if (result.Status == SketchSolveStatus.Solved || request.Constraints.Count != 1 || request.Dimensions.Count != 0)
            return result;
        var relation = request.Constraints[0];
        if (relation.Kind is not (SketchConstraintKind.Parallel or SketchConstraintKind.Perpendicular or SketchConstraintKind.Collinear)
            || relation.ReferenceKeys.Count != 2 || relation.State == SketchConstraintState.Suppressed)
            return result;

        // Symmetric line relations have two possible driven peers. Retry from the
        // original snapshot, validating the complete system before accepting either.
        var reversed = new SketchConstraint(relation.Id, relation.Kind, relation.ReferenceKeys.Reverse(), relation.State);
        var alternate = SolveAttempt(new SketchSolveRequest(request.Document, new[] { reversed }, request.Dimensions,
            request.FixedReferenceKeys, request.InitialGuesses));
        if (alternate.Status != SketchSolveStatus.Solved) return result;
        var restored = Revalidate(new DrawingDocument(alternate.Document.Entities, alternate.Document.Dimensions,
            alternate.Document.Constraints.Select(c => c.Id == relation.Id
                ? new SketchConstraint(c.Id, c.Kind, relation.ReferenceKeys, c.State) : c), alternate.Document.Metadata));
        var diagnostics = SketchSolveDiagnostics.FromDocument(restored);
        return ClassifyStatus(restored, diagnostics) == SketchSolveStatus.Solved
            ? new SketchSolveResult(SketchSolveStatus.Solved, restored) : result;
    }

    private static SketchSolveResult SolveAttempt(SketchSolveRequest request)
    {
        var constrained = SketchConstraintService.ApplyConstraints(
            request.Document,
            request.Constraints);
        var propagatedEntities = constrained.Entities.ToArray();
        SketchConstraintPropagationService.PropagateFromChanges(
            request.Document.Entities, propagatedEntities, constrained.Constraints,
            SketchFixedReferences.FromConstraints(constrained.Constraints, constrained.Entities),
            // Tangency can translate a free offset family from a dependent curve.
            // Dimension edits must retain their directional source/gap semantics.
            allowOffsetDependentTranslation: request.Constraints.Count == 1 && request.Dimensions.Count == 0
                && request.Constraints.Any(c => c.Kind == SketchConstraintKind.Tangent
                    && c.State != SketchConstraintState.Suppressed
                    && !request.Document.Constraints.Any(existing => existing.Id == c.Id)));
        constrained = new DrawingDocument(propagatedEntities, constrained.Dimensions,
            constrained.Constraints, constrained.Metadata);
        var dimensions = constrained.Dimensions
            .Where(dimension => dimension.IsDriving
                && !request.Dimensions.Any(update => update.Id == dimension.Id))
            .Concat(request.Dimensions);
        var solved = SketchDimensionSolverService.ApplyDimensions(
            constrained,
            dimensions);
        solved = Revalidate(solved);
        var affectedDiagnostics = SketchSolveDiagnostics.FromDocument(solved);
        var status = ClassifyStatus(solved, affectedDiagnostics);

        if (status != SketchSolveStatus.Solved)
        {
            // Retain editable failed intent, but never commit a partially solved shape.
            solved = Revalidate(new DrawingDocument(request.Document.Entities,
                solved.Dimensions, solved.Constraints, request.Document.Metadata));
            var stableDiagnostics = SketchSolveDiagnostics.FromDocument(solved);
            if (stableDiagnostics.Count > 0)
            {
                affectedDiagnostics = stableDiagnostics;
            }
        }

        return new SketchSolveResult(
            status,
            solved,
            affectedDiagnostics.Select(diagnostic => diagnostic.Message),
            affectedDiagnostics);
    }

    private static DrawingDocument Revalidate(DrawingDocument document) =>
        new(document.Entities, document.Dimensions,
            SketchConstraintPropagationService.ValidateConstraints(document, document.Constraints),
            document.Metadata);

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

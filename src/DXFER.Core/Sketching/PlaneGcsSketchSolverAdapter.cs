using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Operations;

namespace DXFER.Core.Sketching;

public sealed class PlaneGcsSketchSolverAdapter : ISketchSolver
{
    private readonly IPlaneGcsRuntime? runtime;

    public PlaneGcsSketchSolverAdapter() { }

    public PlaneGcsSketchSolverAdapter(IPlaneGcsRuntime runtime) =>
        this.runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));

    public SketchSolveResult Solve(SketchSolveRequest request) => Solve(request, null);

    public SketchSolveResult Solve(SketchSolveRequest request, PlaneGcsDragRequest? drag)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (runtime is null)
            return Failure(request, drag, "PlaneGCS runtime is unavailable; no fallback solver is enabled.", SketchSolveStatus.Unavailable);

        try
        {
            if (request.Document.Metadata.Mode == DrawingDocumentMode.ReferenceOnly)
                return Failure(request, drag, "Reference-only document cannot be solved or edited.");
            var effective = new DrawingDocument(request.Document.Entities,
                Merge(request.Document.Dimensions, request.Dimensions, d => d.Id),
                Merge(request.Document.Constraints, request.Constraints, c => c.Id), request.Document.Metadata);
            var fixedKeys = GetFixedKeys(request, effective);
            if (!PlaneGcsValidation.ValidateInput(effective, fixedKeys, request.InitialGuesses, drag, out var reason))
                return Failure(request, drag, reason);
            var document = PlaneGcsDocumentMapper.Document(effective);
            foreach (var entity in document.Entities) PlaneGcsDocumentMapper.CheckFinite(entity);
            var attemptedDrivingIds = (drag is null
                ? request.Dimensions.Where(update => update.IsDriving && !request.Document.Dimensions.Any(existing =>
                    SameDrivingIntent(existing, update))).Select(d => d.Id).ToArray()
                : Array.Empty<string>())
                .Concat(PlaneGcsDiagnostics.PendingDrivingDimensionIds(request.Document))
                .Where(id => effective.Dimensions.Any(d => d.Id == id && d.IsDriving))
                .Distinct(StringComparer.Ordinal).ToArray();
            var wire = new PlaneGcsRequest(document,
                request.Constraints.Select(PlaneGcsDocumentMapper.Constraint).ToArray(),
                request.Dimensions.Select(d => PlaneGcsDocumentMapper.Dimension(effective, d)).ToArray(),
                fixedKeys, request.InitialGuesses.Select(g =>
                    new PlaneGcsInitialGuess(g.ReferenceKey, PlaneGcsDocumentMapper.Point(g.Point))).ToArray(), drag,
                attemptedDrivingIds);
            PlaneGcsResponse response;
            try
            {
                response = runtime.Solve(wire);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                return Failure(request, drag, $"PlaneGCS runtime is unavailable: {exception.Message}", SketchSolveStatus.Unavailable);
            }
            if (response is null || response.Engine != "PlaneGCS")
                return Failure(request, drag, "Missing or invalid PlaneGCS engine response.");
            if (response.Status != "solved")
            {
                var prefix = response.Status == "unsupported" ? "PlaneGCS does not support this request." : "PlaneGCS could not solve this request.";
                return Failure(request, drag, JoinDiagnostics(prefix, response.Diagnostics), engineStatus: response.Status);
            }
            if (attemptedDrivingIds.Length > 0)
            {
                if (response.RedundantDrivingDimensionIds is null)
                    return Failure(request, drag, "PlaneGCS did not report driving-dimension redundancy by intent ID.");
                var redundant = attemptedDrivingIds.Intersect(response.RedundantDrivingDimensionIds, StringComparer.Ordinal).ToArray();
                if (redundant.Length > 0)
                    return Failure(request, drag, $"PlaneGCS rejected redundant driving dimension intent: {string.Join(", ", redundant)}.",
                        engineStatus: response.Status, redundantDimensionIds: redundant);
            }
            if (response.Entities is null)
                return Failure(request, drag, "PlaneGCS returned no geometry.");

            var entities = PlaneGcsDocumentMapper.ReadEntities(request.Document, response.Entities);
            var candidate = new DrawingDocument(entities, effective.Dimensions, effective.Constraints, request.Document.Metadata);
            if (!PlaneGcsValidation.ValidateResult(request.Document, candidate, fixedKeys, drag, out reason))
                return Failure(request, drag, reason);

            // Only validation and measurement are performed here. No legacy geometry
            // application, propagation, or pattern reconciliation may repair a result.
            var constraints = candidate.Constraints.Select(c => SketchConstraintService.ValidateConstraint(candidate, c)).ToArray();
            candidate = new DrawingDocument(candidate.Entities, candidate.Dimensions, constraints, candidate.Metadata);
            candidate = SketchDimensionModeService.RefreshMeasurements(candidate);
            if (drag is not null)
                candidate = SketchDimensionAnchorService.FollowDimensionAnchors(request.Document, candidate);
            if (!DrawingLayerService.PreservesProtectedGeometry(request.Document, candidate))
                return Failure(request, drag, "PlaneGCS would change hidden or locked geometry or intent.");
            candidate = PlaneGcsDiagnostics.RecordSolved(candidate, response.RedundantDrivingDimensionIds);
            return new SketchSolveResult(SketchSolveStatus.Solved, candidate, response.Diagnostics ?? Array.Empty<string>());
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return Failure(request, drag, $"PlaneGCS request failed: {exception.Message}");
        }
    }

    public bool TryApplyDrag(DrawingDocument document, string key, Point2 start, Point2 end, bool constrain,
        out DrawingDocument nextDocument, out string status, bool automaticSnapConstraints = false,
        PlaneGcsConstraint? temporarySnapIntent = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        SketchConstraint? snap = null;
        if (temporarySnapIntent is not null
            && !PlaneGcsDragIntentService.TryCreate(document, key, temporarySnapIntent, out snap, out status))
        {
            nextDocument = document;
            return false;
        }
        // The snap is a hard equation during both preview and commit. "Temporary"
        // describes its lifetime, not a weak PlaneGCS gesture goal.
        var result = Solve(new SketchSolveRequest(document, snap is null ? [] : [snap], []),
            new PlaneGcsDragRequest(key, PlaneGcsDocumentMapper.Point(start), PlaneGcsDocumentMapper.Point(end),
                constrain));
        nextDocument = result.Document;
        status = result.Status == SketchSolveStatus.Solved ? "Moved geometry with PlaneGCS."
            : string.Join(" ", result.Diagnostics);
        if (result.Status == SketchSolveStatus.Solved && snap is not null)
        {
            if (!automaticSnapConstraints)
                nextDocument = PlaneGcsDiagnostics.RemoveSolvedConstraints(nextDocument, [snap.Id]);
            else
                status += $" Added {snap.Kind} constraint.";
        }
        else if (result.Status == SketchSolveStatus.Solved && automaticSnapConstraints && temporarySnapIntent is null)
            nextDocument = SketchDragSnapService.AddPersistentConstraint(document, nextDocument, key, ref status);
        return result.Status == SketchSolveStatus.Solved;
    }

    private static IReadOnlyList<T> Merge<T>(IReadOnlyList<T> baseline, IReadOnlyList<T> updates, Func<T, string> id)
    {
        if (baseline.Select(id).Distinct(StringComparer.Ordinal).Count() != baseline.Count
            || updates.Select(id).Distinct(StringComparer.Ordinal).Count() != updates.Count)
            throw new InvalidOperationException("Duplicate sketch intent IDs are not supported.");
        var replacements = updates.ToDictionary(id, StringComparer.Ordinal);
        var originalIds = baseline.Select(id).ToHashSet(StringComparer.Ordinal);
        return baseline.Select(item => replacements.GetValueOrDefault(id(item), item))
            .Concat(updates.Where(item => !originalIds.Contains(id(item)))).ToArray();
    }

    private static string[] GetFixedKeys(SketchSolveRequest request, DrawingDocument effective)
    {
        var keys = request.FixedReferenceKeys
            .Concat(effective.Constraints
                .Where(c => c.Kind == SketchConstraintKind.Fix && c.State != SketchConstraintState.Suppressed)
                .SelectMany(c => c.ReferenceKeys))
            .Concat(effective.Entities.Where(e => !DrawingLayerService.CanEdit(effective, e.Id.Value)).Select(e => e.Id.Value))
            .Append(SketchReference.OriginKey);
        return keys.Select(key => SketchReference.TryNormalize(key, out var normalized) ? normalized : key)
            .Distinct(StringComparer.Ordinal).ToArray();
    }

    private static string JoinDiagnostics(string prefix, IReadOnlyList<string>? diagnostics) =>
        diagnostics is { Count: > 0 } ? $"{prefix} {string.Join(" ", diagnostics)}" : prefix;

    private static SketchSolveResult Failure(SketchSolveRequest request, PlaneGcsDragRequest? drag, string reason,
        SketchSolveStatus status = SketchSolveStatus.Failed, string? engineStatus = null,
        IReadOnlyList<string>? redundantDimensionIds = null)
    {
        var baseline = request.Document;
        var unchanged = new SketchSolveResult(status, baseline, new[] { reason });
        if (drag is not null || baseline.Metadata.Mode == DrawingDocumentMode.ReferenceOnly)
            return unchanged;
        try
        {
            if (request.Constraints.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() != request.Constraints.Count
                || request.Dimensions.Select(d => d.Id).Distinct(StringComparer.Ordinal).Count() != request.Dimensions.Count)
                return unchanged;
            // A retry of existing intent is not a new edit. Only explicit additions
            // or replacements belong in failed-intent history.
            var constraintEdits = request.Constraints.Where(update => !baseline.Constraints.Any(existing =>
                SameConstraint(existing, update))).ToArray();
            var dimensionEdits = request.Dimensions.Where(update => !baseline.Dimensions.Any(existing =>
                SameDimension(existing, update))).ToArray();
            if (constraintEdits.Length == 0 && dimensionEdits.Length == 0) return unchanged;
            bool ProtectedReference(string key) => SketchReference.TryParse(key, out var reference)
                && !reference.IsOrigin && baseline.Entities.Any(e => e.Id.Value == reference.EntityId)
                && !DrawingLayerService.CanEdit(baseline, reference.EntityId);
            if (constraintEdits.Any(update => baseline.Constraints.Any(existing => existing.Id == update.Id
                    && existing.ReferenceKeys.Any(ProtectedReference)))
                || dimensionEdits.Any(update => baseline.Dimensions.Any(existing => existing.Id == update.Id
                    && existing.ReferenceKeys.Any(ProtectedReference))))
                return unchanged;
            if (constraintEdits.Any(c => !Enum.IsDefined(c.Kind) || !Enum.IsDefined(c.State)
                    || c.ReferenceKeys.Count == 0 || c.ReferenceKeys.Any(string.IsNullOrWhiteSpace))
                || dimensionEdits.Any(d => !Enum.IsDefined(d.Kind) || !double.IsFinite(d.Value)
                    || d.Anchor is { } anchor && (!double.IsFinite(anchor.X) || !double.IsFinite(anchor.Y))
                    || d.ReferenceKeys.Count == 0 || d.ReferenceKeys.Any(string.IsNullOrWhiteSpace)))
                return unchanged;

            var failedConstraints = constraintEdits.Select(c => c.State == SketchConstraintState.Suppressed ? c
                : new SketchConstraint(c.Id, c.Kind, c.ReferenceKeys, SketchConstraintState.Unsatisfied)).ToArray();
            var retained = new DrawingDocument(baseline.Entities,
                Merge(baseline.Dimensions, dimensionEdits, d => d.Id),
                Merge(baseline.Constraints, failedConstraints, c => c.Id), baseline.Metadata);
            if (!DrawingLayerService.PreservesProtectedGeometry(baseline, retained)) return unchanged;
            var affected = failedConstraints.Where(c => c.State != SketchConstraintState.Suppressed)
                .Select(c => new SketchSolveDiagnostic(c.Id, "constraint", c.ReferenceKeys, reason))
                .Concat(dimensionEdits.Select(d => new SketchSolveDiagnostic(d.Id, "dimension", d.ReferenceKeys, reason,
                    redundantDimensionIds?.Contains(d.Id) == true
                        ? SketchSolveDiagnosticReason.RedundantDrivingDimension : SketchSolveDiagnosticReason.Unresolved))).ToArray();
            retained = PlaneGcsDiagnostics.RecordFailed(baseline, retained, affected, status, engineStatus);
            return new SketchSolveResult(status, retained, new[] { reason }, affected);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Malformed requests must not replace a valid document with invalid intent.
            return unchanged;
        }
    }

    private static bool SameConstraint(SketchConstraint first, SketchConstraint second) =>
        first.Id == second.Id && first.Kind == second.Kind && first.State == second.State
        && first.ReferenceKeys.SequenceEqual(second.ReferenceKeys);

    private static bool SameDimension(SketchDimension first, SketchDimension second) =>
        first.Id == second.Id && first.Kind == second.Kind && first.Value == second.Value
        && first.Anchor == second.Anchor && first.IsDriving == second.IsDriving
        && first.ReferenceKeys.SequenceEqual(second.ReferenceKeys);

    // Label placement is not a driving equation edit.
    private static bool SameDrivingIntent(SketchDimension first, SketchDimension second) =>
        first.Id == second.Id && first.Kind == second.Kind && first.Value == second.Value
        && first.IsDriving == second.IsDriving && first.ReferenceKeys.SequenceEqual(second.ReferenceKeys);
}

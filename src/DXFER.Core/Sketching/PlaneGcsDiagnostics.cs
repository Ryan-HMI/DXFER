using DXFER.Core.Documents;

namespace DXFER.Core.Sketching;

/// <summary>Pure display/history state from persisted native observations and geometric residuals.</summary>
public static class PlaneGcsDiagnostics
{
    public static SketchConstraintState GetDimensionState(DrawingDocument document, SketchDimension dimension) =>
        Read(document).GetDimensionState(dimension);

    public static IReadOnlyList<SketchSolveDiagnostic> FromDocument(DrawingDocument document) => Read(document).Diagnostics;

    // Create once per render/history operation, rather than hashing once per label.
    public static PlaneGcsDiagnosticView Read(DrawingDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var evidence = document.SolveEvidence;
        var current = false;
        try
        {
            current = evidence is not null && IsWellFormed(evidence)
                && evidence.Version == PlaneGcsSolveEvidence.CurrentVersion
                && evidence.SnapshotSignature == PlaneGcsSemanticSnapshot.Document(document);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException) { }
        return new PlaneGcsDiagnosticView(document, current ? evidence : null);
    }

    public static bool IsWellFormed(PlaneGcsSolveEvidence evidence) =>
        evidence.Engine == "PlaneGCS" && evidence.Version > 0 && Enum.IsDefined(evidence.Status)
        && Signature(evidence.SnapshotSignature)
        && evidence.EngineStatus is null or "solved" or "failed" or "unsupported"
        && evidence.Intents.Count <= 100000
        && evidence.Intents.All(item => item is not null && !string.IsNullOrWhiteSpace(item.ItemId)
            && item.ItemId.Length <= 4096 && item.ItemKind is "constraint" or "dimension"
            && Signature(item.IntentSignature) && Enum.IsDefined(item.State) && Enum.IsDefined(item.Reason)
            && item.Message is not null && item.Message.Length <= 8192
            && (item.ItemKind != "dimension" || item.State != SketchConstraintState.Suppressed))
        && evidence.Intents.Select(item => (item.ItemKind, item.ItemId)).Distinct().Count() == evidence.Intents.Count;

    // Retain the engine marker, not authority over changed semantics. Read checks
    // the complete semantic signature before exposing any carried intent state.
    public static DrawingDocument CarryEvidence(DrawingDocument original, DrawingDocument candidate) =>
        candidate.SolveEvidence is not null || original.SolveEvidence is null ? candidate
            : WithEvidence(candidate, original.SolveEvidence);

    public static DrawingDocument RemoveSolvedConstraints(DrawingDocument document, IEnumerable<string> ids)
    {
        var removed = ids.ToHashSet(StringComparer.Ordinal);
        var constraints = document.Constraints.Where(c => !removed.Contains(c.Id)).ToArray();
        if (constraints.Length == document.Constraints.Count) return document;
        var candidate = new DrawingDocument(document.Entities, document.Dimensions, constraints, document.Metadata, document.SolveEvidence);
        if (!Read(document).HasCurrentEvidence || document.SolveEvidence!.Status != SketchSolveStatus.Solved)
            return candidate;
        // Removing solved equations cannot invalidate satisfaction or introduce
        // driving redundancy. Geometry and all remaining intent are unchanged.
        return WithEvidence(candidate, new("PlaneGCS", PlaneGcsSolveEvidence.CurrentVersion,
            PlaneGcsSemanticSnapshot.Document(candidate), SketchSolveStatus.Solved, document.SolveEvidence.EngineStatus,
            document.SolveEvidence.Intents.Where(i => i.ItemKind != "constraint" || !removed.Contains(i.ItemId)).ToArray()));
    }

    internal static IEnumerable<string> PendingDrivingDimensionIds(DrawingDocument document)
    {
        var evidence = document.SolveEvidence;
        if (evidence is null || !IsWellFormed(evidence) || evidence.Version != PlaneGcsSolveEvidence.CurrentVersion)
            return Array.Empty<string>();
        // Even stale geometry must not turn a previously rejected, unchanged
        // driving intent into harmless pre-existing redundancy on the next solve.
        return document.Dimensions.Where(d => d.IsDriving && evidence.Intents.Any(i => i.ItemKind == "dimension"
                && i.ItemId == d.Id && i.State == SketchConstraintState.Unsatisfied
                && i.IntentSignature == PlaneGcsSemanticSnapshot.Intent(d)))
            .Select(d => d.Id).ToArray();
    }

    internal static DrawingDocument RecordSolved(DrawingDocument document, IReadOnlyList<string>? redundantDrivingIds) =>
        WithEvidence(document, Create(document, SketchSolveStatus.Solved, "solved",
            document.Constraints.Select(c => new PlaneGcsIntentEvidence(c.Id, "constraint", PlaneGcsSemanticSnapshot.Intent(c),
                c.State == SketchConstraintState.Suppressed ? c.State : SketchConstraintState.Satisfied,
                SketchSolveDiagnosticReason.Unresolved, string.Empty))
            .Concat(document.Dimensions.Select(d =>
            {
                var reported = d.IsDriving && redundantDrivingIds?.Contains(d.Id) == true;
                var missing = d.IsDriving && redundantDrivingIds is null;
                // Raw native tags select one nonunique redundant basis. Preserve
                // that observation without attributing a rejected edit to it.
                return new PlaneGcsIntentEvidence(d.Id, "dimension", PlaneGcsSemanticSnapshot.Intent(d),
                    reported || missing ? SketchConstraintState.Unknown : SketchConstraintState.Satisfied,
                    reported ? SketchSolveDiagnosticReason.RedundantDrivingDimension : SketchSolveDiagnosticReason.Unresolved,
                    reported ? $"PlaneGCS reported dimension '{d.Id}' in a redundant equation basis; no rejected edit is attributed."
                        : missing ? $"PlaneGCS did not report native driving-dimension rank for '{d.Id}'." : string.Empty);
            }))));

    internal static DrawingDocument RecordFailed(DrawingDocument baseline, DrawingDocument retained,
        IReadOnlyList<SketchSolveDiagnostic> affected, SketchSolveStatus status, string? engineStatus)
    {
        var prior = Read(baseline);
        var entries = new List<PlaneGcsIntentEvidence>();
        foreach (var constraint in retained.Constraints)
        {
            var diagnostic = affected.FirstOrDefault(d => d.ItemKind == "constraint" && d.ItemId == constraint.Id);
            var signature = PlaneGcsSemanticSnapshot.Intent(constraint);
            var before = baseline.Constraints.FirstOrDefault(c => c.Id == constraint.Id
                && PlaneGcsSemanticSnapshot.Intent(c) == signature);
            var state = constraint.State == SketchConstraintState.Suppressed ? SketchConstraintState.Suppressed
                : diagnostic is not null ? SketchConstraintState.Unsatisfied
                : before is not null ? prior.GetConstraintState(before) : SketchConstraintState.Unknown;
            entries.Add(Entry(constraint.Id, "constraint", signature, state, diagnostic ?? prior.FindDiagnostic("constraint", constraint.Id)));
        }
        foreach (var dimension in retained.Dimensions)
        {
            var diagnostic = affected.FirstOrDefault(d => d.ItemKind == "dimension" && d.ItemId == dimension.Id);
            var signature = PlaneGcsSemanticSnapshot.Intent(dimension);
            var before = baseline.Dimensions.FirstOrDefault(d => d.Id == dimension.Id
                && PlaneGcsSemanticSnapshot.Intent(d) == signature);
            var state = diagnostic is not null ? SketchConstraintState.Unsatisfied
                : before is not null ? prior.GetDimensionState(before) : SketchConstraintState.Unknown;
            entries.Add(Entry(dimension.Id, "dimension", signature, state, diagnostic ?? prior.FindDiagnostic("dimension", dimension.Id)));
        }
        return WithEvidence(retained, Create(retained, status, engineStatus, entries));
    }

    private static PlaneGcsIntentEvidence Entry(string id, string kind, string signature, SketchConstraintState state,
        SketchSolveDiagnostic? diagnostic) =>
        new(id, kind, signature, state, diagnostic?.Reason ?? SketchSolveDiagnosticReason.Unresolved,
            diagnostic?.Message is { } message ? message[..Math.Min(message.Length, 8192)] : string.Empty);

    private static PlaneGcsSolveEvidence Create(DrawingDocument document, SketchSolveStatus status, string? engineStatus,
        IEnumerable<PlaneGcsIntentEvidence> intents) =>
        new("PlaneGCS", PlaneGcsSolveEvidence.CurrentVersion, PlaneGcsSemanticSnapshot.Document(document), status,
            engineStatus is "solved" or "failed" or "unsupported" ? engineStatus : null, intents.ToArray());

    private static DrawingDocument WithEvidence(DrawingDocument document, PlaneGcsSolveEvidence evidence) =>
        new(document.Entities, document.Dimensions, document.Constraints, document.Metadata, evidence);

    private static bool Signature(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
}

public sealed class PlaneGcsDiagnosticView
{
    private readonly Dictionary<string, (string Signature, SketchConstraintState State)> dimensions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Signature, SketchConstraintState State)> constraints = new(StringComparer.Ordinal);
    private readonly IReadOnlyList<SketchSolveDiagnostic> diagnostics;
    private readonly IReadOnlyList<SketchSolveDiagnostic> observations;

    internal PlaneGcsDiagnosticView(DrawingDocument document, PlaneGcsSolveEvidence? evidence)
    {
        HasCurrentEvidence = evidence is not null;
        var entries = evidence?.Intents.ToDictionary(i => (i.ItemKind, i.ItemId));
        var failures = new List<SketchSolveDiagnostic>();
        var unvalidated = new List<SketchSolveDiagnostic>();
        foreach (var dimension in document.Dimensions)
        {
            var signature = PlaneGcsSemanticSnapshot.Intent(dimension);
            var entry = entries?.GetValueOrDefault(("dimension", dimension.Id));
            var state = entry?.IntentSignature == signature ? entry.State : SketchConstraintState.Unknown;
            if (state == SketchConstraintState.Satisfied && !PlaneGcsValidation.DimensionResidualSatisfied(document, dimension))
                state = SketchConstraintState.Unsatisfied;
            dimensions[dimension.Id] = (signature, state);
            if (state == SketchConstraintState.Unsatisfied)
                failures.Add(Diagnostic(dimension.Id, "dimension", dimension.ReferenceKeys, entry));
            else if (state == SketchConstraintState.Unknown && !string.IsNullOrWhiteSpace(entry?.Message))
                unvalidated.Add(Diagnostic(dimension.Id, "dimension", dimension.ReferenceKeys, entry));
        }
        foreach (var constraint in document.Constraints)
        {
            var signature = PlaneGcsSemanticSnapshot.Intent(constraint);
            var entry = entries?.GetValueOrDefault(("constraint", constraint.Id));
            var state = constraint.State == SketchConstraintState.Suppressed ? constraint.State
                : entry?.IntentSignature == signature ? entry.State : SketchConstraintState.Unknown;
            if (state == SketchConstraintState.Satisfied)
                state = SketchConstraintService.ValidateConstraint(document, constraint).State;
            constraints[constraint.Id] = (signature, state);
            if (state == SketchConstraintState.Unsatisfied)
                failures.Add(Diagnostic(constraint.Id, "constraint", constraint.ReferenceKeys, entry));
        }
        diagnostics = failures.AsReadOnly();
        observations = unvalidated.AsReadOnly();
    }

    public bool HasCurrentEvidence { get; }
    public bool HasUnvalidatedIntent => dimensions.Values.Concat(constraints.Values).Any(item => item.State == SketchConstraintState.Unknown);
    public IReadOnlyList<SketchSolveDiagnostic> Diagnostics => diagnostics;
    public IReadOnlyList<SketchSolveDiagnostic> Observations => observations;

    public SketchConstraintState GetDimensionState(SketchDimension dimension) =>
        dimensions.TryGetValue(dimension.Id, out var item) && item.Signature == PlaneGcsSemanticSnapshot.Intent(dimension)
            ? item.State : SketchConstraintState.Unknown;

    public SketchConstraintState GetConstraintState(SketchConstraint constraint) =>
        constraints.TryGetValue(constraint.Id, out var item) && item.Signature == PlaneGcsSemanticSnapshot.Intent(constraint)
            ? item.State : SketchConstraintState.Unknown;

    internal SketchSolveDiagnostic? FindDiagnostic(string kind, string id) =>
        diagnostics.FirstOrDefault(d => d.ItemKind == kind && d.ItemId == id);

    private static SketchSolveDiagnostic Diagnostic(string id, string kind, IEnumerable<string> references, PlaneGcsIntentEvidence? entry) =>
        new(id, kind, references,
            string.IsNullOrWhiteSpace(entry?.Message) ? $"PlaneGCS {kind} '{id}' is unsatisfied." : entry.Message,
            entry?.Reason ?? SketchSolveDiagnosticReason.Unresolved);
}

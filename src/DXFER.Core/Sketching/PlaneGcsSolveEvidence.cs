namespace DXFER.Core.Sketching;

/// <summary>Persisted solve observations, not a substitute for validating a subsequent edit.</summary>
public sealed class PlaneGcsSolveEvidence
{
    public const int CurrentVersion = 1;

    public PlaneGcsSolveEvidence(string engine, int version, string snapshotSignature, SketchSolveStatus status,
        string? engineStatus, IReadOnlyList<PlaneGcsIntentEvidence> intents, string? lastAcceptedSnapshotSignature = null)
    {
        Engine = engine;
        Version = version;
        SnapshotSignature = snapshotSignature;
        Status = status;
        EngineStatus = engineStatus;
        Intents = Array.AsReadOnly((intents ?? Array.Empty<PlaneGcsIntentEvidence>()).ToArray());
        LastAcceptedSnapshotSignature = lastAcceptedSnapshotSignature;
    }

    public string Engine { get; }
    public int Version { get; }
    public string SnapshotSignature { get; }
    public SketchSolveStatus Status { get; }
    // Null means adapter/preflight/transport failure, not a completed native solve.
    public string? EngineStatus { get; }
    public IReadOnlyList<PlaneGcsIntentEvidence> Intents { get; }
    // Failed edits retain geometry but replace intent. Keep the full accepted
    // snapshot identity so an exact restoration is not treated as new intent.
    public string? LastAcceptedSnapshotSignature { get; }
}

public sealed record PlaneGcsIntentEvidence(
    string ItemId,
    string ItemKind,
    string IntentSignature,
    SketchConstraintState State,
    SketchSolveDiagnosticReason Reason,
    string Message);

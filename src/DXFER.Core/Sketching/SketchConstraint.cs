using System.Collections.ObjectModel;

namespace DXFER.Core.Sketching;

public sealed record SketchConstraint
{
    public SketchConstraint(
        string id,
        SketchConstraintKind kind,
        IEnumerable<string> referenceKeys,
        SketchConstraintState state = SketchConstraintState.Unknown)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(referenceKeys);

        Id = id;
        Kind = kind;
        ReferenceKeys = Array.AsReadOnly(referenceKeys.ToArray());
        State = state;
    }

    public string Id { get; }

    public SketchConstraintKind Kind { get; }

    public ReadOnlyCollection<string> ReferenceKeys { get; }

    public SketchConstraintState State { get; }
}

using System.Collections.ObjectModel;
using DXFER.Core.Documents;

namespace DXFER.Core.Sketching;

public sealed class SketchSolveRequest
{
    public SketchSolveRequest(
        DrawingDocument document,
        IEnumerable<SketchConstraint> constraints,
        IEnumerable<SketchDimension> dimensions,
        IEnumerable<string>? fixedReferenceKeys = null,
        IEnumerable<SketchSolveInitialGuess>? initialGuesses = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(constraints);
        ArgumentNullException.ThrowIfNull(dimensions);

        var constraintArray = constraints.ToArray();
        var dimensionArray = dimensions.ToArray();

        Document = document;
        Constraints = Array.AsReadOnly(constraintArray);
        Dimensions = Array.AsReadOnly(dimensionArray);
        FixedReferenceKeys = Array.AsReadOnly(
            ResolveFixedReferenceKeys(fixedReferenceKeys, constraintArray).ToArray());
        InitialGuesses = Array.AsReadOnly((initialGuesses ?? Array.Empty<SketchSolveInitialGuess>()).ToArray());
    }

    public DrawingDocument Document { get; }

    public IReadOnlyList<DrawingEntity> Entities => Document.Entities;

    public ReadOnlyCollection<SketchConstraint> Constraints { get; }

    public ReadOnlyCollection<SketchDimension> Dimensions { get; }

    public ReadOnlyCollection<string> FixedReferenceKeys { get; }

    public ReadOnlyCollection<SketchSolveInitialGuess> InitialGuesses { get; }

    public static SketchSolveRequest FromDocument(DrawingDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return new SketchSolveRequest(document, document.Constraints, document.Dimensions);
    }

    private static IEnumerable<string> ResolveFixedReferenceKeys(
        IEnumerable<string>? fixedReferenceKeys,
        IReadOnlyList<SketchConstraint> constraints)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var keys = fixedReferenceKeys ?? constraints
            .Where(constraint => constraint.Kind == SketchConstraintKind.Fix
                && constraint.State != SketchConstraintState.Suppressed)
            .SelectMany(constraint => constraint.ReferenceKeys);

        foreach (var key in keys)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            var normalized = SketchReference.TryNormalize(key, out var parsed)
                ? parsed
                : key.Trim();
            if (seen.Add(normalized))
            {
                yield return normalized;
            }
        }
    }
}

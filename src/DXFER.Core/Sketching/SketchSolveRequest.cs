using System.Collections.ObjectModel;
using DXFER.Core.Documents;

namespace DXFER.Core.Sketching;

public sealed class SketchSolveRequest
{
    public SketchSolveRequest(
        DrawingDocument document,
        IEnumerable<SketchConstraint> constraints,
        IEnumerable<SketchDimension> dimensions,
        IReadOnlyDictionary<string, SketchInitialGuess>? initialGuesses = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(constraints);
        ArgumentNullException.ThrowIfNull(dimensions);

        Document = document;
        Constraints = Array.AsReadOnly(constraints.ToArray());
        Dimensions = Array.AsReadOnly(dimensions.ToArray());
        InitialGuesses = new ReadOnlyDictionary<string, SketchInitialGuess>(
            CopyInitialGuesses(initialGuesses));
    }

    public DrawingDocument Document { get; }

    public IReadOnlyList<DrawingEntity> Entities => Document.Entities;

    public ReadOnlyCollection<SketchConstraint> Constraints { get; }

    public ReadOnlyCollection<SketchDimension> Dimensions { get; }

    public ReadOnlyDictionary<string, SketchInitialGuess> InitialGuesses { get; }

    public static SketchSolveRequest FromDocument(DrawingDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return new SketchSolveRequest(document, document.Constraints, document.Dimensions);
    }

    private static Dictionary<string, SketchInitialGuess> CopyInitialGuesses(
        IReadOnlyDictionary<string, SketchInitialGuess>? initialGuesses)
    {
        var copied = new Dictionary<string, SketchInitialGuess>(StringComparer.Ordinal);
        if (initialGuesses is null)
        {
            return copied;
        }

        foreach (var guess in initialGuesses)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(guess.Key);
            ArgumentNullException.ThrowIfNull(guess.Value);

            copied.Add(guess.Key, guess.Value);
        }

        return copied;
    }
}

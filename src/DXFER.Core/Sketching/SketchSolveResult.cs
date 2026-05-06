using System.Collections.ObjectModel;
using DXFER.Core.Documents;

namespace DXFER.Core.Sketching;

public sealed class SketchSolveResult
{
    public SketchSolveResult(
        SketchSolveStatus status,
        DrawingDocument document,
        IEnumerable<string>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        Status = status;
        Document = document;
        Diagnostics = Array.AsReadOnly((diagnostics ?? Array.Empty<string>()).ToArray());
    }

    public SketchSolveStatus Status { get; }

    public DrawingDocument Document { get; }

    public ReadOnlyCollection<string> Diagnostics { get; }
}

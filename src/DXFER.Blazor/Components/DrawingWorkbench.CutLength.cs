using System.Globalization;
using DXFER.Core.Documents;
using DXFER.Core.Operations;

namespace DXFER.Blazor.Components;

public partial class DrawingWorkbench
{
    private DrawingDocument? _cutLengthDocument;
    private CutPathLengthResult _cutLength = new(null, null);

    private CutPathLengthResult CurrentCutLength
    {
        get
        {
            if (!ReferenceEquals(_cutLengthDocument, _document))
            {
                _cutLengthDocument = _document;
                _cutLength = CutPathLengthService.Calculate(_document);
            }
            return _cutLength;
        }
    }

    private string CutLengthText => CurrentCutLength.CutLengthInches is { } length
        ? length.ToString("0.######", CultureInfo.InvariantCulture) + " in"
        : "Review";
}

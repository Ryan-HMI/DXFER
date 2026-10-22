using DXFER.Core.Sketching;

namespace DXFER.Blazor.Components;

public partial class DrawingWorkbench
{
    private void OnToolFeedbackRequested(string message)
    {
        if (_activeTool != WorkbenchTool.Slot) return;
        _status = message;
        _ = InvokeAsync(StateHasChanged);
    }

    private void OnLineSlotCommitRequested(string sourceKey, double width, bool keyedWidth)
    {
        if (!CanEditPlaneGcsDependencies(_document)) return;
        try
        {
            var candidate = LineSlotCreation.Create(_document, sourceKey, width, keyedWidth,
                CreateEntityId, CreateConstraintId, CreateDimensionId);
            // Creating a slot may add relationships, but cannot reposition existing geometry.
            var result = SketchSolver.Solve(new SketchSolveRequest(candidate, [], [],
                fixedReferenceKeys: _document.Entities.Select(entity => entity.Id.Value)));
            if (!SolveSucceeded(result))
            {
                ReportSolveFailure(result);
                return;
            }
            if (!ApplyDocumentChange(result.Document, "Slot added.")) return;
            _activeTool = null;
            ResetSelection();
            _focusCanvasAfterRender = true;
            _ = InvokeAsync(StateHasChanged);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            ReportSolveFailure($"{exception.Message} Geometry unchanged.");
        }
    }
}

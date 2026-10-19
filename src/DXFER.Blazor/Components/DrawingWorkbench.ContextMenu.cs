using DXFER.Core.Documents;
using DXFER.Core.Operations;
using DXFER.Core.Sketching;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace DXFER.Blazor.Components;

public partial class DrawingWorkbench
{
    private string? _contextKey;
    private string[] _contextCornerSelection = [];
    private string[] _contextEntities = [];
    private DrawingDocument? _contextDocument;
    private bool _contextOpen, _contextFocus;
    private double _contextX, _contextY;
    private ElementReference _contextMenu;
    private string _contextLayer = "";
    private SketchDimension? ContextDimension => _contextKey?.StartsWith("dimension:", StringComparison.Ordinal) == true
        ? _document.Dimensions.FirstOrDefault(d => d.Id == _contextKey[10..]) : null;
    private bool ContextEditable => !LayersReadOnly && ReferenceEquals(_contextDocument, _document)
        && _contextEntities.All(id => DrawingLayerService.CanEdit(_document, id));
    private bool ContextCorner => DrawingModifyService.CanModifySelectedCorner(_document, _contextCornerSelection);
    private string ContextStyle => FormattableString.Invariant(
        $"left:clamp(8px,{_contextX}px,calc(100vw - 248px));top:clamp(8px,{_contextY}px,calc(100vh - 340px));");

    private async Task OpenContextMenu(string? key, string[] selection, double x, double y)
    {
        if (!EnableFullEditing) return;
        _contextKey = key;
        _contextDocument = _document;
        _contextCornerSelection = key is not null && DrawingModifyService.CanModifySelectedCorner(_document, [key])
            ? [key] : selection;
        _contextEntities = selection.Select(k => TryGetEntityIdForOperationTarget(k, out var id) ? id : "")
            .Where(id => _document.Entities.Any(e => e.Id.Value == id)).Distinct(StringComparer.Ordinal).ToArray();
        if (ContextDimension is { } dimension)
            _contextEntities = dimension.ReferenceKeys.Select(k => SketchReference.TryParse(k, out var r) ? r.EntityId : "")
                .Where(id => id != SketchReference.OriginEntityId && id.Length > 0).Distinct().ToArray();
        _contextX = x; _contextY = y;
        _contextLayer = "";
        _contextOpen = true; _contextFocus = true;
        await InvokeAsync(StateHasChanged);
    }

    private void CloseContextMenu()
    {
        _contextOpen = false;
        _focusCanvasAfterRender = true;
    }

    private void ContextKeyDown(KeyboardEventArgs args)
    {
        if (args.Key == "Escape") CloseContextMenu();
    }

    private async Task ContextCommand(WorkbenchCommandId command, bool corner = false)
    {
        if (!ContextEditable || _canvas is null) { CloseContextMenu(); return; }
        var keys = corner ? _contextCornerSelection : _contextEntities;
        CloseContextMenu();
        await _canvas.SelectEntitiesAsync(keys);
        await InvokeWorkbenchCommand(command);
    }

    private void ContextDimensionMode(bool driving)
    {
        if (ContextEditable && ContextDimension is { } dimension)
        {
            if (SketchDimensionModeService.TrySetDriving(_document, dimension.Id, driving, out var changed, out var reason))
                ApplyDocumentChange(changed, reason);
            else _status = reason;
        }
        CloseContextMenu();
    }

    private void ContextDimensionRadialKind(SketchDimensionKind kind)
    {
        if (ContextEditable && ContextDimension is { } dimension)
        {
            if (SketchDimensionModeService.TrySetRadialKind(_document, dimension.Id, kind, out var changed, out var reason))
                ApplyDocumentChange(changed, reason);
            else _status = reason;
        }
        CloseContextMenu();
    }

    private void ContextMoveLayer(ChangeEventArgs args)
    {
        var name = args.Value?.ToString();
        if (ContextEditable && name is not null && Layers.ContainsKey(name))
            LayerChange(() => DrawingLayerService.Move(_document, _contextEntities, name), "Selection moved to layer.");
        CloseContextMenu();
    }
}

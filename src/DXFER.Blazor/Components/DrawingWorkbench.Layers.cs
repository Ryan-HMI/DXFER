using System.Globalization;
using DXFER.Core.Documents;
using DXFER.Core.Operations;
using Microsoft.AspNetCore.Components;

namespace DXFER.Blazor.Components;

public partial class DrawingWorkbench
{
    private string _selectedLayerName = "0";
    private string _layerNameInput = "0";
    private string LayerNameDraft
    {
        get => _selectedLayerName == SelectedLayerName ? _layerNameInput : SelectedLayerName;
        set => _layerNameInput = value;
    }
    private string? _layerError;
    private bool _layersCollapsed;
    private IReadOnlyDictionary<string, DxfEntityStyle> Layers => DrawingLayerService.GetLayers(_document);
    private string SelectedLayerName => Layers.ContainsKey(_selectedLayerName) ? _selectedLayerName : "0";
    private DxfEntityStyle SelectedLayer => Layers[SelectedLayerName];
    private bool LayersReadOnly => _document.Metadata.Mode == DrawingDocumentMode.ReferenceOnly;
    private int LayerCount(string name) => _document.Entities.Count(e => DrawingLayerService.LayerName(_document, e.Id.Value).Equals(name, StringComparison.OrdinalIgnoreCase));

    private void PickLayer(string name)
    {
        _selectedLayerName = name;
        _layerNameInput = name;
        _layerError = null;
    }

    private void LayerChange(Func<DrawingDocument> change, string status)
    {
        if (LayersReadOnly) return;
        try
        {
            ApplyDocumentChange(change(), status);
            ClearCornerPreview();
            ResetSelection();
            _layerError = null;
        }
        catch (ArgumentException ex) { _layerError = ex.Message; }
    }

    private void AddLayer()
    {
        var index = 1;
        while (Layers.ContainsKey($"Layer {index}")) index++;
        var name = $"Layer {index}";
        LayerChange(() => DrawingLayerService.SetLayer(_document, new(name, "CONTINUOUS", 7)), "Layer created.");
        PickLayer(name);
    }

    private void RenameLayer()
    {
        var old = SelectedLayerName;
        var name = _layerNameInput.Trim();
        LayerChange(() => DrawingLayerService.SetLayer(_document, SelectedLayer with { LayerName = name }, old), "Layer renamed.");
        if (_layerError is null) PickLayer(name);
    }

    private void ToggleLayerVisible(string name) => LayerChange(
        () => DrawingLayerService.SetLayer(_document, Layers[name] with { IsHidden = !Layers[name].IsHidden }, name), "Layer visibility changed.");
    private void ToggleLayerLock(string name) => LayerChange(
        () => DrawingLayerService.SetLayer(_document, Layers[name] with { IsLocked = !Layers[name].IsLocked }, name), "Layer lock changed.");
    private void ChangeLayerColor(string name, ChangeEventArgs args)
    {
        if (int.TryParse(args.Value?.ToString()?.TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            LayerChange(() => DrawingLayerService.SetLayer(_document, Layers[name] with { TrueColor = rgb }, name), "Layer color changed.");
    }
    private void SetCurrentLayer() => LayerChange(() => DrawingLayerService.WithMetadata(_document,
        _document.Metadata with { ActiveLayerName = SelectedLayerName }), "Current drawing layer changed.");
    private void DeleteLayer() => LayerChange(() => DrawingLayerService.DeleteEmpty(_document, SelectedLayerName), "Empty layer deleted.");
    private void MoveToLayer()
    {
        var ids = _selectedEntityIds.Select(k => k.Split('|')[0]).ToArray();
        LayerChange(() => DrawingLayerService.Move(_document, ids, SelectedLayerName), "Selection moved to layer.");
    }
    private async Task SelectLayerEntities()
    {
        if (SelectedLayer.IsHidden || SelectedLayer.IsLocked) return;
        _activeTool = null;
        ClearCornerPreview();
        var ids = _document.Entities.Where(e => DrawingLayerService.LayerName(_document, e.Id.Value).Equals(SelectedLayerName, StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Id.Value).ToArray();
        if (_canvas is not null) await _canvas.SelectEntitiesAsync(ids);
    }
}

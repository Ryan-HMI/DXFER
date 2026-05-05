using DXFER.Blazor.Interop;
using DXFER.Core.Documents;
using DXFER.Core.Operations;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace DXFER.Blazor.Components;

public partial class DrawingCanvas : IAsyncDisposable
{
    private const string CanvasModulePath = "./_content/DXFER.Blazor/drawingCanvas.js?v=20260504-split-construction";

    private ElementReference _canvas;
    private ElementReference _dimensionOverlay;
    private IJSObjectReference? _module;
    private IJSObjectReference? _canvasInstance;
    private DotNetObjectReference<DrawingCanvas>? _dotNetReference;
    private DrawingDocument? _renderedDocument;
    private WorkbenchTool? _renderedActiveTool;
    private int _renderedSelectionResetToken;
    private int _renderedDocumentFitToken = -1;

    [Inject]
    private IJSRuntime JsRuntime { get; set; } = default!;

    [Parameter]
    public DrawingDocument? Document { get; set; }

    [Parameter]
    public bool ShowToolbar { get; set; } = true;

    [Parameter]
    public int DocumentFitToken { get; set; }

    [Parameter]
    public bool ShowOriginAxes { get; set; }

    [Parameter]
    public WorkbenchTool? ActiveTool { get; set; }

    [Parameter]
    public GrainDirection GrainDirection { get; set; } = GrainDirection.None;

    [Parameter]
    public bool ConstructionMode { get; set; }

    [Parameter]
    public int SelectionResetToken { get; set; }

    [Parameter]
    public Action<string?>? HoveredEntityChanged { get; set; }

    [Parameter]
    public Action<IReadOnlySet<string>>? SelectionChanged { get; set; }

    [Parameter]
    public Action<string?>? ActiveSelectionChanged { get; set; }

    [Parameter]
    public Action<string, IReadOnlyList<CanvasPointDto>, IReadOnlyDictionary<string, double>>? ToolCommitRequested { get; set; }

    [Parameter]
    public Action<string>? ToolModeChanged { get; set; }

    [Parameter]
    public Action<string, CanvasPointDto>? SplitAtPointRequested { get; set; }

    [Parameter]
    public Action<string>? ConstructionToggleRequested { get; set; }

    [Parameter]
    public Action? ToolCancelRequested { get; set; }

    [Parameter]
    public Action<IReadOnlySet<string>>? DeleteSelectionRequested { get; set; }

    [Parameter]
    public Action<string, double>? SketchDimensionValueChanged { get; set; }

    [Parameter]
    public Action<string, CanvasPointDto>? SketchDimensionAnchorChanged { get; set; }

    [Parameter]
    public Action<IReadOnlyList<string>, CanvasPointDto, bool>? SketchDimensionPlacementRequested { get; set; }

    protected string? HoveredEntityId { get; private set; }

    protected HashSet<string> SelectedEntityIds { get; } = new(StringComparer.Ordinal);

    protected string? ActiveSelectionKey { get; private set; }

    protected override async Task OnParametersSetAsync()
    {
        if (_canvasInstance is not null && _renderedSelectionResetToken != SelectionResetToken)
        {
            _renderedSelectionResetToken = SelectionResetToken;
            await ClearSelectionStateAsync();
        }

        if (_canvasInstance is not null && !ReferenceEquals(_renderedDocument, Document))
        {
            await SetCanvasDocumentAsync();
        }

        if (_canvasInstance is not null)
        {
            if (_renderedActiveTool != ActiveTool)
            {
                await SetActiveToolAsync();
            }

            await SetOriginAxesVisibilityAsync();
            await SetGrainDirectionAsync();
            await SetConstructionModeAsync();
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        _dotNetReference = DotNetObjectReference.Create(this);
        _module = await JsRuntime.InvokeAsync<IJSObjectReference>("import", CanvasModulePath);
        _canvasInstance = await _module.InvokeAsync<IJSObjectReference>("createDrawingCanvas", _canvas, _dotNetReference, _dimensionOverlay);

        _renderedSelectionResetToken = SelectionResetToken;
        await SetCanvasDocumentAsync();
        await SetActiveToolAsync();
        await SetOriginAxesVisibilityAsync();
        await SetGrainDirectionAsync();
        await SetConstructionModeAsync();
    }

    public async Task FitToExtentsAsync()
    {
        if (_canvasInstance is null)
        {
            return;
        }

        await _canvasInstance.InvokeVoidAsync("fitToExtents");
    }

    public async Task SyncSelectionFromCanvasAsync()
    {
        if (_canvasInstance is null)
        {
            return;
        }

        var selectedKeys = await _canvasInstance.InvokeAsync<string[]>("getSelectedKeys");
        var activeSelectionKey = await _canvasInstance.InvokeAsync<string?>("getActiveSelectionKey");
        ApplySelectionState(selectedKeys, activeSelectionKey);
    }

    [JSInvokable]
    public Task OnEntityHovered(string? selectionKey)
    {
        if (StringComparer.Ordinal.Equals(HoveredEntityId, selectionKey))
        {
            return Task.CompletedTask;
        }

        HoveredEntityId = selectionKey;
        HoveredEntityChanged?.Invoke(HoveredEntityId);
        _ = InvokeAsync(StateHasChanged);
        return Task.CompletedTask;
    }

    [JSInvokable]
    public Task OnEntityClicked(string selectionKey)
    {
        if (StringComparer.Ordinal.Equals(ActiveSelectionKey, selectionKey))
        {
            SelectedEntityIds.Remove(selectionKey);
            ActiveSelectionKey = null;
        }
        else
        {
            SelectedEntityIds.Add(selectionKey);
            ActiveSelectionKey = selectionKey;
        }

        SelectionChanged?.Invoke(SelectedEntityIds);
        ActiveSelectionChanged?.Invoke(ActiveSelectionKey);
        _ = InvokeAsync(StateHasChanged);
        return Task.CompletedTask;
    }

    [JSInvokable]
    public Task OnSelectionChangedFromCanvas(string[] selectionKeys, string? activeSelectionKey)
    {
        ApplySelectionState(selectionKeys, activeSelectionKey);
        _ = InvokeAsync(StateHasChanged);
        return Task.CompletedTask;
    }

    private void ApplySelectionState(IEnumerable<string> selectionKeys, string? activeSelectionKey)
    {
        SelectedEntityIds.Clear();
        foreach (var selectionKey in selectionKeys)
        {
            SelectedEntityIds.Add(selectionKey);
        }

        ActiveSelectionKey = activeSelectionKey is not null && SelectedEntityIds.Contains(activeSelectionKey)
            ? activeSelectionKey
            : null;
        SelectionChanged?.Invoke(SelectedEntityIds);
        ActiveSelectionChanged?.Invoke(ActiveSelectionKey);
    }

    [JSInvokable]
    public Task OnSelectionCleared()
    {
        if (SelectedEntityIds.Count == 0)
        {
            return Task.CompletedTask;
        }

        SelectedEntityIds.Clear();
        ActiveSelectionKey = null;
        SelectionChanged?.Invoke(SelectedEntityIds);
        ActiveSelectionChanged?.Invoke(ActiveSelectionKey);
        _ = InvokeAsync(StateHasChanged);
        return Task.CompletedTask;
    }

    [JSInvokable]
    public Task OnSketchToolCommitted(
        string toolName,
        double[] coordinates,
        string[]? dimensionKeys = null,
        double[]? dimensionValues = null)
    {
        var points = new List<CanvasPointDto>();
        for (var index = 0; index + 1 < coordinates.Length; index += 2)
        {
            points.Add(new CanvasPointDto(coordinates[index], coordinates[index + 1]));
        }

        ToolCommitRequested?.Invoke(toolName, points, BuildDimensionLocks(dimensionKeys, dimensionValues));
        return Task.CompletedTask;
    }

    private static IReadOnlyDictionary<string, double> BuildDimensionLocks(
        IReadOnlyList<string>? dimensionKeys,
        IReadOnlyList<double>? dimensionValues)
    {
        if (dimensionKeys is null || dimensionValues is null)
        {
            return new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        }

        var locks = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var count = Math.Min(dimensionKeys.Count, dimensionValues.Count);
        for (var index = 0; index < count; index++)
        {
            var key = dimensionKeys[index];
            var value = dimensionValues[index];
            if (string.IsNullOrWhiteSpace(key) || !double.IsFinite(value) || value <= 0)
            {
                continue;
            }

            locks[key.Trim()] = value;
        }

        return locks;
    }

    [JSInvokable]
    public Task OnSketchToolModeChanged(string toolName)
    {
        ToolModeChanged?.Invoke(toolName);
        _ = InvokeAsync(StateHasChanged);
        return Task.CompletedTask;
    }

    [JSInvokable]
    public Task OnSplitAtPointRequested(string targetKey, double x, double y)
    {
        SplitAtPointRequested?.Invoke(targetKey, new CanvasPointDto(x, y));
        return Task.CompletedTask;
    }

    [JSInvokable]
    public Task OnConstructionToggleRequested(string targetKey)
    {
        ConstructionToggleRequested?.Invoke(targetKey);
        return Task.CompletedTask;
    }

    [JSInvokable]
    public Task OnSketchToolCanceled()
    {
        ToolCancelRequested?.Invoke();
        return Task.CompletedTask;
    }

    [JSInvokable]
    public Task OnDeleteSelectionRequested(string[] selectionKeys)
    {
        SelectedEntityIds.Clear();
        foreach (var selectionKey in selectionKeys)
        {
            SelectedEntityIds.Add(selectionKey);
        }

        ActiveSelectionKey = null;
        SelectionChanged?.Invoke(SelectedEntityIds);
        ActiveSelectionChanged?.Invoke(ActiveSelectionKey);
        DeleteSelectionRequested?.Invoke(SelectedEntityIds);
        _ = InvokeAsync(StateHasChanged);
        return Task.CompletedTask;
    }

    [JSInvokable]
    public Task OnSketchDimensionValueChanged(string dimensionId, double value)
    {
        SketchDimensionValueChanged?.Invoke(dimensionId, value);
        return Task.CompletedTask;
    }

    [JSInvokable]
    public Task OnSketchDimensionAnchorChanged(string dimensionId, double x, double y)
    {
        SketchDimensionAnchorChanged?.Invoke(dimensionId, new CanvasPointDto(x, y));
        return Task.CompletedTask;
    }

    [JSInvokable]
    public Task OnSketchDimensionPlacementRequested(string[] selectionKeys, double x, double y, bool radialDiameter)
    {
        SketchDimensionPlacementRequested?.Invoke(selectionKeys, new CanvasPointDto(x, y), radialDiameter);
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_canvasInstance is not null)
        {
            try
            {
                await _canvasInstance.InvokeVoidAsync("dispose");
                await _canvasInstance.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
            }
        }

        if (_module is not null)
        {
            try
            {
                await _module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
            }
        }

        _dotNetReference?.Dispose();
    }

    private async Task SetCanvasDocumentAsync()
    {
        if (_canvasInstance is null)
        {
            return;
        }

        var documentChanged = !ReferenceEquals(_renderedDocument, Document);
        if (documentChanged)
        {
            _renderedDocument = Document;
            HoveredEntityId = null;
            HoveredEntityChanged?.Invoke(HoveredEntityId);
            if (RemoveStoredPointSelections())
            {
                SelectionChanged?.Invoke(SelectedEntityIds);
                ActiveSelectionChanged?.Invoke(ActiveSelectionKey);
            }
        }

        var shouldFitDocument = _renderedDocumentFitToken != DocumentFitToken;
        if (shouldFitDocument)
        {
            _renderedDocumentFitToken = DocumentFitToken;
        }

        var dto = Document is null ? CanvasDocumentDto.Empty : CanvasDocumentDto.FromDocument(Document);
        await _canvasInstance.InvokeVoidAsync("setDocument", dto, shouldFitDocument);
    }

    private async Task SetOriginAxesVisibilityAsync()
    {
        if (_canvasInstance is null)
        {
            return;
        }

        await _canvasInstance.InvokeVoidAsync("setOriginAxesVisible", ShowOriginAxes);
    }

    private async Task SetActiveToolAsync()
    {
        if (_canvasInstance is null)
        {
            return;
        }

        await _canvasInstance.InvokeVoidAsync("setActiveTool", ActiveTool?.ToString() ?? "select");
        _renderedActiveTool = ActiveTool;
    }

    private async Task SetGrainDirectionAsync()
    {
        if (_canvasInstance is null)
        {
            return;
        }

        await _canvasInstance.InvokeVoidAsync("setGrainDirection", GrainDirection.ToString());
    }

    private async Task SetConstructionModeAsync()
    {
        if (_canvasInstance is null)
        {
            return;
        }

        await _canvasInstance.InvokeVoidAsync("setConstructionMode", ConstructionMode);
    }

    private async Task ClearSelectionStateAsync()
    {
        HoveredEntityId = null;
        SelectedEntityIds.Clear();
        ActiveSelectionKey = null;
        HoveredEntityChanged?.Invoke(HoveredEntityId);
        SelectionChanged?.Invoke(SelectedEntityIds);
        ActiveSelectionChanged?.Invoke(ActiveSelectionKey);

        if (_canvasInstance is not null)
        {
            await _canvasInstance.InvokeVoidAsync("clearSelection");
        }
    }

    private bool RemoveStoredPointSelections()
    {
        var removed = false;
        foreach (var selectedEntityId in SelectedEntityIds.ToArray())
        {
            if (selectedEntityId.Contains("|point|", StringComparison.Ordinal))
            {
                SelectedEntityIds.Remove(selectedEntityId);
                removed = true;
            }
        }

        if (ActiveSelectionKey is not null && !SelectedEntityIds.Contains(ActiveSelectionKey))
        {
            ActiveSelectionKey = null;
            removed = true;
        }

        return removed;
    }
}

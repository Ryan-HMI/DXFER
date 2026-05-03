using DXFER.Blazor.Interop;
using DXFER.Core.Documents;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace DXFER.Blazor.Components;

public partial class DrawingCanvas : IAsyncDisposable
{
    private const string CanvasModulePath = "./_content/DXFER.Blazor/drawingCanvas.js";

    private ElementReference _canvas;
    private IJSObjectReference? _module;
    private IJSObjectReference? _canvasInstance;
    private DotNetObjectReference<DrawingCanvas>? _dotNetReference;
    private DrawingDocument? _renderedDocument;

    [Inject]
    private IJSRuntime JsRuntime { get; set; } = default!;

    [Parameter]
    public DrawingDocument? Document { get; set; }

    [Parameter]
    public bool ShowToolbar { get; set; } = true;

    [Parameter]
    public Action<string?>? HoveredEntityChanged { get; set; }

    [Parameter]
    public Action<IReadOnlySet<string>>? SelectionChanged { get; set; }

    protected string? HoveredEntityId { get; private set; }

    protected HashSet<string> SelectedEntityIds { get; } = new(StringComparer.Ordinal);

    protected override async Task OnParametersSetAsync()
    {
        if (_canvasInstance is not null && !ReferenceEquals(_renderedDocument, Document))
        {
            await SetCanvasDocumentAsync();
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
        _canvasInstance = await _module.InvokeAsync<IJSObjectReference>("createDrawingCanvas", _canvas, _dotNetReference);

        await SetCanvasDocumentAsync();
    }

    public async Task FitToExtentsAsync()
    {
        if (_canvasInstance is null)
        {
            return;
        }

        await _canvasInstance.InvokeVoidAsync("fitToExtents");
    }

    [JSInvokable]
    public Task OnEntityHovered(string? entityId)
    {
        if (StringComparer.Ordinal.Equals(HoveredEntityId, entityId))
        {
            return Task.CompletedTask;
        }

        HoveredEntityId = entityId;
        HoveredEntityChanged?.Invoke(HoveredEntityId);
        _ = InvokeAsync(StateHasChanged);
        return Task.CompletedTask;
    }

    [JSInvokable]
    public Task OnEntityClicked(string entityId)
    {
        if (!SelectedEntityIds.Add(entityId))
        {
            SelectedEntityIds.Remove(entityId);
        }

        SelectionChanged?.Invoke(SelectedEntityIds);
        _ = InvokeAsync(StateHasChanged);
        return Task.CompletedTask;
    }

    [JSInvokable]
    public Task OnSelectionCleared()
    {
        if (SelectedEntityIds.Count == 0)
        {
            return Task.CompletedTask;
        }

        SelectedEntityIds.Clear();
        SelectionChanged?.Invoke(SelectedEntityIds);
        _ = InvokeAsync(StateHasChanged);
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
            SelectedEntityIds.Clear();
            HoveredEntityChanged?.Invoke(HoveredEntityId);
            SelectionChanged?.Invoke(SelectedEntityIds);
        }

        var dto = Document is null ? CanvasDocumentDto.Empty : CanvasDocumentDto.FromDocument(Document);
        await _canvasInstance.InvokeVoidAsync("setDocument", dto);
    }
}

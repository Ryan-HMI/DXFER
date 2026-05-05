using System.Globalization;
using System.Text.Json;
using DXFER.Blazor.IO;
using DXFER.Blazor.Interop;
using DXFER.Blazor.Selection;
using DXFER.Blazor.Sketching;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.IO;
using DXFER.Core.Operations;
using DXFER.Core.Sketching;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace DXFER.Blazor.Components;

public partial class DrawingWorkbench : IDisposable, IAsyncDisposable
{
    private const string HotkeyModulePath = "./_content/DXFER.Blazor/workbenchHotkeys.js?v=20260505-undo-redo";
    private const string DownloadModulePath = "./_content/DXFER.Blazor/downloadFile.js?v=20260504-save";
    private const long MaxDxfFileSize = 25 * 1024 * 1024;
    private const string SegmentKeySeparator = "|segment|";
    private const string PointKeySeparator = "|point|";
    private const int MinToolPanelWidth = 184;
    private const int MaxToolPanelWidth = 360;
    private const int MinInspectorWidth = 220;
    private const int MaxInspectorWidth = 520;
    private const int DefaultPatternInstanceCount = 3;

    private DrawingCanvas? _canvas;
    private DrawingDocument _document = SampleDrawingFactory.CreateCanvasPrototype();
    private string _fileName = "Sample flat pattern";
    private string _status = "Ready. Select a line to align a vector or load a DXF.";
    private string? _hoveredEntityId;
    private string? _activeSelectionKey;
    private string _exportText = string.Empty;
    private GrainDirection _grainDirection = GrainDirection.None;
    private WorkbenchTool? _activeTool;
    private bool _showOriginAxes = true;
    private bool _constructionMode;
    private bool _isToolPanelCollapsed;
    private bool _isInspectorCollapsed = true;
    private int _toolPanelWidth = 220;
    private int _inspectorWidth = 280;
    private int _selectionResetToken;
    private int _documentFitToken = 1;
    private int _createdEntitySequence;
    private int _createdDimensionSequence;
    private int _createdConstraintSequence;
    private DockResizeTarget _resizeTarget = DockResizeTarget.None;
    private double _resizeStartClientX;
    private int _resizeStartToolPanelWidth;
    private int _resizeStartInspectorWidth;
    private readonly HashSet<string> _selectedEntityIds = new(StringComparer.Ordinal);
    private readonly Stack<DrawingDocument> _undoStack = new();
    private readonly Stack<DrawingDocument> _redoStack = new();
    private IJSObjectReference? _hotkeyModule;
    private IJSObjectReference? _hotkeyListener;
    private IJSObjectReference? _downloadModule;
    private DotNetObjectReference<DrawingWorkbench>? _hotkeyDotNetReference;

    [Inject]
    private WorkbenchMenuCommandService MenuCommandService { get; set; } = default!;

    [Inject]
    private ToolHotkeyService ToolHotkeys { get; set; } = default!;

    [Inject]
    private IJSRuntime JsRuntime { get; set; } = default!;

    protected override void OnInitialized()
    {
        MenuCommandService.CommandRequested += InvokeWorkbenchCommand;
        MenuCommandService.FileOpenRequested += OpenFileAsync;
        ToolHotkeys.BindingsChanged += OnToolHotkeysChanged;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        _hotkeyDotNetReference = DotNetObjectReference.Create(this);
        _hotkeyModule = await JsRuntime.InvokeAsync<IJSObjectReference>("import", HotkeyModulePath);
        _hotkeyListener = await _hotkeyModule.InvokeAsync<IJSObjectReference>(
            "createToolHotkeyListener",
            _hotkeyDotNetReference);
    }

    public void Dispose()
    {
        MenuCommandService.CommandRequested -= InvokeWorkbenchCommand;
        MenuCommandService.FileOpenRequested -= OpenFileAsync;
        ToolHotkeys.BindingsChanged -= OnToolHotkeysChanged;
        _hotkeyDotNetReference?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_hotkeyListener is not null)
        {
            try
            {
                await _hotkeyListener.InvokeVoidAsync("dispose");
                await _hotkeyListener.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
            }
        }

        if (_hotkeyModule is not null)
        {
            try
            {
                await _hotkeyModule.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
            }
        }

        if (_downloadModule is not null)
        {
            try
            {
                await _downloadModule.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
            }
        }

        Dispose();
    }

    private bool HasDocument => _document.Entities.Count > 0;

    private bool HasSelection => _selectedEntityIds.Count > 0;

    private bool CanDeleteSelection =>
        SelectionDeleteResolver.CanDeleteSelection(_document, _selectedEntityIds);

    private bool CanUndo => _undoStack.Count > 0;

    private bool CanRedo => _redoStack.Count > 0;

    private bool CanAlignSelectedVector =>
        SelectionVectorResolver.TryGetAlignmentVector(_document, _selectedEntityIds, _activeSelectionKey, out _, out _);

    private bool CanMoveSelectedPointToOrigin =>
        SelectionPointResolver.TryGetPointToOriginReference(_document, _selectedEntityIds, out _);

    private bool CanModifySelectedGeometry =>
        GetWholeEntityIdsForOperations().Any();

    private bool CanFilletOrChamferSelectedLines =>
        GetSelectedWholeEntities().OfType<LineEntity>().Take(3).Count() == 2;

    private bool CanCreateSketchDimension =>
        SketchCommandFactory.TryBuildDimension(
            _document,
            _selectedEntityIds,
            "preview",
            out _,
            out _,
            _activeSelectionKey);

    private bool CanCreateConstraint(SketchConstraintKind kind) =>
        SketchCommandFactory.TryBuildConstraint(
            _document,
            _selectedEntityIds,
            kind,
            "preview",
            out _,
            out _,
            _activeSelectionKey);

    private Bounds2 Bounds => _document.GetBounds();

    private IReadOnlyList<WorkbenchToolGroup> ToolGroups => new[]
    {
        new WorkbenchToolGroup("View", new[]
        {
            Command(WorkbenchCommandId.Measure, WorkbenchTool.Measure, CadIconName.Measure, "Measure"),
            Command(WorkbenchCommandId.FitExtents, null, CadIconName.Fit, "Fit extents", !HasDocument),
            Command(WorkbenchCommandId.OriginAxes, null, CadIconName.OriginAxes, "Origin axes", pressed: _showOriginAxes),
            Command(WorkbenchCommandId.ToolHotkeys, null, CadIconName.Hotkeys, "Tool hotkeys")
        }),
        new WorkbenchToolGroup("Sketch", new[]
        {
            Command(WorkbenchCommandId.Line, WorkbenchTool.Line, CadIconName.Line, "Line"),
            Command(WorkbenchCommandId.MidpointLine, WorkbenchTool.MidpointLine, CadIconName.MidpointLine, "Midpoint line"),
            Command(WorkbenchCommandId.TwoPointRectangle, WorkbenchTool.TwoPointRectangle, CadIconName.Rectangle, "Two-point rectangle"),
            Command(WorkbenchCommandId.CenterRectangle, WorkbenchTool.CenterRectangle, CadIconName.CenterRectangle, "Center rectangle"),
            Command(WorkbenchCommandId.AlignedRectangle, WorkbenchTool.AlignedRectangle, CadIconName.AlignedRectangle, "Aligned rectangle"),
            Command(WorkbenchCommandId.CenterCircle, WorkbenchTool.CenterCircle, CadIconName.Circle, "Center circle"),
            Command(WorkbenchCommandId.ThreePointCircle, WorkbenchTool.ThreePointCircle, CadIconName.ThreePointCircle, "Three-point circle"),
            Command(WorkbenchCommandId.Ellipse, WorkbenchTool.Ellipse, CadIconName.Ellipse, "Ellipse", disabled: true, isFuture: true),
            Command(WorkbenchCommandId.ThreePointArc, WorkbenchTool.ThreePointArc, CadIconName.Arc, "Three-point arc"),
            Command(WorkbenchCommandId.CenterPointArc, WorkbenchTool.CenterPointArc, CadIconName.CenterPointArc, "Center point arc"),
            Command(WorkbenchCommandId.EllipticalArc, WorkbenchTool.EllipticalArc, CadIconName.EllipticalArc, "Elliptical arc", disabled: true, isFuture: true),
            Command(WorkbenchCommandId.Conic, WorkbenchTool.Conic, CadIconName.Conic, "Conic", disabled: true, isFuture: true),
            Command(WorkbenchCommandId.InscribedPolygon, WorkbenchTool.InscribedPolygon, CadIconName.InscribedPolygon, "Inscribed polygon", disabled: true, isFuture: true),
            Command(WorkbenchCommandId.CircumscribedPolygon, WorkbenchTool.CircumscribedPolygon, CadIconName.CircumscribedPolygon, "Circumscribed polygon", disabled: true, isFuture: true),
            Command(WorkbenchCommandId.Spline, WorkbenchTool.Spline, CadIconName.Spline, "Spline", disabled: true, isFuture: true),
            Command(WorkbenchCommandId.Bezier, WorkbenchTool.Bezier, CadIconName.Bezier, "Bezier", disabled: true, isFuture: true),
            Command(WorkbenchCommandId.SplineControlPoint, WorkbenchTool.SplineControlPoint, CadIconName.SplineControlPoint, "Spline control point", disabled: true, isFuture: true),
            Command(WorkbenchCommandId.Point, WorkbenchTool.Point, CadIconName.Point, "Point"),
            Command(WorkbenchCommandId.Text, WorkbenchTool.Text, CadIconName.Text, "Text", disabled: true, isFuture: true),
            Command(WorkbenchCommandId.Slot, WorkbenchTool.Slot, CadIconName.Slot, "Slot", disabled: true, isFuture: true)
        }),
        new WorkbenchToolGroup("Modify", new[]
        {
            Command(WorkbenchCommandId.Undo, null, CadIconName.Undo, "Undo", !CanUndo),
            Command(WorkbenchCommandId.Redo, null, CadIconName.Redo, "Redo", !CanRedo),
            Command(
                WorkbenchCommandId.Construction,
                WorkbenchTool.Construction,
                CadIconName.Construction,
                "Construction",
                tooltip: "Construction. Converts preselected geometry once; with no selection, enters a modal convert tool until Esc."),
            Command(WorkbenchCommandId.DeleteSelection, null, CadIconName.Delete, "Delete selected geometry", !CanDeleteSelection),
            Command(WorkbenchCommandId.PowerTrim, WorkbenchTool.PowerTrim, CadIconName.PowerTrim, "Power trim/extend"),
            Command(WorkbenchCommandId.SplitAtPoint, WorkbenchTool.SplitAtPoint, CadIconName.Split, "Split at point"),
            Command(WorkbenchCommandId.Offset, WorkbenchTool.Offset, CadIconName.Offset, "Offset", !CanModifySelectedGeometry),
            Command(WorkbenchCommandId.Fillet, null, CadIconName.Fillet, "Fillet", !CanFilletOrChamferSelectedLines),
            Command(WorkbenchCommandId.Chamfer, null, CadIconName.Chamfer, "Chamfer", !CanFilletOrChamferSelectedLines),
            Command(WorkbenchCommandId.Dimension, WorkbenchTool.Dimension, CadIconName.Dimension, "Dimension")
        }),
        new WorkbenchToolGroup("Transform", new[]
        {
            Command(WorkbenchCommandId.Translate, WorkbenchTool.Translate, CadIconName.Move, "Translate", !CanModifySelectedGeometry),
            Command(WorkbenchCommandId.Rotate, WorkbenchTool.Rotate, CadIconName.Rotate, "Rotate", !CanModifySelectedGeometry),
            Command(WorkbenchCommandId.Rotate90Clockwise, null, CadIconName.Rotate90Clockwise, "Rotate 90 CW", !HasDocument),
            Command(WorkbenchCommandId.Rotate90CounterClockwise, null, CadIconName.Rotate90CounterClockwise, "Rotate 90 CCW", !HasDocument),
            Command(WorkbenchCommandId.Scale, WorkbenchTool.Scale, CadIconName.Scale, "Scale", !CanModifySelectedGeometry),
            Command(WorkbenchCommandId.Mirror, WorkbenchTool.Mirror, CadIconName.Mirror, "Mirror", !CanModifySelectedGeometry),
            Command(WorkbenchCommandId.BoundsToOrigin, null, CadIconName.BoundsToOrigin, "Move bounds minimum to origin", !HasDocument),
            Command(WorkbenchCommandId.PointToOrigin, null, CadIconName.PointToOrigin, "Move selected point to origin", !CanMoveSelectedPointToOrigin),
            Command(WorkbenchCommandId.VectorToX, null, CadIconName.VectorToX, "Align selected vector to X", !CanAlignSelectedVector),
            Command(WorkbenchCommandId.VectorToY, null, CadIconName.VectorToY, "Align selected vector to Y", !CanAlignSelectedVector)
        }),
        new WorkbenchToolGroup("Pattern", new[]
        {
            Command(WorkbenchCommandId.LinearPattern, WorkbenchTool.LinearPattern, CadIconName.LinearPattern, "Linear pattern", !CanModifySelectedGeometry),
            Command(WorkbenchCommandId.CircularPattern, WorkbenchTool.CircularPattern, CadIconName.CircularPattern, "Circular pattern", !CanModifySelectedGeometry)
        }),
        new WorkbenchToolGroup("Constraints", new[]
        {
            Command(WorkbenchCommandId.Coincident, null, CadIconName.Coincident, "Coincident", !CanCreateConstraint(SketchConstraintKind.Coincident)),
            Command(WorkbenchCommandId.Concentric, null, CadIconName.Concentric, "Concentric", !CanCreateConstraint(SketchConstraintKind.Concentric)),
            Command(WorkbenchCommandId.Parallel, null, CadIconName.Parallel, "Parallel", !CanCreateConstraint(SketchConstraintKind.Parallel)),
            Command(WorkbenchCommandId.Tangent, WorkbenchTool.Tangent, CadIconName.Tangent, "Tangent", disabled: true, isFuture: true),
            Command(WorkbenchCommandId.Horizontal, null, CadIconName.Horizontal, "Horizontal", !CanCreateConstraint(SketchConstraintKind.Horizontal)),
            Command(WorkbenchCommandId.Vertical, null, CadIconName.Vertical, "Vertical", !CanCreateConstraint(SketchConstraintKind.Vertical)),
            Command(WorkbenchCommandId.Perpendicular, null, CadIconName.Perpendicular, "Perpendicular", !CanCreateConstraint(SketchConstraintKind.Perpendicular)),
            Command(WorkbenchCommandId.Equal, null, CadIconName.Equal, "Equal", !CanCreateConstraint(SketchConstraintKind.Equal)),
            Command(WorkbenchCommandId.Midpoint, null, CadIconName.Midpoint, "Midpoint", !CanCreateConstraint(SketchConstraintKind.Midpoint)),
            Command(WorkbenchCommandId.Normal, WorkbenchTool.Normal, CadIconName.Normal, "Normal", disabled: true, isFuture: true),
            Command(WorkbenchCommandId.Pierce, WorkbenchTool.Pierce, CadIconName.Pierce, "Pierce", disabled: true, isFuture: true),
            Command(WorkbenchCommandId.Symmetric, WorkbenchTool.Symmetric, CadIconName.Symmetric, "Symmetric", disabled: true, isFuture: true),
            Command(WorkbenchCommandId.Fix, null, CadIconName.Fix, "Fix", !CanCreateConstraint(SketchConstraintKind.Fix)),
            Command(WorkbenchCommandId.Curvature, WorkbenchTool.Curvature, CadIconName.Curvature, "Curvature", disabled: true, isFuture: true)
        })
    };

    private string WorkbenchCssClass
    {
        get
        {
            var classes = new List<string> { "dxfer-workbench" };
            if (_isToolPanelCollapsed)
            {
                classes.Add("dxfer-tool-panel-collapsed");
            }

            if (_isInspectorCollapsed)
            {
                classes.Add("dxfer-inspector-collapsed");
            }

            if (_resizeTarget is not DockResizeTarget.None)
            {
                classes.Add("dxfer-dock-resizing");
            }

            return string.Join(" ", classes);
        }
    }

    private string WorkbenchGridStyle =>
        $"--dxfer-tools-width:{_toolPanelWidth}px;--dxfer-inspector-width:{_inspectorWidth}px;";

    private bool IsParametricCommandActive =>
        _activeTool.HasValue;

    private string ActiveToolLabel => _activeTool switch
    {
        WorkbenchTool.MidpointLine => "Midpoint line",
        WorkbenchTool.TwoPointRectangle => "Two-point rectangle",
        WorkbenchTool.CenterRectangle => "Center rectangle",
        WorkbenchTool.AlignedRectangle => "Aligned rectangle",
        WorkbenchTool.CenterCircle => "Center circle",
        WorkbenchTool.ThreePointCircle => "Three-point circle",
        WorkbenchTool.CenterPointArc => "Center point arc",
        WorkbenchTool.EllipticalArc => "Elliptical arc",
        WorkbenchTool.TangentArc => "Tangent arc",
        WorkbenchTool.InscribedPolygon => "Inscribed polygon",
        WorkbenchTool.CircumscribedPolygon => "Circumscribed polygon",
        WorkbenchTool.SplineControlPoint => "Spline control point",
        WorkbenchTool.Point => "Point",
        WorkbenchTool.Construction => "Construction",
        WorkbenchTool.PowerTrim => "Power trim/extend",
        WorkbenchTool.ThreePointArc => "Three-point arc",
        WorkbenchTool.SplitAtPoint => "Split at point",
        WorkbenchTool.Offset => "Offset",
        WorkbenchTool.Translate => "Translate",
        WorkbenchTool.Rotate => "Rotate",
        WorkbenchTool.Scale => "Scale",
        WorkbenchTool.Mirror => "Mirror",
        WorkbenchTool.Dimension => "Dimension",
        WorkbenchTool.LinearPattern => "Linear pattern",
        WorkbenchTool.CircularPattern => "Circular pattern",
        _ => _activeTool?.ToString() ?? "Selection"
    };

    private string HoverText => FormatSelectionKey(_hoveredEntityId);

    private string ActiveSelectionText => FormatSelectionKey(_activeSelectionKey);

    private string CommandPromptText => _activeTool switch
    {
        WorkbenchTool.Measure => "Measure: select points or geometry for live deltas. Esc: exit measure.",
        WorkbenchTool.Line => "Line: click start point, then end point. Shift: polar snap. Double-click: start a fresh line. Esc: cancel.",
        WorkbenchTool.MidpointLine => "Midpoint line: click midpoint, then endpoint. Shift: polar snap. Esc: cancel.",
        WorkbenchTool.TwoPointRectangle => "Corner rectangle: click first corner, then opposite corner. Shift: polar snap. Esc: cancel.",
        WorkbenchTool.CenterRectangle => "Center rectangle: click center, then corner. Shift: polar snap. Esc: cancel.",
        WorkbenchTool.AlignedRectangle => "Aligned rectangle: click baseline start, baseline end, then depth. Esc: cancel.",
        WorkbenchTool.CenterCircle => "Center circle: click center, then radius point. Shift: polar snap. Esc: cancel.",
        WorkbenchTool.ThreePointCircle => "Three-point circle: click three points on the circumference. Esc: cancel.",
        WorkbenchTool.ThreePointArc => "Three-point arc: click start point, through point, then end point. Esc: cancel.",
        WorkbenchTool.TangentArc => "Tangent arc: click start point, tangent direction point, then end point. Esc: cancel.",
        WorkbenchTool.CenterPointArc => "Center point arc: click center, start radius point, then end angle point. Esc: cancel.",
        WorkbenchTool.Point => "Point: click to place a persistent sketch point. Esc: cancel.",
        WorkbenchTool.Construction => "Construction: click geometry to toggle construction state. Esc: cancel.",
        WorkbenchTool.PowerTrim => "Power trim/extend: click a line section to trim, or click past an endpoint to extend to another line. Esc: cancel.",
        WorkbenchTool.SplitAtPoint => "Split at point: click a line, or select one line and one point before invoking. Esc: cancel.",
        WorkbenchTool.Offset => "Offset: select whole geometry, then click the through side or radius point. Esc: cancel.",
        WorkbenchTool.Translate => "Translate: select whole geometry, click from point, then to point. Esc: cancel.",
        WorkbenchTool.Rotate => "Rotate: select whole geometry, click center, reference point, then target point. Esc: cancel.",
        WorkbenchTool.Scale => "Scale: select whole geometry, click center, reference radius, then target radius. Esc: cancel.",
        WorkbenchTool.Mirror => "Mirror: select whole geometry, click two points for the mirror axis. Esc: cancel.",
        WorkbenchTool.LinearPattern => "Linear pattern: select whole geometry, click from point, then spacing point. Creates three instances. Esc: cancel.",
        WorkbenchTool.CircularPattern => "Circular pattern: select whole geometry, click center, reference point, then angle point. Creates three instances. Esc: cancel.",
        WorkbenchTool.Dimension => "Dimension: click geometry or points, move to place, then click to set. Shift: diameter for arcs. Esc: cancel.",
        not null => $"{ActiveToolLabel}: follow canvas prompts. Esc: cancel.",
        null => _constructionMode
            ? "Selection: construction creation is enabled. Click Construction to disable, or select whole geometry and click it to convert. Box select adds; Ctrl-box deselects."
            : "Selection: click to select and make active. Click active again to deselect. Box select adds; Ctrl-box deselects."
    };

    private string MeasurementText
    {
        get
        {
            if (!TryGetSelectionMeasurement(out var measurement))
            {
                return "none";
            }

            return $"X {FormatNumber(measurement.DeltaX)}, Y {FormatNumber(measurement.DeltaY)}, D {FormatNumber(measurement.Distance)}";
        }
    }

    private IReadOnlyList<LiveReadoutItem> LiveMeasurementReadouts => BuildLiveMeasurementReadouts();

    private string MetadataJson
    {
        get
        {
            var bounds = Bounds;
            return JsonSerializer.Serialize(
                new
                {
                    fileName = _fileName,
                    entityCount = _document.Entities.Count,
                    grainDirection = _grainDirection.ToString(),
                    bounds = new
                    {
                        minX = Round(bounds.MinX),
                        minY = Round(bounds.MinY),
                        maxX = Round(bounds.MaxX),
                        maxY = Round(bounds.MaxY),
                        width = Round(bounds.Width),
                        height = Round(bounds.Height)
                    }
                },
                new JsonSerializerOptions { WriteIndented = true });
        }
    }

    private async Task OpenFileAsync(IBrowserFile file)
    {
        _fileName = file.Name;
        _exportText = string.Empty;

        if (file.Name.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase))
        {
            _status = "DWG selected. V1 keeps DWG as an external viewer handoff; DXFER editing stays DXF-only.";
            return;
        }

        await using var stream = file.OpenReadStream(MaxDxfFileSize);
        using var reader = new StreamReader(stream);
        var text = await reader.ReadToEndAsync();
        var document = DxfDocumentReader.Read(text);

        if (document.Entities.Count == 0)
        {
            _status = "No supported DXF entities were found. V1 reads LINE, CIRCLE, ARC, POINT, LWPOLYLINE, POLYLINE, and SPLINE.";
            return;
        }

        _document = document;
        _documentFitToken++;
        ClearHistory();
        ResetSelection();
        _status = $"Loaded {document.Entities.Count} supported entities from DXF.";
    }

    private void LoadSample()
    {
        _document = SampleDrawingFactory.CreateCanvasPrototype();
        _documentFitToken++;
        _fileName = "Sample flat pattern";
        _status = "Sample drawing loaded.";
        _exportText = string.Empty;
        ClearHistory();
        ResetSelection();
    }

    private async Task InvokeWorkbenchCommand(WorkbenchCommandId commandId)
    {
        switch (commandId)
        {
            case WorkbenchCommandId.LoadSample:
                LoadSample();
                break;
            case WorkbenchCommandId.ExportDxfText:
                GenerateDxfExport();
                break;
            case WorkbenchCommandId.SaveDxf:
                await DownloadDxfAsync();
                break;
            case WorkbenchCommandId.Measure:
                ActivateTool(WorkbenchTool.Measure, "Measure command active. Press Esc to return to selection.");
                break;
            case WorkbenchCommandId.Undo:
                UndoLastDocumentChange();
                break;
            case WorkbenchCommandId.Redo:
                RedoLastDocumentChange();
                break;
            case WorkbenchCommandId.FitExtents:
                await FitAsync();
                break;
            case WorkbenchCommandId.OriginAxes:
                ToggleOriginAxes();
                break;
            case WorkbenchCommandId.DeleteSelection:
                await SyncSelectionFromCanvasAsync();
                DeleteSelectedGeometry();
                break;
            case WorkbenchCommandId.Construction:
                await SyncSelectionFromCanvasAsync();
                ToggleConstructionModeOrSelection();
                break;
            case WorkbenchCommandId.SplitAtPoint:
                await SyncSelectionFromCanvasAsync();
                if (!TrySplitSelectedLineAtPoint())
                {
                    ActivateTool(
                        WorkbenchTool.SplitAtPoint,
                        "Split at point active. Click a line, or select one line and one point before invoking.");
                }

                break;
            case WorkbenchCommandId.PowerTrim:
                ActivateTool(
                    WorkbenchTool.PowerTrim,
                    "Power trim/extend active. Click a line section to trim, or click past an endpoint to extend to another line.");
                break;
            case WorkbenchCommandId.Offset:
            case WorkbenchCommandId.Translate:
            case WorkbenchCommandId.Rotate:
            case WorkbenchCommandId.Scale:
            case WorkbenchCommandId.Mirror:
            case WorkbenchCommandId.LinearPattern:
            case WorkbenchCommandId.CircularPattern:
                await SyncSelectionFromCanvasAsync();
                if (!CanModifySelectedGeometry)
                {
                    _status = $"{FormatCommandName(commandId)} needs one or more whole selected entities.";
                    break;
                }

                if (TryGetImplementedModifyTool(commandId, out var modifyTool))
                {
                    ActivateTool(
                        modifyTool,
                        $"{FormatCommandName(commandId)} active. {GetToolPrompt(modifyTool)} Press Esc to cancel.");
                }

                break;
            case WorkbenchCommandId.Fillet:
                await SyncSelectionFromCanvasAsync();
                FilletSelectedLines();
                break;
            case WorkbenchCommandId.Chamfer:
                await SyncSelectionFromCanvasAsync();
                ChamferSelectedLines();
                break;
            case WorkbenchCommandId.Dimension:
                ActivateTool(
                    WorkbenchTool.Dimension,
                    "Dimension active. Click geometry or points, move to place, then click to set. Shift: diameter for arcs. Esc to cancel.");
                break;
            case WorkbenchCommandId.Coincident:
                await SyncSelectionFromCanvasAsync();
                AddSketchConstraint(SketchConstraintKind.Coincident);
                break;
            case WorkbenchCommandId.Concentric:
                await SyncSelectionFromCanvasAsync();
                AddSketchConstraint(SketchConstraintKind.Concentric);
                break;
            case WorkbenchCommandId.Parallel:
                await SyncSelectionFromCanvasAsync();
                AddSketchConstraint(SketchConstraintKind.Parallel);
                break;
            case WorkbenchCommandId.Horizontal:
                await SyncSelectionFromCanvasAsync();
                AddSketchConstraint(SketchConstraintKind.Horizontal);
                break;
            case WorkbenchCommandId.Vertical:
                await SyncSelectionFromCanvasAsync();
                AddSketchConstraint(SketchConstraintKind.Vertical);
                break;
            case WorkbenchCommandId.Perpendicular:
                await SyncSelectionFromCanvasAsync();
                AddSketchConstraint(SketchConstraintKind.Perpendicular);
                break;
            case WorkbenchCommandId.Equal:
                await SyncSelectionFromCanvasAsync();
                AddSketchConstraint(SketchConstraintKind.Equal);
                break;
            case WorkbenchCommandId.Midpoint:
                await SyncSelectionFromCanvasAsync();
                AddSketchConstraint(SketchConstraintKind.Midpoint);
                break;
            case WorkbenchCommandId.Fix:
                await SyncSelectionFromCanvasAsync();
                AddSketchConstraint(SketchConstraintKind.Fix);
                break;
            case WorkbenchCommandId.BoundsToOrigin:
                MoveBoundsToOrigin();
                break;
            case WorkbenchCommandId.PointToOrigin:
                await SyncSelectionFromCanvasAsync();
                MoveSelectedPointToOrigin();
                break;
            case WorkbenchCommandId.VectorToX:
                await SyncSelectionFromCanvasAsync();
                AlignSelectedVectorToX();
                break;
            case WorkbenchCommandId.VectorToY:
                await SyncSelectionFromCanvasAsync();
                AlignSelectedVectorToY();
                break;
            case WorkbenchCommandId.Rotate90Clockwise:
                RotateDocumentAboutBoundsCenter(-90, "Rotated drawing 90 degrees clockwise.");
                break;
            case WorkbenchCommandId.Rotate90CounterClockwise:
                RotateDocumentAboutBoundsCenter(90, "Rotated drawing 90 degrees counterclockwise.");
                break;
            case WorkbenchCommandId.ToolHotkeys:
                MenuCommandService.OpenHotkeyOptions();
                break;
            default:
                if (TryGetImplementedSketchTool(commandId, out var tool))
                {
                    ActivateTool(tool, $"{FormatCommandName(commandId)} active. {GetToolPrompt(tool)} Press Esc to cancel.");
                    ResetSelection();
                    break;
                }

                _status = $"{FormatCommandName(commandId)} is not implemented yet.";
                break;
        }
    }

    private void OnToolCommitRequested(
        string toolName,
        IReadOnlyList<CanvasPointDto> points,
        IReadOnlyDictionary<string, double> dimensionValues)
    {
        var toolPoints = points.Select(ToPoint).ToArray();
        var newEntities = CreateEntitiesForTool(toolName, toolPoints).ToArray();
        if (newEntities.Length == 0)
        {
            return;
        }

        var newDimensions = SketchCreationDimensionFactory.CreateDimensionsForTool(
            toolName,
            newEntities,
            dimensionValues,
            CreateDimensionId);

        ApplyDocumentChange(
            new DrawingDocument(
                _document.Entities.Concat(newEntities),
                _document.Dimensions.Concat(newDimensions),
                _document.Constraints),
            $"{FormatCreatedToolName(toolName)} added.");
        ResetSelection();
        _ = InvokeAsync(StateHasChanged);
    }

    private void OnCanvasToolModeChanged(string toolName)
    {
        var tool = NormalizeToolName(toolName) switch
        {
            "line" => WorkbenchTool.Line,
            "tangentarc" => WorkbenchTool.TangentArc,
            _ => (WorkbenchTool?)null
        };

        if (tool is null || _activeTool == tool)
        {
            return;
        }

        _activeTool = tool.Value;
        _status = $"{ActiveToolLabel} active. {GetToolPrompt(tool.Value)} Press Esc to cancel.";
        _ = InvokeAsync(StateHasChanged);
    }

    private void OnSplitAtPointRequested(string targetKey, CanvasPointDto point)
    {
        if (!TryGetLineIdForSplitTarget(targetKey, out var lineEntityId))
        {
            _status = "Select one line before splitting at a selected point.";
            _ = InvokeAsync(StateHasChanged);
            return;
        }

        SplitLineAtPoint(lineEntityId, ToPoint(point));
    }

    private void OnConstructionToggleRequested(string targetKey)
    {
        if (!TryGetEntityIdForOperationTarget(targetKey, out var entityId))
        {
            _status = "Construction conversion needs whole geometry.";
            _ = InvokeAsync(StateHasChanged);
            return;
        }

        ToggleConstructionForEntityIds(
            new[] { entityId },
            "Construction convert active. Click geometry to toggle construction state. Esc to cancel.",
            clearTool: false);
    }

    private void OnModifyToolCommitRequested(string toolName, IReadOnlyList<CanvasPointDto> points)
    {
        var wholeEntityIds = GetWholeEntityIdsForOperations().ToArray();
        if (wholeEntityIds.Length == 0)
        {
            _status = $"{FormatCreatedToolName(toolName)} needs one or more whole selected entities.";
            _ = InvokeAsync(StateHasChanged);
            return;
        }

        var toolPoints = points.Select(ToPoint).ToArray();
        if (!TryApplyModifyTool(toolName, wholeEntityIds, toolPoints))
        {
            _ = InvokeAsync(StateHasChanged);
            return;
        }

        _activeTool = null;
        ResetSelection();
        _ = InvokeAsync(StateHasChanged);
    }

    private void OnPowerTrimRequested(string targetKey, CanvasPointDto point)
    {
        if (!TryGetEntityIdForOperationTarget(targetKey, out var entityId)
            || FindEntity(entityId) is not LineEntity)
        {
            _status = "Power trim/extend needs a line target.";
            _ = InvokeAsync(StateHasChanged);
            return;
        }

        if (!DrawingModifyService.TryPowerTrimOrExtendLine(
                _document,
                entityId,
                ToPoint(point),
                CreateEntityId,
                out var nextDocument))
        {
            _status = "Power trim/extend needs another line crossing the picked line or its extension.";
            _ = InvokeAsync(StateHasChanged);
            return;
        }

        ApplyDocumentChange(nextDocument, "Power trim/extend applied. Click another line section, or Esc to cancel.");
        _activeTool = WorkbenchTool.PowerTrim;
        ResetSelection();
        _ = InvokeAsync(StateHasChanged);
    }

    private void OnToolCanceled()
    {
        if (_activeTool is null)
        {
            return;
        }

        _activeTool = null;
        _status = "Selection active.";
        _ = InvokeAsync(StateHasChanged);
    }

    private void ActivateTool(WorkbenchTool tool, string status)
    {
        _activeTool = tool;
        _status = status;
    }

    private void UndoLastDocumentChange()
    {
        if (!_undoStack.TryPop(out var previousDocument))
        {
            return;
        }

        _redoStack.Push(_document);
        _document = previousDocument;
        _exportText = string.Empty;
        ResetSelection();
        _status = "Undo applied.";
    }

    private void RedoLastDocumentChange()
    {
        if (!_redoStack.TryPop(out var nextDocument))
        {
            return;
        }

        _undoStack.Push(_document);
        _document = nextDocument;
        _exportText = string.Empty;
        ResetSelection();
        _status = "Redo applied.";
    }

    private async Task FitAsync()
    {
        if (_canvas is not null)
        {
            await _canvas.FitToExtentsAsync();
            _status = "Fit extents applied.";
        }
    }

    private void MoveBoundsToOrigin()
    {
        ApplyDocumentChange(
            DrawingPrepService.MoveBoundsMinimumToOrigin(_document),
            "Moved drawing bounds minimum to global origin.");
    }

    private void DeleteSelectedGeometry()
    {
        var result = SelectionDeleteResolver.DeleteSelection(_document, _selectedEntityIds);
        if (result.DeletedCount == 0)
        {
            _status = "Select whole geometry, polyline segments, or dimensions before deleting.";
            return;
        }

        ApplyDocumentChange(
            result.Document,
            FormatDeleteStatus(result));
        ResetSelection();
        _ = InvokeAsync(StateHasChanged);
    }

    private void DeleteSelectedGeometry(IReadOnlySet<string> selectedEntityIds)
    {
        OnSelectionChanged(selectedEntityIds);
        DeleteSelectedGeometry();
    }

    private void ToggleConstructionModeOrSelection()
    {
        var selectedWholeEntityIds = GetWholeEntityIdsForOperations().ToArray();
        if (selectedWholeEntityIds.Length == 0)
        {
            if (_selectedEntityIds.Count > 0)
            {
                _status = "Construction conversion needs whole selected geometry. Clear selection to enter construction convert.";
                return;
            }

            _constructionMode = false;
            ActivateTool(
                WorkbenchTool.Construction,
                "Construction convert active. Click geometry to toggle construction state. Esc to cancel.");
            return;
        }

        ToggleConstructionForEntityIds(
            selectedWholeEntityIds,
            clearTool: true);
    }

    private void ToggleConstructionForEntityIds(
        IReadOnlyCollection<string> entityIds,
        string? modalStatus = null,
        bool clearTool = true)
    {
        var result = DrawingConstructionService.ToggleSelected(_document, entityIds);
        if (result.ChangedCount == 0)
        {
            _status = "Select whole geometry before converting construction state.";
            return;
        }

        ApplyDocumentChange(
            result.Document,
            modalStatus ?? (result.IsConstruction
                ? $"Converted {result.ChangedCount} selected entities to construction geometry."
                : $"Converted {result.ChangedCount} selected entities to normal geometry."));
        _constructionMode = false;
        if (clearTool)
        {
            _activeTool = null;
        }
    }

    private bool TryApplyModifyTool(string toolName, IReadOnlyList<string> selectedEntityIds, IReadOnlyList<Point2> points)
    {
        var normalizedTool = NormalizeToolName(toolName);
        var before = _document;
        DrawingDocument nextDocument;
        string status;

        switch (normalizedTool)
        {
            case "translate" when points.Count >= 2:
                nextDocument = DrawingModifyService.TranslateSelected(_document, selectedEntityIds, points[0], points[1]);
                status = $"Translated {selectedEntityIds.Count} selected entities.";
                break;
            case "rotate" when points.Count >= 3:
                nextDocument = DrawingModifyService.RotateSelected(_document, selectedEntityIds, points[0], points[1], points[2]);
                status = $"Rotated {selectedEntityIds.Count} selected entities.";
                break;
            case "scale" when points.Count >= 3:
                nextDocument = DrawingModifyService.ScaleSelected(_document, selectedEntityIds, points[0], points[1], points[2]);
                status = $"Scaled {selectedEntityIds.Count} selected entities.";
                break;
            case "mirror" when points.Count >= 2:
                nextDocument = DrawingModifyService.MirrorSelected(_document, selectedEntityIds, points[0], points[1]);
                status = $"Mirrored {selectedEntityIds.Count} selected entities.";
                break;
            case "offset" when points.Count >= 1:
                if (!DrawingModifyService.TryOffsetSelected(
                        _document,
                        selectedEntityIds,
                        points[0],
                        CreateEntityId,
                        out nextDocument,
                        out var offsetCount))
                {
                    _status = "Offset needs a line, circle, arc, or polyline and a distinct through point.";
                    return false;
                }

                status = $"Offset {offsetCount} selected entities.";
                break;
            case "linearpattern" when points.Count >= 2:
                if (!DrawingModifyService.TryLinearPatternSelected(
                        _document,
                        selectedEntityIds,
                        points[0],
                        points[1],
                        DefaultPatternInstanceCount,
                        CreateEntityId,
                        out nextDocument,
                        out var linearCount))
                {
                    _status = "Linear pattern needs a nonzero spacing vector.";
                    return false;
                }

                status = $"Linear pattern added {linearCount} entities.";
                break;
            case "circularpattern" when points.Count >= 3:
                if (!DrawingModifyService.TryCircularPatternSelected(
                        _document,
                        selectedEntityIds,
                        points[0],
                        points[1],
                        points[2],
                        DefaultPatternInstanceCount,
                        CreateEntityId,
                        out nextDocument,
                        out var circularCount))
                {
                    _status = "Circular pattern needs a center, reference point, and distinct angle point.";
                    return false;
                }

                status = $"Circular pattern added {circularCount} entities.";
                break;
            default:
                _status = $"{FormatCreatedToolName(toolName)} needs more points.";
                return false;
        }

        if (ReferenceEquals(before, nextDocument))
        {
            _status = $"{FormatCreatedToolName(toolName)} did not change the drawing.";
            return false;
        }

        ApplyDocumentChange(nextDocument, status);
        return true;
    }

    private void FilletSelectedLines()
    {
        if (!DrawingModifyService.TryFilletSelectedLines(
                _document,
                GetWholeEntityIdsForOperations(),
                GetDefaultCornerDistance(),
                out var nextDocument))
        {
            _status = "Fillet needs exactly two nonparallel selected lines with enough length.";
            return;
        }

        ApplyDocumentChange(nextDocument, "Filleted selected lines.");
        ResetSelection();
    }

    private void ChamferSelectedLines()
    {
        if (!DrawingModifyService.TryChamferSelectedLines(
                _document,
                GetWholeEntityIdsForOperations(),
                GetDefaultCornerDistance(),
                out var nextDocument))
        {
            _status = "Chamfer needs exactly two nonparallel selected lines with enough length.";
            return;
        }

        ApplyDocumentChange(nextDocument, "Chamfered selected lines.");
        ResetSelection();
    }

    private double GetDefaultCornerDistance()
    {
        var bounds = Bounds;
        var size = Math.Max(bounds.Width, bounds.Height);
        return Math.Max(0.1, size * 0.05);
    }

    private async Task SyncSelectionFromCanvasAsync()
    {
        if (_canvas is not null)
        {
            await _canvas.SyncSelectionFromCanvasAsync();
        }
    }

    private bool TrySplitSelectedLineAtPoint()
    {
        if (!LineSplitSelectionResolver.TryResolveLineAndPoint(
                _document,
                _selectedEntityIds,
                out var lineEntityId,
                out var point))
        {
            return false;
        }

        return SplitLineAtPoint(lineEntityId, point);
    }

    private bool SplitLineAtPoint(string lineEntityId, Point2 point)
    {
        var newLineId = CreateEntityId("line");
        if (!LineSplitService.TrySplitLineAtPoint(_document, lineEntityId, point, newLineId, out var nextDocument))
        {
            _status = "Split point must lie inside the selected line.";
            _ = InvokeAsync(StateHasChanged);
            return false;
        }

        var stayModal = _activeTool == WorkbenchTool.SplitAtPoint;
        ApplyDocumentChange(
            nextDocument,
            stayModal
                ? "Split line at point. Click another line to split, or Esc to cancel."
                : "Split line at point.");
        _activeTool = stayModal ? WorkbenchTool.SplitAtPoint : null;
        ResetSelection();
        _ = InvokeAsync(StateHasChanged);
        return true;
    }

    private void AddSketchDimension()
    {
        if (!SketchCommandFactory.TryBuildDimension(
                _document,
                _selectedEntityIds,
                CreateDimensionId(),
                out var dimension,
                out var status,
                _activeSelectionKey))
        {
            _status = status;
            _ = InvokeAsync(StateHasChanged);
            return;
        }

        ApplyDocumentChange(SketchDimensionSolverService.ApplyDimension(_document, dimension), status);
        _ = InvokeAsync(StateHasChanged);
    }

    private void OnSketchDimensionPlacementRequested(
        IReadOnlyList<string> selectionKeys,
        CanvasPointDto anchor,
        bool radialDiameter)
    {
        if (!SketchCommandFactory.TryBuildDimension(
                _document,
                selectionKeys,
                CreateDimensionId(),
                out var dimension,
                out var status,
                anchorOverride: ToPoint(anchor),
                radialDiameter: radialDiameter))
        {
            _status = status;
            _ = InvokeAsync(StateHasChanged);
            return;
        }

        ApplyDocumentChange(SketchDimensionSolverService.ApplyDimension(_document, dimension), status);
        _activeTool = null;
        ResetSelection();
        _ = InvokeAsync(StateHasChanged);
    }

    private void AddSketchConstraint(SketchConstraintKind kind)
    {
        if (!SketchCommandFactory.TryBuildConstraint(
                _document,
                _selectedEntityIds,
                kind,
                CreateConstraintId(kind),
                out var constraint,
                out var status,
                _activeSelectionKey))
        {
            _status = status;
            _ = InvokeAsync(StateHasChanged);
            return;
        }

        var nextDocument = SketchConstraintService.ApplyConstraint(_document, constraint);
        var appliedConstraint = nextDocument.Constraints.FirstOrDefault(candidate => candidate.Id == constraint.Id);
        var appliedStatus = appliedConstraint?.State == SketchConstraintState.Unsatisfied
            ? $"{status} Constraint is currently unsatisfied."
            : status;
        ApplyDocumentChange(nextDocument, appliedStatus);
        _ = InvokeAsync(StateHasChanged);
    }

    private void OnSketchDimensionValueChanged(string dimensionId, double value)
    {
        if (!double.IsFinite(value) || value <= 0)
        {
            _status = "Dimension edit canceled.";
            _ = InvokeAsync(StateHasChanged);
            return;
        }

        var existing = _document.Dimensions.FirstOrDefault(
            dimension => StringComparer.Ordinal.Equals(dimension.Id, dimensionId));
        if (existing is null)
        {
            _status = "Dimension no longer exists.";
            _ = InvokeAsync(StateHasChanged);
            return;
        }

        var nextDimension = new SketchDimension(
            existing.Id,
            existing.Kind,
            existing.ReferenceKeys,
            value,
            existing.Anchor,
            isDriving: true);
        ApplyDocumentChange(
            SketchDimensionSolverService.ApplyDimension(_document, nextDimension),
            $"Updated {FormatDimensionKind(existing.Kind)} dimension to {FormatNumber(value)}.");
        _ = InvokeAsync(StateHasChanged);
    }

    private void OnSketchDimensionAnchorChanged(string dimensionId, CanvasPointDto anchor)
    {
        var existing = _document.Dimensions.FirstOrDefault(
            dimension => StringComparer.Ordinal.Equals(dimension.Id, dimensionId));
        if (existing is null)
        {
            _status = "Dimension no longer exists.";
            _ = InvokeAsync(StateHasChanged);
            return;
        }

        var nextAnchor = ToPoint(anchor);
        var nextDimension = new SketchDimension(
            existing.Id,
            existing.Kind,
            existing.ReferenceKeys,
            existing.Value,
            nextAnchor,
            existing.IsDriving);
        var nextDimensions = _document.Dimensions
            .Select(dimension => StringComparer.Ordinal.Equals(dimension.Id, dimensionId) ? nextDimension : dimension)
            .ToArray();
        ApplyDocumentChange(
            new DrawingDocument(_document.Entities, nextDimensions, _document.Constraints),
            "Moved dimension.");
        _ = InvokeAsync(StateHasChanged);
    }

    private void OnGeometryDragRequested(string selectionKey, CanvasPointDto dragStart, CanvasPointDto dragEnd)
    {
        if (!SketchGeometryDragService.TryApplyDrag(
                _document,
                selectionKey,
                ToPoint(dragStart),
                ToPoint(dragEnd),
                out var nextDocument,
                out var status))
        {
            _status = status;
            _ = InvokeAsync(StateHasChanged);
            return;
        }

        ApplyDocumentChange(nextDocument, status);
        _ = InvokeAsync(StateHasChanged);
    }

    private bool TryGetLineIdForSplitTarget(string targetKey, out string lineEntityId)
    {
        if (FindEntity(targetKey) is LineEntity)
        {
            lineEntityId = targetKey;
            return true;
        }

        if (TryParsePointSelectionEntityId(targetKey, out var pointEntityId)
            && FindEntity(pointEntityId) is LineEntity)
        {
            lineEntityId = pointEntityId;
            return true;
        }

        var selectedLineIds = _selectedEntityIds
            .Where(selectionKey => FindEntity(selectionKey) is LineEntity)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (selectedLineIds.Length == 1)
        {
            lineEntityId = selectedLineIds[0];
            return true;
        }

        lineEntityId = string.Empty;
        return false;
    }

    private bool TryGetEntityIdForOperationTarget(string targetKey, out string entityId)
    {
        if (FindEntity(targetKey) is not null)
        {
            entityId = targetKey;
            return true;
        }

        if (TryParsePointSelectionEntityId(targetKey, out var pointEntityId)
            && FindEntity(pointEntityId) is not null)
        {
            entityId = pointEntityId;
            return true;
        }

        if (TryParseSegmentSelectionKey(targetKey, out var segmentEntityId, out _)
            && FindEntity(segmentEntityId) is not null)
        {
            entityId = segmentEntityId;
            return true;
        }

        entityId = string.Empty;
        return false;
    }

    private void MoveSelectedPointToOrigin()
    {
        if (!SelectionPointResolver.TryGetPointToOriginReference(_document, _selectedEntityIds, out var point))
        {
            _status = "Select one point, circle, or arc before moving a point to origin.";
            return;
        }

        ApplyDocumentChange(
            DrawingPrepService.MovePointToOrigin(_document, point),
            "Moved selected reference point to global origin.");
    }

    private void AlignSelectedVectorToX() => AlignSelectedVector(AxisDirection.X);

    private void AlignSelectedVectorToY() => AlignSelectedVector(AxisDirection.Y);

    private void ToggleOriginAxes()
    {
        _showOriginAxes = !_showOriginAxes;
        _status = _showOriginAxes
            ? "Origin axes enabled."
            : "Origin axes hidden.";
    }

    private void ToggleToolPanel() => _isToolPanelCollapsed = !_isToolPanelCollapsed;

    private void ToggleInspector() => _isInspectorCollapsed = !_isInspectorCollapsed;

    private void BeginToolPanelResize(PointerEventArgs args) => BeginDockPanelResize(DockResizeTarget.ToolPanel, args.ClientX);

    private void BeginInspectorResize(PointerEventArgs args) => BeginDockPanelResize(DockResizeTarget.Inspector, args.ClientX);

    private void BeginDockPanelResize(DockResizeTarget target, double clientX)
    {
        _resizeTarget = target;
        _resizeStartClientX = clientX;
        _resizeStartToolPanelWidth = _toolPanelWidth;
        _resizeStartInspectorWidth = _inspectorWidth;
    }

    private void OnWorkbenchPointerMove(PointerEventArgs args)
    {
        if (_resizeTarget is DockResizeTarget.None)
        {
            return;
        }

        var deltaX = args.ClientX - _resizeStartClientX;
        if (_resizeTarget is DockResizeTarget.ToolPanel)
        {
            _toolPanelWidth = Math.Clamp(
                _resizeStartToolPanelWidth + (int)Math.Round(deltaX),
                MinToolPanelWidth,
                MaxToolPanelWidth);
            return;
        }

        _inspectorWidth = Math.Clamp(
            _resizeStartInspectorWidth - (int)Math.Round(deltaX),
            MinInspectorWidth,
            MaxInspectorWidth);
    }

    private void EndDockPanelResize()
    {
        _resizeTarget = DockResizeTarget.None;
    }

    private void AlignSelectedVector(AxisDirection axis)
    {
        if (!SelectionVectorResolver.TryGetAlignmentVector(_document, _selectedEntityIds, _activeSelectionKey, out var vectorStart, out var vectorEnd))
        {
            _status = "Select one line or segment, or exactly two points, before aligning a vector.";
            return;
        }

        var before = _document;
        var after = DrawingPrepService.AlignVectorToAxis(_document, vectorStart, vectorEnd, axis);
        if (ReferenceEquals(before, after))
        {
            _status = "Selected vector has no usable length.";
            return;
        }

        ApplyDocumentChange(after, $"Aligned selected vector to global {axis}.");
    }

    private void RotateDocumentAboutBoundsCenter(double degrees, string status)
    {
        ApplyDocumentChange(
            DrawingPrepService.RotateAboutBoundsCenter(_document, degrees),
            status);
    }

    private void ApplyDocumentChange(DrawingDocument nextDocument, string status)
    {
        if (ReferenceEquals(_document, nextDocument))
        {
            _status = status;
            return;
        }

        _undoStack.Push(_document);
        _redoStack.Clear();
        _document = nextDocument;
        _exportText = string.Empty;
        _status = status;
    }

    private void ClearHistory()
    {
        _undoStack.Clear();
        _redoStack.Clear();
    }

    private void SetGrainDirection(GrainDirection grainDirection)
    {
        _grainDirection = grainDirection;
        _status = grainDirection switch
        {
            GrainDirection.GlobalX => "Grain annotation set to global X.",
            GrainDirection.GlobalY => "Grain annotation set to global Y.",
            _ => "Grain annotation cleared."
        };
    }

    private void GenerateDxfExport()
    {
        _exportText = DxfDocumentWriter.Write(_document);
        _status = "Generated ASCII DXF export text for supported entities.";
    }

    private async Task DownloadDxfAsync()
    {
        var exportText = DxfDocumentWriter.Write(_document);
        var downloadName = DxfDownloadFileName.FromSourceName(_fileName);
        _exportText = exportText;
        _downloadModule ??= await JsRuntime.InvokeAsync<IJSObjectReference>("import", DownloadModulePath);
        await _downloadModule.InvokeVoidAsync(
            "downloadTextFile",
            downloadName,
            exportText,
            "application/dxf;charset=utf-8");
        _status = $"Saved {downloadName}.";
    }

    private void OnHoveredEntityChanged(string? entityId)
    {
        _hoveredEntityId = entityId;
        _ = InvokeAsync(StateHasChanged);
    }

    private void OnSelectionChanged(IReadOnlySet<string> selectedEntityIds)
    {
        _selectedEntityIds.Clear();
        foreach (var selectedEntityId in selectedEntityIds)
        {
            _selectedEntityIds.Add(selectedEntityId);
        }

        if (_activeSelectionKey is not null && !_selectedEntityIds.Contains(_activeSelectionKey))
        {
            _activeSelectionKey = null;
        }

        _ = InvokeAsync(StateHasChanged);
    }

    private void OnActiveSelectionChanged(string? activeSelectionKey)
    {
        _activeSelectionKey = activeSelectionKey is not null && _selectedEntityIds.Contains(activeSelectionKey)
            ? activeSelectionKey
            : null;
        _ = InvokeAsync(StateHasChanged);
    }

    private void ResetSelection()
    {
        _selectedEntityIds.Clear();
        _hoveredEntityId = null;
        _activeSelectionKey = null;
        _selectionResetToken++;
    }

    private bool TryGetSelectionMeasurement(out MeasurementResult measurement)
    {
        if (TryGetTwoSelectedPoints(out var firstPoint, out var secondPoint))
        {
            measurement = MeasurementService.Measure(firstPoint, secondPoint);
            return true;
        }

        if (TryGetSelectedSegment(out var segmentStart, out var segmentEnd))
        {
            measurement = MeasurementService.Measure(segmentStart, segmentEnd);
            return true;
        }

        var wholeEntityIds = GetWholeEntityIdsForOperations().ToArray();
        if (wholeEntityIds.Length == 0)
        {
            measurement = default;
            return false;
        }

        return DrawingPrepService.TryGetMeasurement(_document, wholeEntityIds, out measurement);
    }

    private IReadOnlyList<LiveReadoutItem> BuildLiveMeasurementReadouts()
    {
        var items = new List<LiveReadoutItem>
        {
            new("Sel", _selectedEntityIds.Count == 0 ? "none" : _selectedEntityIds.Count.ToString(CultureInfo.InvariantCulture)),
            new("Active", ActiveSelectionText),
            new("Affects", "Drawing"),
            new("Construction", _constructionMode ? "on" : "off")
        };

        if (_activeTool is { } activeTool)
        {
            items.Add(new LiveReadoutItem("Tool", ActiveToolLabel));
        }

        if (TryGetTwoSelectedPoints(out var firstPoint, out var secondPoint))
        {
            AddPointDeltaReadouts(items, firstPoint, secondPoint);
            return items;
        }

        var vectors = GetSelectedVectors().ToArray();
        if (vectors.Length == 2)
        {
            items.Add(new LiveReadoutItem("Angle", $"{FormatNumber(GetUnsignedAngleBetween(vectors[0], vectors[1]))} deg"));
            return items;
        }

        var entities = GetSelectedWholeEntities().ToArray();
        if (entities.Length == 1)
        {
            AddEntityReadouts(items, entities[0]);
            return items;
        }

        if (vectors.Length == 1)
        {
            AddVectorReadouts(items, vectors[0], "Length");
            return items;
        }

        if (entities.Length > 1)
        {
            var bounds = Bounds2.FromPoints(entities.SelectMany(GetEntityReadoutPoints));
            items.Add(new LiveReadoutItem("Bounds", FormatSize(bounds.Width, bounds.Height)));
            items.Add(new LiveReadoutItem("Entities", entities.Length.ToString(CultureInfo.InvariantCulture)));
            return items;
        }

        items.Add(new LiveReadoutItem("Bounds", FormatSize(Bounds.Width, Bounds.Height)));
        return items;
    }

    private void AddEntityReadouts(ICollection<LiveReadoutItem> items, DrawingEntity entity)
    {
        switch (entity)
        {
            case LineEntity line:
                AddVectorReadouts(items, new ReadoutVector(line.Start, line.End), "Length");
                break;
            case CircleEntity circle:
                items.Add(new LiveReadoutItem("Radius", FormatNumber(circle.Radius)));
                items.Add(new LiveReadoutItem("Diameter", FormatNumber(circle.Radius * 2)));
                items.Add(new LiveReadoutItem("Area", FormatNumber(Math.PI * circle.Radius * circle.Radius)));
                break;
            case ArcEntity arc:
                var sweep = GetArcSweepDegrees(arc);
                items.Add(new LiveReadoutItem("Radius", FormatNumber(arc.Radius)));
                items.Add(new LiveReadoutItem("Sweep", $"{FormatNumber(sweep)} deg"));
                items.Add(new LiveReadoutItem("Arc Len", FormatNumber(arc.Radius * sweep * Math.PI / 180.0)));
                break;
            case PointEntity point:
                items.Add(new LiveReadoutItem("X", FormatNumber(point.Location.X)));
                items.Add(new LiveReadoutItem("Y", FormatNumber(point.Location.Y)));
                break;
            case PolylineEntity polyline:
                AddPolylineReadouts(items, polyline.Vertices);
                break;
            case SplineEntity spline:
                var samples = spline.GetSamplePoints();
                items.Add(new LiveReadoutItem("Spline Len", FormatNumber(GetPathLength(samples))));
                items.Add(new LiveReadoutItem("Ctrl", spline.ControlPoints.Count.ToString(CultureInfo.InvariantCulture)));
                break;
        }
    }

    private void AddPolylineReadouts(ICollection<LiveReadoutItem> items, IReadOnlyList<Point2> vertices)
    {
        var pathLength = GetPathLength(vertices);
        if (IsClosedPath(vertices))
        {
            items.Add(new LiveReadoutItem("Perimeter", FormatNumber(pathLength)));
            items.Add(new LiveReadoutItem("Area", FormatNumber(Math.Abs(GetSignedArea(vertices)))));
            return;
        }

        items.Add(new LiveReadoutItem("Path Len", FormatNumber(pathLength)));
        items.Add(new LiveReadoutItem("Segments", Math.Max(0, vertices.Count - 1).ToString(CultureInfo.InvariantCulture)));
    }

    private static void AddPointDeltaReadouts(ICollection<LiveReadoutItem> items, Point2 first, Point2 second)
    {
        var measurement = MeasurementService.Measure(first, second);
        items.Add(new LiveReadoutItem("Delta X", FormatNumber(measurement.DeltaX)));
        items.Add(new LiveReadoutItem("Delta Y", FormatNumber(measurement.DeltaY)));
        items.Add(new LiveReadoutItem("Dist", FormatNumber(measurement.Distance)));
    }

    private static void AddVectorReadouts(ICollection<LiveReadoutItem> items, ReadoutVector vector, string lengthLabel)
    {
        var measurement = MeasurementService.Measure(vector.Start, vector.End);
        items.Add(new LiveReadoutItem(lengthLabel, FormatNumber(measurement.Distance)));
        items.Add(new LiveReadoutItem("Delta X", FormatNumber(measurement.DeltaX)));
        items.Add(new LiveReadoutItem("Delta Y", FormatNumber(measurement.DeltaY)));
        items.Add(new LiveReadoutItem("Angle", $"{FormatNumber(GetVectorAngleDegrees(vector))} deg"));
    }

    private IEnumerable<ReadoutVector> GetSelectedVectors()
    {
        foreach (var selectionKey in _selectedEntityIds)
        {
            if (TryParseSegmentSelectionKey(selectionKey, out var entityId, out var segmentIndex)
                && FindEntity(entityId) is PolylineEntity polyline
                && segmentIndex >= 0
                && segmentIndex < polyline.Vertices.Count - 1)
            {
                yield return new ReadoutVector(polyline.Vertices[segmentIndex], polyline.Vertices[segmentIndex + 1]);
                continue;
            }

            if (FindEntity(selectionKey) is LineEntity line)
            {
                yield return new ReadoutVector(line.Start, line.End);
            }
        }
    }

    private IEnumerable<DrawingEntity> GetSelectedWholeEntities() =>
        _selectedEntityIds
            .Where(selectionKey =>
                !selectionKey.Contains(PointKeySeparator, StringComparison.Ordinal)
                && !selectionKey.Contains(SegmentKeySeparator, StringComparison.Ordinal))
            .Select(FindEntity)
            .OfType<DrawingEntity>();

    private static IEnumerable<Point2> GetEntityReadoutPoints(DrawingEntity entity) =>
        entity switch
        {
            LineEntity line => new[] { line.Start, line.End },
            CircleEntity circle => new[]
            {
                new Point2(circle.Center.X - circle.Radius, circle.Center.Y - circle.Radius),
                new Point2(circle.Center.X + circle.Radius, circle.Center.Y + circle.Radius)
            },
            ArcEntity arc => arc.GetSamplePoints(32),
            PointEntity point => new[] { point.Location },
            PolylineEntity polyline => polyline.Vertices,
            SplineEntity spline => spline.GetSamplePoints(),
            _ => Array.Empty<Point2>()
        };

    private static double GetPathLength(IReadOnlyList<Point2> points)
    {
        var length = 0d;
        for (var index = 1; index < points.Count; index++)
        {
            length += MeasurementService.Measure(points[index - 1], points[index]).Distance;
        }

        return length;
    }

    private static bool IsClosedPath(IReadOnlyList<Point2> points) =>
        points.Count > 2
        && MeasurementService.Measure(points[0], points[^1]).Distance <= 0.000001;

    private static double GetSignedArea(IReadOnlyList<Point2> points)
    {
        var area = 0d;
        for (var index = 1; index < points.Count; index++)
        {
            area += points[index - 1].X * points[index].Y - points[index].X * points[index - 1].Y;
        }

        return area / 2.0;
    }

    private static double GetArcSweepDegrees(ArcEntity arc)
    {
        var sweep = arc.EndAngleDegrees - arc.StartAngleDegrees;
        while (sweep < 0)
        {
            sweep += 360.0;
        }

        return sweep;
    }

    private static double GetUnsignedAngleBetween(ReadoutVector first, ReadoutVector second)
    {
        var firstAngle = GetVectorAngleDegrees(first);
        var secondAngle = GetVectorAngleDegrees(second);
        var delta = Math.Abs(NormalizeSignedDegrees(secondAngle - firstAngle));
        return delta > 180 ? 360 - delta : delta;
    }

    private static double GetVectorAngleDegrees(ReadoutVector vector) =>
        Math.Atan2(vector.End.Y - vector.Start.Y, vector.End.X - vector.Start.X) * 180.0 / Math.PI;

    private static double NormalizeSignedDegrees(double angle)
    {
        var normalized = angle % 360.0;
        if (normalized > 180.0)
        {
            normalized -= 360.0;
        }
        else if (normalized <= -180.0)
        {
            normalized += 360.0;
        }

        return normalized;
    }

    private bool TryGetTwoSelectedPoints(out Point2 first, out Point2 second)
    {
        Point2? firstPoint = null;
        Point2? secondPoint = null;

        foreach (var selectionKey in _selectedEntityIds)
        {
            if (!TryGetPointFromSelectionKey(selectionKey, out var point))
            {
                continue;
            }

            if (firstPoint is null)
            {
                firstPoint = point;
                continue;
            }

            secondPoint = point;
            break;
        }

        if (firstPoint is { } firstValue && secondPoint is { } secondValue)
        {
            first = firstValue;
            second = secondValue;
            return true;
        }

        first = default;
        second = default;
        return false;
    }

    private bool TryGetSelectedSegment(out Point2 start, out Point2 end)
    {
        foreach (var selectionKey in _selectedEntityIds)
        {
            if (!TryParseSegmentSelectionKey(selectionKey, out var entityId, out var segmentIndex))
            {
                continue;
            }

            if (FindEntity(entityId) is PolylineEntity polyline
                && segmentIndex >= 0
                && segmentIndex < polyline.Vertices.Count - 1)
            {
                start = polyline.Vertices[segmentIndex];
                end = polyline.Vertices[segmentIndex + 1];
                return true;
            }
        }

        start = default;
        end = default;
        return false;
    }

    private bool TryGetPointFromSelectionKey(string selectionKey, out Point2 point)
    {
        var separatorIndex = selectionKey.IndexOf(PointKeySeparator, StringComparison.Ordinal);
        if (separatorIndex < 0)
        {
            point = default;
            return false;
        }

        var tail = selectionKey[(separatorIndex + PointKeySeparator.Length)..];
        var parts = tail.Split('|', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3
            || !double.TryParse(parts[^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
            || !double.TryParse(parts[^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
        {
            point = default;
            return false;
        }

        point = new Point2(x, y);
        return true;
    }

    private bool TryParseSegmentSelectionKey(string selectionKey, out string entityId, out int segmentIndex)
    {
        var separatorIndex = selectionKey.IndexOf(SegmentKeySeparator, StringComparison.Ordinal);
        if (separatorIndex < 0)
        {
            entityId = string.Empty;
            segmentIndex = default;
            return false;
        }

        entityId = selectionKey[..separatorIndex];
        return int.TryParse(
            selectionKey[(separatorIndex + SegmentKeySeparator.Length)..],
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out segmentIndex);
    }

    private static bool TryParsePointSelectionEntityId(string selectionKey, out string entityId)
    {
        var separatorIndex = selectionKey.IndexOf(PointKeySeparator, StringComparison.Ordinal);
        if (separatorIndex < 0)
        {
            entityId = string.Empty;
            return false;
        }

        entityId = selectionKey[..separatorIndex];
        return !string.IsNullOrWhiteSpace(entityId);
    }

    private IEnumerable<string> GetWholeEntityIdsForOperations() =>
        _selectedEntityIds
            .Where(selectionKey =>
                !selectionKey.Contains(PointKeySeparator, StringComparison.Ordinal)
                && !selectionKey.Contains(SegmentKeySeparator, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal);

    private DrawingEntity? FindEntity(string entityId) =>
        _document.Entities.FirstOrDefault(entity =>
            StringComparer.Ordinal.Equals(entity.Id.Value, entityId));

    private static string FormatSelectionKey(string? selectionKey)
    {
        if (string.IsNullOrWhiteSpace(selectionKey))
        {
            return "none";
        }

        var pointIndex = selectionKey.IndexOf(PointKeySeparator, StringComparison.Ordinal);
        if (pointIndex >= 0)
        {
            var entityId = selectionKey[..pointIndex];
            var tail = selectionKey[(pointIndex + PointKeySeparator.Length)..];
            var parts = tail.Split('|', StringSplitOptions.RemoveEmptyEntries);
            var label = parts.Length >= 3 ? string.Join(" ", parts[..^2]) : "point";
            return $"{entityId} {label}";
        }

        var segmentIndex = selectionKey.IndexOf(SegmentKeySeparator, StringComparison.Ordinal);
        if (segmentIndex >= 0)
        {
            return $"{selectionKey[..segmentIndex]} segment {selectionKey[(segmentIndex + SegmentKeySeparator.Length)..]}";
        }

        return selectionKey;
    }

    private static string FormatRange(double min, double max) => $"{FormatNumber(min)} to {FormatNumber(max)}";

    private static string FormatSize(double width, double height) => $"{FormatNumber(width)} x {FormatNumber(height)}";

    private static string FormatNumber(double value) => Round(value).ToString("0.###", CultureInfo.InvariantCulture);

    private static double Round(double value) => Math.Round(value, 4);

    private IEnumerable<DrawingEntity> CreateEntitiesForTool(string toolName, IReadOnlyList<Point2> points)
    {
        var normalizedTool = NormalizeToolName(toolName);
        if (normalizedTool == "point" && points.Count >= 1)
        {
            yield return new PointEntity(CreateEntityId("point"), points[0], _constructionMode);
            yield break;
        }

        if (points.Count < 2)
        {
            yield break;
        }

        var first = points[0];
        var second = points[1];
        var isConstruction = _constructionMode;
        switch (normalizedTool)
        {
            case "line":
                yield return new LineEntity(CreateEntityId("line"), first, second, isConstruction);
                break;
            case "midpointline":
                var mirroredEndpoint = new Point2((2 * first.X) - second.X, (2 * first.Y) - second.Y);
                yield return new LineEntity(CreateEntityId("line"), mirroredEndpoint, second, isConstruction);
                break;
            case "twopointrectangle":
                var oppositeA = new Point2(second.X, first.Y);
                var oppositeB = new Point2(first.X, second.Y);
                yield return new LineEntity(CreateEntityId("rect"), first, oppositeA, isConstruction);
                yield return new LineEntity(CreateEntityId("rect"), oppositeA, second, isConstruction);
                yield return new LineEntity(CreateEntityId("rect"), second, oppositeB, isConstruction);
                yield return new LineEntity(CreateEntityId("rect"), oppositeB, first, isConstruction);
                break;
            case "centerrectangle":
                var centerCorners = SketchRectangleGeometry.GetCenterRectangleCorners(first, second);
                yield return new LineEntity(CreateEntityId("rect"), centerCorners[0], centerCorners[1], isConstruction);
                yield return new LineEntity(CreateEntityId("rect"), centerCorners[1], centerCorners[2], isConstruction);
                yield return new LineEntity(CreateEntityId("rect"), centerCorners[2], centerCorners[3], isConstruction);
                yield return new LineEntity(CreateEntityId("rect"), centerCorners[3], centerCorners[0], isConstruction);
                break;
            case "alignedrectangle" when points.Count >= 3:
                var corners = GetAlignedRectangleCorners(first, second, points[2]);
                if (corners is not null)
                {
                    yield return new LineEntity(CreateEntityId("rect"), corners[0], corners[1], isConstruction);
                    yield return new LineEntity(CreateEntityId("rect"), corners[1], corners[2], isConstruction);
                    yield return new LineEntity(CreateEntityId("rect"), corners[2], corners[3], isConstruction);
                    yield return new LineEntity(CreateEntityId("rect"), corners[3], corners[0], isConstruction);
                }

                break;
            case "centercircle":
                var radius = Math.Sqrt(Math.Pow(second.X - first.X, 2) + Math.Pow(second.Y - first.Y, 2));
                if (radius > 0.000001)
                {
                    yield return new CircleEntity(CreateEntityId("circle"), first, radius, isConstruction);
                }

                break;
            case "threepointcircle" when points.Count >= 3:
                var circle = SketchArcGeometry.GetThreePointCircle(first, second, points[2]);
                if (circle is not null)
                {
                    yield return new CircleEntity(CreateEntityId("circle"), circle.Value.Center, circle.Value.Radius, isConstruction);
                }

                break;
            case "threepointarc" when points.Count >= 3:
                var threePointArc = SketchArcGeometry.GetThreePointArc(first, second, points[2]);
                if (threePointArc is not null)
                {
                    yield return new ArcEntity(
                        CreateEntityId("arc"),
                        threePointArc.Value.Center,
                        threePointArc.Value.Radius,
                        threePointArc.Value.StartAngleDegrees,
                        threePointArc.Value.EndAngleDegrees,
                        isConstruction);
                }

                break;
            case "tangentarc" when points.Count >= 3:
                var tangentArc = SketchArcGeometry.GetTangentArc(first, second, points[2]);
                if (tangentArc is not null)
                {
                    yield return new ArcEntity(
                        CreateEntityId("arc"),
                        tangentArc.Value.Center,
                        tangentArc.Value.Radius,
                        tangentArc.Value.StartAngleDegrees,
                        tangentArc.Value.EndAngleDegrees,
                        isConstruction);
                }

                break;
            case "centerpointarc" when points.Count >= 3:
                var centerPointArc = SketchArcGeometry.GetCenterPointArc(first, second, points[2]);
                if (centerPointArc is not null)
                {
                    yield return new ArcEntity(
                        CreateEntityId("arc"),
                        centerPointArc.Value.Center,
                        centerPointArc.Value.Radius,
                        centerPointArc.Value.StartAngleDegrees,
                        centerPointArc.Value.EndAngleDegrees,
                        isConstruction);
                }

                break;
        }
    }

    private static Point2[]? GetAlignedRectangleCorners(Point2 first, Point2 second, Point2 depthPoint)
    {
        var dx = second.X - first.X;
        var dy = second.Y - first.Y;
        var length = Math.Sqrt((dx * dx) + (dy * dy));
        if (length <= 0.000001)
        {
            return null;
        }

        var normalX = -dy / length;
        var normalY = dx / length;
        var depth = ((depthPoint.X - second.X) * normalX) + ((depthPoint.Y - second.Y) * normalY);
        var offset = new Point2(normalX * depth, normalY * depth);
        return new[]
        {
            first,
            second,
            new Point2(second.X + offset.X, second.Y + offset.Y),
            new Point2(first.X + offset.X, first.Y + offset.Y)
        };
    }

    private WorkbenchToolCommand Command(
        WorkbenchCommandId id,
        WorkbenchTool? tool,
        CadIconName icon,
        string label,
        bool disabled = false,
        bool? pressed = null,
        bool isFuture = false,
        string? tooltip = null) =>
        new(
            id,
            tool,
            icon,
            label,
            disabled,
            ResolvePressedState(tool, disabled, pressed),
            isFuture,
            IsConfirmedWorkingCommand(id),
            tooltip ?? BuildTooltip(id, label, tool, isFuture),
            ToolHotkeys.GetKey(id));

    private static bool IsConfirmedWorkingCommand(WorkbenchCommandId id) =>
        id is WorkbenchCommandId.Line
            or WorkbenchCommandId.MidpointLine
            or WorkbenchCommandId.TwoPointRectangle
            or WorkbenchCommandId.CenterRectangle
            or WorkbenchCommandId.CenterCircle
            or WorkbenchCommandId.ThreePointArc
            or WorkbenchCommandId.TangentArc
            or WorkbenchCommandId.CenterPointArc;

    private bool? ResolvePressedState(WorkbenchTool? tool, bool disabled, bool? pressed)
    {
        if (pressed.HasValue)
        {
            return pressed;
        }

        if (tool is null || disabled)
        {
            return null;
        }

        return _activeTool == tool;
    }

    private string BuildTooltip(WorkbenchCommandId id, string label, WorkbenchTool? tool, bool isFuture)
    {
        var hotkey = ToolHotkeys.GetKey(id);
        var hotkeyText = hotkey is null ? string.Empty : $" Hotkey: {hotkey}.";

        if (isFuture)
        {
            return $"{label}. Not implemented yet.";
        }

        return tool.HasValue
            ? $"{label}.{hotkeyText} Enters a modal tool; press Esc to return to selection."
            : $"{label}.{hotkeyText}".TrimEnd();
    }

    private void OnToolHotkeysChanged() => _ = InvokeAsync(StateHasChanged);

    [JSInvokable]
    public async Task<bool> OnToolHotkeyPressed(
        string key,
        bool ctrlKey,
        bool altKey,
        bool shiftKey,
        bool metaKey,
        bool isEditableTarget)
    {
        var press = new ToolHotkeyPress(key, ctrlKey, altKey, shiftKey, metaKey, isEditableTarget);
        if (!ToolHotkeys.TryResolve(press, out var commandId))
        {
            return false;
        }

        await InvokeAsync(async () =>
        {
            await InvokeWorkbenchCommand(commandId);
            StateHasChanged();
        });
        return true;
    }

    private EntityId CreateEntityId(string prefix)
    {
        _createdEntitySequence++;
        var id = $"{prefix}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds():x}-{_createdEntitySequence:x}";
        var existing = _document.Entities.Select(entity => entity.Id.Value).ToHashSet(StringComparer.Ordinal);
        var suffix = 0;
        var candidate = id;
        while (existing.Contains(candidate))
        {
            suffix++;
            candidate = $"{id}-{suffix}";
        }

        return EntityId.Create(candidate);
    }

    private string CreateDimensionId()
    {
        _createdDimensionSequence++;
        return CreateUniqueSketchId(
            "dim",
            _createdDimensionSequence,
            _document.Dimensions.Select(dimension => dimension.Id));
    }

    private string CreateConstraintId(SketchConstraintKind kind)
    {
        _createdConstraintSequence++;
        return CreateUniqueSketchId(
            kind.ToString().ToLowerInvariant(),
            _createdConstraintSequence,
            _document.Constraints.Select(constraint => constraint.Id));
    }

    private static string CreateUniqueSketchId(string prefix, int sequence, IEnumerable<string> existingIds)
    {
        var stem = $"{prefix}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds():x}-{sequence:x}";
        var existing = existingIds.ToHashSet(StringComparer.Ordinal);
        var suffix = 0;
        var candidate = stem;
        while (existing.Contains(candidate))
        {
            suffix++;
            candidate = $"{stem}-{suffix}";
        }

        return candidate;
    }

    private static bool TryGetImplementedSketchTool(WorkbenchCommandId commandId, out WorkbenchTool tool)
    {
        switch (commandId)
        {
            case WorkbenchCommandId.Line:
                tool = WorkbenchTool.Line;
                return true;
            case WorkbenchCommandId.MidpointLine:
                tool = WorkbenchTool.MidpointLine;
                return true;
            case WorkbenchCommandId.TwoPointRectangle:
                tool = WorkbenchTool.TwoPointRectangle;
                return true;
            case WorkbenchCommandId.CenterRectangle:
                tool = WorkbenchTool.CenterRectangle;
                return true;
            case WorkbenchCommandId.AlignedRectangle:
                tool = WorkbenchTool.AlignedRectangle;
                return true;
            case WorkbenchCommandId.CenterCircle:
                tool = WorkbenchTool.CenterCircle;
                return true;
            case WorkbenchCommandId.ThreePointCircle:
                tool = WorkbenchTool.ThreePointCircle;
                return true;
            case WorkbenchCommandId.ThreePointArc:
                tool = WorkbenchTool.ThreePointArc;
                return true;
            case WorkbenchCommandId.CenterPointArc:
                tool = WorkbenchTool.CenterPointArc;
                return true;
            case WorkbenchCommandId.Point:
                tool = WorkbenchTool.Point;
                return true;
            default:
                tool = default;
                return false;
        }
    }

    private static bool TryGetImplementedModifyTool(WorkbenchCommandId commandId, out WorkbenchTool tool)
    {
        switch (commandId)
        {
            case WorkbenchCommandId.Offset:
                tool = WorkbenchTool.Offset;
                return true;
            case WorkbenchCommandId.Translate:
                tool = WorkbenchTool.Translate;
                return true;
            case WorkbenchCommandId.Rotate:
                tool = WorkbenchTool.Rotate;
                return true;
            case WorkbenchCommandId.Scale:
                tool = WorkbenchTool.Scale;
                return true;
            case WorkbenchCommandId.Mirror:
                tool = WorkbenchTool.Mirror;
                return true;
            case WorkbenchCommandId.LinearPattern:
                tool = WorkbenchTool.LinearPattern;
                return true;
            case WorkbenchCommandId.CircularPattern:
                tool = WorkbenchTool.CircularPattern;
                return true;
            default:
                tool = default;
                return false;
        }
    }

    private static string GetToolPrompt(WorkbenchTool tool) => tool switch
    {
        WorkbenchTool.Line => "Pick points. Hover the last vertex to switch to tangent arc.",
        WorkbenchTool.CenterRectangle => "Pick center, then corner.",
        WorkbenchTool.AlignedRectangle => "Pick baseline start, baseline end, then depth.",
        WorkbenchTool.ThreePointCircle => "Pick three points on the circle.",
        WorkbenchTool.ThreePointArc => "Pick start point, through point, then end point.",
        WorkbenchTool.TangentArc => "Pick start, tangent direction, then endpoint. In a chain, hover the last vertex to switch back to line.",
        WorkbenchTool.CenterPointArc => "Pick center, start radius point, then end angle point.",
        WorkbenchTool.Point => "Pick a point on the canvas.",
        WorkbenchTool.Construction => "Click geometry to toggle construction state.",
        WorkbenchTool.PowerTrim => "Click a line section to trim, or click past an endpoint to extend.",
        WorkbenchTool.Offset => "Click the through side or radius point.",
        WorkbenchTool.Translate => "Pick from point, then to point.",
        WorkbenchTool.Rotate => "Pick center, reference point, then target point.",
        WorkbenchTool.Scale => "Pick center, reference radius, then target radius.",
        WorkbenchTool.Mirror => "Pick two points for the mirror axis.",
        WorkbenchTool.LinearPattern => "Pick from point, then spacing point.",
        WorkbenchTool.CircularPattern => "Pick center, reference point, then target angle point.",
        _ => "Pick two points on the canvas."
    };

    private static Point2 ToPoint(CanvasPointDto point) => new(point.X, point.Y);

    private static string NormalizeToolName(string toolName) =>
        toolName.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();

    private static string FormatCommandName(WorkbenchCommandId commandId) => commandId switch
    {
        WorkbenchCommandId.MidpointLine => "Midpoint line",
        WorkbenchCommandId.TwoPointRectangle => "Two-point rectangle",
        WorkbenchCommandId.CenterRectangle => "Center rectangle",
        WorkbenchCommandId.AlignedRectangle => "Aligned rectangle",
        WorkbenchCommandId.CenterCircle => "Center circle",
        WorkbenchCommandId.ThreePointCircle => "Three-point circle",
        WorkbenchCommandId.ThreePointArc => "Three-point arc",
        WorkbenchCommandId.TangentArc => "Tangent arc",
        WorkbenchCommandId.CenterPointArc => "Center point arc",
        WorkbenchCommandId.EllipticalArc => "Elliptical arc",
        WorkbenchCommandId.InscribedPolygon => "Inscribed polygon",
        WorkbenchCommandId.CircumscribedPolygon => "Circumscribed polygon",
        WorkbenchCommandId.SplineControlPoint => "Spline control point",
        WorkbenchCommandId.PowerTrim => "Power trim/extend",
        WorkbenchCommandId.DeleteSelection => "Delete selected geometry",
        WorkbenchCommandId.SplitAtPoint => "Split at point",
        WorkbenchCommandId.SaveDxf => "Save DXF",
        WorkbenchCommandId.LinearPattern => "Linear pattern",
        WorkbenchCommandId.CircularPattern => "Circular pattern",
        WorkbenchCommandId.Rotate90Clockwise => "Rotate 90 CW",
        WorkbenchCommandId.Rotate90CounterClockwise => "Rotate 90 CCW",
        WorkbenchCommandId.ToolHotkeys => "Tool hotkeys",
        _ => commandId.ToString()
    };

    private static string FormatCreatedToolName(string toolName) => NormalizeToolName(toolName) switch
    {
        "midpointline" => "Midpoint line",
        "twopointrectangle" => "Two-point rectangle",
        "centerrectangle" => "Center rectangle",
        "alignedrectangle" => "Aligned rectangle",
        "centercircle" => "Center circle",
        "threepointcircle" => "Three-point circle",
        "threepointarc" => "Three-point arc",
        "tangentarc" => "Tangent arc",
        "centerpointarc" => "Center point arc",
        "point" => "Point",
        "powertrim" => "Power trim/extend",
        "splitatpoint" => "Split at point",
        "offset" => "Offset",
        "translate" => "Translate",
        "rotate" => "Rotate",
        "scale" => "Scale",
        "mirror" => "Mirror",
        "linearpattern" => "Linear pattern",
        "circularpattern" => "Circular pattern",
        _ => "Line"
    };

    private static string FormatDimensionKind(SketchDimensionKind kind) => kind switch
    {
        SketchDimensionKind.LinearDistance => "linear",
        SketchDimensionKind.HorizontalDistance => "horizontal",
        SketchDimensionKind.VerticalDistance => "vertical",
        SketchDimensionKind.PointToLineDistance => "point-to-line",
        SketchDimensionKind.Radius => "radius",
        SketchDimensionKind.Diameter => "diameter",
        SketchDimensionKind.Angle => "angle",
        _ => kind.ToString()
    };

    private static string FormatDeleteStatus(SelectionDeleteResult result)
    {
        if (result.DeletedGeometryCount == 0 && result.DeletedDimensions > 0)
        {
            return result.DeletedDimensions == 1
                ? "Deleted 1 dimension."
                : $"Deleted {result.DeletedDimensions} dimensions.";
        }

        if (result.DeletedDimensions > 0)
        {
            return result.DeletedDimensions == 1
                ? $"Deleted {FormatGeometryDeleteSummary(result)} and 1 dimension."
                : $"Deleted {FormatGeometryDeleteSummary(result)} and {result.DeletedDimensions} dimensions.";
        }

        return FormatGeometryDeleteStatus(result);
    }

    private static string FormatGeometryDeleteStatus(SelectionDeleteResult result)
    {
        if (result.DeletedEntities > 0 && result.DeletedSegments > 0)
        {
            return $"Deleted {result.DeletedEntities} entities and {result.DeletedSegments} polyline segments.";
        }

        if (result.DeletedEntities > 0)
        {
            return result.DeletedEntities == 1
                ? "Deleted 1 entity."
                : $"Deleted {result.DeletedEntities} entities.";
        }

        return result.DeletedSegments == 1
            ? "Deleted 1 polyline segment."
            : $"Deleted {result.DeletedSegments} polyline segments.";
    }

    private static string FormatGeometryDeleteSummary(SelectionDeleteResult result)
    {
        if (result.DeletedEntities > 0 && result.DeletedSegments > 0)
        {
            return $"{result.DeletedEntities} entities, {result.DeletedSegments} polyline segments";
        }

        if (result.DeletedEntities > 0)
        {
            return result.DeletedEntities == 1
                ? "1 entity"
                : $"{result.DeletedEntities} entities";
        }

        return result.DeletedSegments == 1
            ? "1 polyline segment"
            : $"{result.DeletedSegments} polyline segments";
    }

    private sealed record LiveReadoutItem(string Label, string Value);

    private readonly record struct ReadoutVector(Point2 Start, Point2 End);
}

internal enum DockResizeTarget
{
    None,
    ToolPanel,
    Inspector
}

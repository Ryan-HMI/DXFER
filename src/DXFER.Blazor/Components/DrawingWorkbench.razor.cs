using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DXFER.CadIO;
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
    private const string HotkeyModulePath = "./_content/DXFER.Blazor/workbenchHotkeys.js?v=20260506-all-hotkey-keys";
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
    private DrawingDocument _document = CreateBlankDocument();
    private string _fileName = "Untitled.dxf";
    private string _status = "Blank drawing ready. Sketch geometry or open a DXF.";
    private string? _hoveredEntityId;
    private string? _activeSelectionKey;
    private string _exportText = string.Empty;
    private GrainDirection _grainDirection = GrainDirection.None;
    private WorkbenchTool? _activeTool;
    private bool _showOriginAxes = true;
    private bool _showAllConstraints;
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
    private PendingCircleSplit? _pendingCircleSplit;
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

    private bool CanAddSplinePoint =>
        GetSelectedWholeEntities()
            .OfType<SplineEntity>()
            .Where(spline => spline.FitPoints.Count >= 2)
            .Take(2)
            .Count() == 1;

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
            Command(
                WorkbenchCommandId.ShowConstraints,
                null,
                CadIconName.ShowConstraints,
                "Show constraints",
                pressed: _showAllConstraints,
                tooltip: "Show constraints. When off, constraints appear only while hovering referenced geometry."),
            Command(WorkbenchCommandId.ToolHotkeys, null, CadIconName.Hotkeys, "Tool hotkeys")
        }, "Display"),
        new WorkbenchToolGroup("Sketch", new[]
        {
            Command(WorkbenchCommandId.Line, WorkbenchTool.Line, CadIconName.Line, "Line"),
            Command(WorkbenchCommandId.MidpointLine, WorkbenchTool.MidpointLine, CadIconName.MidpointLine, "Midpoint line"),
            Command(WorkbenchCommandId.TwoPointRectangle, WorkbenchTool.TwoPointRectangle, CadIconName.Rectangle, "Two-point rectangle"),
            Command(WorkbenchCommandId.CenterRectangle, WorkbenchTool.CenterRectangle, CadIconName.CenterRectangle, "Center rectangle"),
            Command(WorkbenchCommandId.AlignedRectangle, WorkbenchTool.AlignedRectangle, CadIconName.AlignedRectangle, "Aligned rectangle"),
            Command(WorkbenchCommandId.CenterCircle, WorkbenchTool.CenterCircle, CadIconName.Circle, "Center circle"),
            Command(WorkbenchCommandId.ThreePointCircle, WorkbenchTool.ThreePointCircle, CadIconName.ThreePointCircle, "Three-point circle"),
            Command(WorkbenchCommandId.Ellipse, WorkbenchTool.Ellipse, CadIconName.Ellipse, "Ellipse"),
            Command(WorkbenchCommandId.ThreePointArc, WorkbenchTool.ThreePointArc, CadIconName.Arc, "Three-point arc"),
            Command(WorkbenchCommandId.CenterPointArc, WorkbenchTool.CenterPointArc, CadIconName.CenterPointArc, "Center point arc"),
            Command(WorkbenchCommandId.EllipticalArc, WorkbenchTool.EllipticalArc, CadIconName.EllipticalArc, "Elliptical arc"),
            Command(WorkbenchCommandId.Conic, WorkbenchTool.Conic, CadIconName.Conic, "Conic"),
            Command(WorkbenchCommandId.InscribedPolygon, WorkbenchTool.InscribedPolygon, CadIconName.InscribedPolygon, "Inscribed polygon"),
            Command(WorkbenchCommandId.CircumscribedPolygon, WorkbenchTool.CircumscribedPolygon, CadIconName.CircumscribedPolygon, "Circumscribed polygon"),
            Command(WorkbenchCommandId.Spline, WorkbenchTool.Spline, CadIconName.Spline, "Spline"),
            Command(WorkbenchCommandId.SplineControlPoint, WorkbenchTool.SplineControlPoint, CadIconName.SplineControlPoint, "Add spline point"),
            Command(WorkbenchCommandId.Point, WorkbenchTool.Point, CadIconName.Point, "Point"),
            Command(WorkbenchCommandId.Text, WorkbenchTool.Text, CadIconName.Text, "Text", disabled: true, isFuture: true),
            Command(WorkbenchCommandId.Slot, WorkbenchTool.Slot, CadIconName.Slot, "Slot")
        }, "Create"),
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
            Command(WorkbenchCommandId.AddSplinePoint, WorkbenchTool.AddSplinePoint, CadIconName.SplineControlPoint, "Add spline point", !CanAddSplinePoint),
            Command(WorkbenchCommandId.Offset, WorkbenchTool.Offset, CadIconName.Offset, "Offset", !CanModifySelectedGeometry),
            Command(WorkbenchCommandId.Fillet, null, CadIconName.Fillet, "Fillet", !CanFilletOrChamferSelectedLines),
            Command(WorkbenchCommandId.Chamfer, null, CadIconName.Chamfer, "Chamfer", !CanFilletOrChamferSelectedLines),
            Command(WorkbenchCommandId.Dimension, WorkbenchTool.Dimension, CadIconName.Dimension, "Dimension")
        }, "Edit"),
        new WorkbenchToolGroup("Transform", new[]
        {
            Command(WorkbenchCommandId.Translate, WorkbenchTool.Translate, CadIconName.Move, "Translate", !CanModifySelectedGeometry),
            Command(WorkbenchCommandId.Rotate, WorkbenchTool.Rotate, CadIconName.Rotate, "Rotate", !CanModifySelectedGeometry),
            Command(WorkbenchCommandId.Scale, WorkbenchTool.Scale, CadIconName.Scale, "Scale", !CanModifySelectedGeometry),
            Command(WorkbenchCommandId.Mirror, WorkbenchTool.Mirror, CadIconName.Mirror, "Mirror", !CanModifySelectedGeometry)
        }, "Move"),
        new WorkbenchToolGroup("Pattern", new[]
        {
            Command(WorkbenchCommandId.LinearPattern, WorkbenchTool.LinearPattern, CadIconName.LinearPattern, "Linear pattern", !CanModifySelectedGeometry),
            Command(WorkbenchCommandId.CircularPattern, WorkbenchTool.CircularPattern, CadIconName.CircularPattern, "Circular pattern", !CanModifySelectedGeometry)
        }, "Repeat", InitiallyOpen: false),
        new WorkbenchToolGroup("Constraints", new[]
        {
            Command(WorkbenchCommandId.Coincident, WorkbenchTool.Coincident, CadIconName.Coincident, "Coincident", tooltip: ConstraintTooltip(SketchConstraintKind.Coincident)),
            Command(WorkbenchCommandId.Concentric, WorkbenchTool.Concentric, CadIconName.Concentric, "Concentric", tooltip: ConstraintTooltip(SketchConstraintKind.Concentric)),
            Command(WorkbenchCommandId.Parallel, WorkbenchTool.Parallel, CadIconName.Parallel, "Parallel", tooltip: ConstraintTooltip(SketchConstraintKind.Parallel)),
            Command(WorkbenchCommandId.Tangent, WorkbenchTool.Tangent, CadIconName.Tangent, "Tangent", tooltip: ConstraintTooltip(SketchConstraintKind.Tangent)),
            Command(WorkbenchCommandId.Horizontal, WorkbenchTool.Horizontal, CadIconName.Horizontal, "Horizontal", tooltip: ConstraintTooltip(SketchConstraintKind.Horizontal)),
            Command(WorkbenchCommandId.Vertical, WorkbenchTool.Vertical, CadIconName.Vertical, "Vertical", tooltip: ConstraintTooltip(SketchConstraintKind.Vertical)),
            Command(WorkbenchCommandId.Perpendicular, WorkbenchTool.Perpendicular, CadIconName.Perpendicular, "Perpendicular", tooltip: ConstraintTooltip(SketchConstraintKind.Perpendicular)),
            Command(WorkbenchCommandId.Equal, WorkbenchTool.Equal, CadIconName.Equal, "Equal", tooltip: ConstraintTooltip(SketchConstraintKind.Equal)),
            Command(WorkbenchCommandId.Midpoint, WorkbenchTool.Midpoint, CadIconName.Midpoint, "Midpoint", tooltip: ConstraintTooltip(SketchConstraintKind.Midpoint)),
            Command(WorkbenchCommandId.Normal, WorkbenchTool.Normal, CadIconName.Normal, "Normal", disabled: true, isFuture: true),
            Command(WorkbenchCommandId.Symmetric, WorkbenchTool.Symmetric, CadIconName.Symmetric, "Symmetric", disabled: true, isFuture: true),
            Command(WorkbenchCommandId.Fix, WorkbenchTool.Fix, CadIconName.Fix, "Fix", tooltip: ConstraintTooltip(SketchConstraintKind.Fix))
        }, "Solve", InitiallyOpen: false),
        new WorkbenchToolGroup("Cleanup", CleanupCommands, "Prep", InitiallyOpen: false)
    };

    private IReadOnlyList<WorkbenchToolCommand> CleanupCommands => new[]
    {
        Command(WorkbenchCommandId.Rotate90Clockwise, null, CadIconName.Rotate90Clockwise, "Rotate 90 CW", !HasDocument),
        Command(WorkbenchCommandId.Rotate90CounterClockwise, null, CadIconName.Rotate90CounterClockwise, "Rotate 90 CCW", !HasDocument),
        Command(WorkbenchCommandId.BoundsToOrigin, null, CadIconName.BoundsToOrigin, "Bounds to origin", !HasDocument, tooltip: "Move drawing bounds minimum to global origin."),
        Command(WorkbenchCommandId.PointToOrigin, null, CadIconName.PointToOrigin, "Point to origin", !CanMoveSelectedPointToOrigin, tooltip: "Move the selected point to global origin."),
        Command(WorkbenchCommandId.VectorToX, null, CadIconName.VectorToX, "Vector to X", !CanAlignSelectedVector, tooltip: "Align the selected line, segment, or two selected points to global X."),
        Command(WorkbenchCommandId.VectorToY, null, CadIconName.VectorToY, "Vector to Y", !CanAlignSelectedVector, tooltip: "Align the selected line, segment, or two selected points to global Y."),
        Command(WorkbenchCommandId.RemoveDuplicates, null, CadIconName.RemoveDuplicates, "Remove duplicates", disabled: true, isFuture: true, tooltip: "Remove duplicate geometry. Planned cleanup command; not implemented yet.")
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
        WorkbenchTool.Ellipse => "Ellipse",
        WorkbenchTool.CenterPointArc => "Center point arc",
        WorkbenchTool.EllipticalArc => "Elliptical arc",
        WorkbenchTool.Conic => "Conic",
        WorkbenchTool.TangentArc => "Tangent arc",
        WorkbenchTool.InscribedPolygon => "Inscribed polygon",
        WorkbenchTool.CircumscribedPolygon => "Circumscribed polygon",
        WorkbenchTool.Spline => "Spline",
        WorkbenchTool.Bezier => "Bezier",
        WorkbenchTool.SplineControlPoint => "Add spline point",
        WorkbenchTool.Point => "Point",
        WorkbenchTool.Construction => "Construction",
        WorkbenchTool.PowerTrim => "Power trim/extend",
        WorkbenchTool.ThreePointArc => "Three-point arc",
        WorkbenchTool.SplitAtPoint => "Split at point",
        WorkbenchTool.AddSplinePoint => "Add spline point",
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
        WorkbenchTool.Ellipse => "Ellipse: click center, major radius point, then minor radius point. Esc: cancel.",
        WorkbenchTool.ThreePointArc => "Three-point arc: click start point, through point, then end point. Esc: cancel.",
        WorkbenchTool.TangentArc => "Tangent arc: click start point, tangent direction point, then end point. Esc: cancel.",
        WorkbenchTool.CenterPointArc => "Center point arc: click center, start radius point, then end angle point. Esc: cancel.",
        WorkbenchTool.EllipticalArc => "Elliptical arc: click center, major radius point, minor radius point, then end parameter point. Esc: cancel.",
        WorkbenchTool.Conic => "Conic: click start, control point, then end point. Esc: cancel.",
        WorkbenchTool.InscribedPolygon => "Inscribed polygon: click center, scroll for sides, then vertex radius. Esc: cancel.",
        WorkbenchTool.CircumscribedPolygon => "Circumscribed polygon: click center, scroll for sides, then side apothem. Esc: cancel.",
        WorkbenchTool.Spline => "Spline: click fit points, double-click to finish. Esc: cancel.",
        WorkbenchTool.Bezier => "Bezier: click four control points. Esc: cancel.",
        WorkbenchTool.SplineControlPoint => "Add spline point: click a fit-point spline where the new fit point belongs. Esc: cancel.",
        WorkbenchTool.Point => "Point: click to place a persistent sketch point. Esc: cancel.",
        WorkbenchTool.Slot => "Slot: click first center, second center, then radius point. Esc: cancel.",
        WorkbenchTool.Construction => "Construction: click geometry to toggle construction state. Esc: cancel.",
        WorkbenchTool.PowerTrim => "Power trim/extend: click a line, polyline, polygon, circle, arc, ellipse, spline, or point section to trim, or click past a line endpoint to extend. Esc: cancel.",
        WorkbenchTool.SplitAtPoint => "Split at point: click a line or arc, or pick two points on a circle. Esc: cancel.",
        WorkbenchTool.AddSplinePoint => "Add spline point: select one fit-point spline, then click the spline where the new fit point belongs. Esc: cancel.",
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
                    units = _document.Metadata.Units.ToString(),
                    mode = _document.Metadata.Mode.ToString(),
                    trustedSource = _document.Metadata.TrustedSource,
                    warnings = _document.Metadata.Warnings.Select(warning => new
                    {
                        warning.Code,
                        Severity = warning.Severity.ToString(),
                        warning.Message
                    }),
                    unsupportedEntityCounts = _document.Metadata.UnsupportedEntityCounts,
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
        try
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
            var document = WithOpenFileMetadata(DxfDocumentReader.Read(text), file.Name, text);

            if (document.Entities.Count == 0)
            {
                _status = "No supported DXF entities were found. V1 reads LINE, CIRCLE, ARC, POINT, LWPOLYLINE, POLYLINE, and SPLINE."
                    + FormatWarningSummary(document.Metadata.Warnings);
                return;
            }

            _document = document;
            _documentFitToken++;
            ClearHistory();
            ResetSelection();
            _status = $"Loaded {document.Entities.Count} supported entities from DXF."
                + FormatWarningSummary(document.Metadata.Warnings);
        }
        finally
        {
            await InvokeAsync(StateHasChanged);
        }
    }

    private static DrawingDocument WithOpenFileMetadata(
        DrawingDocument document,
        string fileName,
        string sourceText)
    {
        var metadata = document.Metadata with
        {
            SourceFileName = fileName,
            SourceSha256 = ComputeSha256(sourceText),
            TrustedSource = false
        };

        return new DrawingDocument(
            document.Entities,
            document.Dimensions,
            document.Constraints,
            metadata);
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

    private void NewBlankDocument()
    {
        _document = CreateBlankDocument();
        _documentFitToken++;
        _fileName = "Untitled.dxf";
        _status = "Blank drawing ready.";
        _exportText = string.Empty;
        _grainDirection = GrainDirection.None;
        _activeTool = null;
        _constructionMode = false;
        ClearHistory();
        ResetSelection();
    }

    private async Task InvokeWorkbenchCommand(WorkbenchCommandId commandId)
    {
        switch (commandId)
        {
            case WorkbenchCommandId.NewBlankDocument:
                NewBlankDocument();
                break;
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
            case WorkbenchCommandId.ShowConstraints:
                ToggleShowAllConstraints();
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
                        "Split at point active. Click a line or arc, or pick two points on a circle.");
                }

                break;
            case WorkbenchCommandId.PowerTrim:
                ActivateTool(
                    WorkbenchTool.PowerTrim,
                    "Power trim/extend active. Click a line, polyline, polygon, circle, arc, ellipse, spline, or point section to trim, or click past a line endpoint to extend.");
                break;
            case WorkbenchCommandId.AddSplinePoint:
            case WorkbenchCommandId.Offset:
            case WorkbenchCommandId.Translate:
            case WorkbenchCommandId.Rotate:
            case WorkbenchCommandId.Scale:
            case WorkbenchCommandId.Mirror:
            case WorkbenchCommandId.LinearPattern:
            case WorkbenchCommandId.CircularPattern:
                await SyncSelectionFromCanvasAsync();
                if (commandId == WorkbenchCommandId.AddSplinePoint && !CanAddSplinePoint)
                {
                    _status = "Add spline point needs exactly one selected fit-point spline.";
                    break;
                }

                if (commandId != WorkbenchCommandId.AddSplinePoint && !CanModifySelectedGeometry)
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
            case WorkbenchCommandId.Concentric:
            case WorkbenchCommandId.Parallel:
            case WorkbenchCommandId.Tangent:
            case WorkbenchCommandId.Horizontal:
            case WorkbenchCommandId.Vertical:
            case WorkbenchCommandId.Perpendicular:
            case WorkbenchCommandId.Equal:
            case WorkbenchCommandId.Midpoint:
            case WorkbenchCommandId.Fix:
                await SyncSelectionFromCanvasAsync();
                StartConstraintTool(commandId);
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
            case WorkbenchCommandId.RemoveDuplicates:
                _status = "Remove duplicates is planned for cleanup and is not implemented yet.";
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

        await InvokeAsync(StateHasChanged);
    }

    private void OnToolCommitRequested(
        string toolName,
        IReadOnlyList<CanvasPointDto> points,
        IReadOnlyDictionary<string, double> dimensionValues)
    {
        var toolPoints = points.Select(ToPoint).ToArray();
        var newEntities = CreateEntitiesForTool(toolName, toolPoints, dimensionValues).ToArray();
        if (newEntities.Length == 0)
        {
            return;
        }

        var newDimensions = SketchCreationDimensionFactory.CreateDimensionsForTool(
            toolName,
            newEntities,
            dimensionValues,
            CreateDimensionId);
        var newConstraints = SketchCreationConstraintFactory.CreateConstraintsForInsertion(
            toolName,
            _document.Entities,
            newEntities,
            CreateConstraintId);

        var nextDocument = new DrawingDocument(
            _document.Entities.Concat(newEntities),
            _document.Dimensions.Concat(newDimensions),
            _document.Constraints,
            _document.Metadata);
        if (newConstraints.Count > 0)
        {
            nextDocument = SketchConstraintService.ApplyConstraints(nextDocument, newConstraints);
        }

        ApplyDocumentChange(nextDocument, $"{FormatCreatedToolName(toolName)} added.");
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
        var splitPoint = ToPoint(point);
        if (TryGetEntityIdForOperationTarget(targetKey, out var entityId)
            && FindEntity(entityId) is CircleEntity)
        {
            SplitCircleAtPoint(entityId, splitPoint);
            return;
        }

        _pendingCircleSplit = null;
        if (TryGetEntityIdForOperationTarget(targetKey, out var arcEntityId)
            && FindEntity(arcEntityId) is ArcEntity)
        {
            SplitArcAtPoint(arcEntityId, splitPoint);
            return;
        }

        if (!TryGetLineIdForSplitTarget(targetKey, out var lineEntityId))
        {
            _status = "Click a line, an arc, or two points on a circle before splitting.";
            _ = InvokeAsync(StateHasChanged);
            return;
        }

        SplitLineAtPoint(lineEntityId, splitPoint);
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

        if (StringComparer.Ordinal.Equals(NormalizeToolName(toolName), "addsplinepoint"))
        {
            _activeTool = WorkbenchTool.AddSplinePoint;
            _status = "Added spline fit point. Click another location on the selected spline, or Esc to cancel.";
        }
        else
        {
            _activeTool = null;
            ResetSelection();
        }

        _ = InvokeAsync(StateHasChanged);
    }

    private void OnAddSplinePointRequested(string targetKey, CanvasPointDto point)
    {
        if (!TryGetEntityIdForOperationTarget(targetKey, out var entityId)
            || FindEntity(entityId) is not SplineEntity spline
            || spline.FitPoints.Count < 2)
        {
            _status = "Add spline point needs a fit-point spline target.";
            _ = InvokeAsync(StateHasChanged);
            return;
        }

        if (!DrawingModifyService.TryAddSplinePoint(
                _document,
                new[] { entityId },
                ToPoint(point),
                out var nextDocument))
        {
            _status = "Pick point must be on the clicked spline.";
            _ = InvokeAsync(StateHasChanged);
            return;
        }

        ApplyDocumentChange(nextDocument, "Added spline fit point. Click another location on a spline, or Esc to cancel.");
        _activeTool = WorkbenchTool.SplineControlPoint;
        _selectedEntityIds.Clear();
        _selectedEntityIds.Add(entityId);
        _activeSelectionKey = entityId;
        _ = InvokeAsync(StateHasChanged);
    }

    private void OnPowerTrimRequested(string targetKey, CanvasPointDto point)
    {
        if (!TryGetEntityIdForOperationTarget(targetKey, out var entityId)
            || FindEntity(entityId) is not { } targetEntity)
        {
            _status = "Power trim/extend needs a geometry target.";
            _ = InvokeAsync(StateHasChanged);
            return;
        }

        if (targetEntity is not LineEntity and not PolylineEntity and not PolygonEntity and not CircleEntity and not ArcEntity and not EllipseEntity and not SplineEntity and not PointEntity)
        {
            _status = "Power trim/extend currently supports line, polyline, polygon, circle, arc, ellipse, spline, and point targets only.";
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
            _status = "Power trim/extend needs a crossed geometry section or an extendable line target.";
            _ = InvokeAsync(StateHasChanged);
            return;
        }

        ApplyDocumentChange(nextDocument, "Power trim/extend applied. Click another section, or Esc to cancel.");
        _activeTool = WorkbenchTool.PowerTrim;
        ResetSelection();
        _ = InvokeAsync(StateHasChanged);
    }

    private void OnPowerTrimCrossingRequested(IReadOnlyList<CanvasPowerTrimPickDto> picks)
    {
        var trimPicks = new List<PowerTrimLinePick>();
        var unsupportedTargets = 0;
        foreach (var pick in picks)
        {
            if (TryGetEntityIdForOperationTarget(pick.TargetKey, out var entityId)
                && FindEntity(entityId) is { } targetEntity)
            {
                if (targetEntity is LineEntity or PolylineEntity or PolygonEntity or CircleEntity or ArcEntity or EllipseEntity or SplineEntity or PointEntity)
                {
                    trimPicks.Add(new PowerTrimLinePick(entityId, ToPoint(pick.Point)));
                }
                else
                {
                    unsupportedTargets++;
                }
            }
        }

        var nextDocument = _document;
        var appliedCount = trimPicks.Count == 0
            ? 0
            : DrawingModifyService.PowerTrimOrExtendLines(_document, trimPicks, CreateEntityId, out nextDocument);
        if (appliedCount == 0)
        {
            _status = unsupportedTargets > 0
                ? "Power trim drag currently supports crossed line, polyline, polygon, circle, arc, ellipse, spline, and point sections only."
                : "Power trim drag needs one or more geometry sections crossed by the cursor path.";
            _ = InvokeAsync(StateHasChanged);
            return;
        }

        ApplyDocumentChange(nextDocument, $"Power trim drag applied to {appliedCount} geometry picks. Drag another path, or Esc to cancel.");
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

        _pendingCircleSplit = null;
        _activeTool = null;
        _status = "Selection active.";
        _ = InvokeAsync(StateHasChanged);
    }

    private void ActivateTool(WorkbenchTool tool, string status)
    {
        if (tool != WorkbenchTool.SplitAtPoint)
        {
            _pendingCircleSplit = null;
        }

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
            _status = "Select whole geometry, polyline segments, dimensions, or constraints before deleting.";
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
            case "addsplinepoint" when points.Count >= 1:
                if (!DrawingModifyService.TryAddSplinePoint(
                        _document,
                        selectedEntityIds,
                        points[0],
                        out nextDocument))
                {
                    _status = "Add spline point needs exactly one selected fit-point spline and a pick on that spline.";
                    return false;
                }

                status = "Added spline fit point.";
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
        _pendingCircleSplit = null;
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

    private bool SplitCircleAtPoint(string circleEntityId, Point2 point)
    {
        if (_pendingCircleSplit is not { } pending
            || !StringComparer.Ordinal.Equals(pending.CircleEntityId, circleEntityId))
        {
            _pendingCircleSplit = new PendingCircleSplit(circleEntityId, point);
            _activeTool = WorkbenchTool.SplitAtPoint;
            _status = "First circle split point set. Pick the second point on the same circle, or Esc to cancel.";
            _ = InvokeAsync(StateHasChanged);
            return true;
        }

        var newArcId = CreateEntityId("arc");
        if (!CurveSplitService.TrySplitCircleAtPoints(
                _document,
                circleEntityId,
                pending.FirstPoint,
                point,
                newArcId,
                out var nextDocument))
        {
            _status = "Second split point must be a different point on the same circle.";
            _ = InvokeAsync(StateHasChanged);
            return false;
        }

        _pendingCircleSplit = null;
        ApplyDocumentChange(nextDocument, "Split circle into arcs. Click another curve to split, or Esc to cancel.");
        _activeTool = WorkbenchTool.SplitAtPoint;
        ResetSelection();
        _ = InvokeAsync(StateHasChanged);
        return true;
    }

    private bool SplitArcAtPoint(string arcEntityId, Point2 point)
    {
        var newArcId = CreateEntityId("arc");
        if (!CurveSplitService.TrySplitArcAtPoint(_document, arcEntityId, point, newArcId, out var nextDocument))
        {
            _status = "Split point must lie inside the selected arc.";
            _ = InvokeAsync(StateHasChanged);
            return false;
        }

        ApplyDocumentChange(nextDocument, "Split arc at point. Click another curve to split, or Esc to cancel.");
        _activeTool = WorkbenchTool.SplitAtPoint;
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
        _activeTool = WorkbenchTool.Dimension;
        _status = $"{status} Dimension tool stays active; pick another reference or Esc to cancel.";
        ResetSelection();
        _ = InvokeAsync(StateHasChanged);
    }

    private void StartConstraintTool(WorkbenchCommandId commandId)
    {
        if (!TryGetConstraintKind(commandId, out var kind)
            || !TryGetConstraintTool(kind, out var tool))
        {
            return;
        }

        if (TryApplySketchConstraint(kind, keepModal: true))
        {
            return;
        }

        if (_selectedEntityIds.Count > 0)
        {
            ResetSelection();
        }

        ActivateTool(
            tool,
            $"{FormatCommandName(commandId)} active. {GetConstraintPrompt(kind)} Esc: cancel.");
    }

    private bool TryApplySketchConstraint(SketchConstraintKind kind, bool keepModal)
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
            return false;
        }

        var nextDocument = SketchConstraintService.ApplyConstraint(_document, constraint);
        var appliedConstraint = nextDocument.Constraints.FirstOrDefault(candidate => candidate.Id == constraint.Id);
        var appliedStatus = appliedConstraint?.State == SketchConstraintState.Unsatisfied
            ? $"{status} Constraint is currently unsatisfied."
            : status;
        ApplyDocumentChange(nextDocument, appliedStatus);
        if (keepModal && TryGetConstraintTool(kind, out var tool))
        {
            _activeTool = tool;
            _status = $"{appliedStatus} {FormatConstraintKind(kind)} stays active; select the next eligible references or Esc to cancel.";
            ResetSelection();
        }

        _ = InvokeAsync(StateHasChanged);
        return true;
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

        var nextValue = existing.Kind == SketchDimensionKind.Count
            ? PolygonEntity.NormalizeSideCount(value)
            : value;
        var nextDimension = new SketchDimension(
            existing.Id,
            existing.Kind,
            existing.ReferenceKeys,
            nextValue,
            existing.Anchor,
            isDriving: true);
        ApplyDocumentChange(
            SketchDimensionSolverService.ApplyDimension(_document, nextDimension),
            $"Updated {FormatDimensionKind(existing.Kind)} dimension to {FormatNumber(nextValue)}.");
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
            new DrawingDocument(_document.Entities, nextDimensions, _document.Constraints, _document.Metadata),
            "Moved dimension.");
        _ = InvokeAsync(StateHasChanged);
    }

    private void OnGeometryDragRequested(
        string selectionKey,
        CanvasPointDto dragStart,
        CanvasPointDto dragEnd,
        bool constrainToCurrentVector)
    {
        if (!SketchGeometryDragService.TryApplyDrag(
                _document,
                selectionKey,
                ToPoint(dragStart),
                ToPoint(dragEnd),
                constrainToCurrentVector,
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

    private void ToggleShowAllConstraints()
    {
        _showAllConstraints = !_showAllConstraints;
        _status = _showAllConstraints
            ? "Constraint glyphs shown. Drag glyph groups to reposition them."
            : "Constraint glyphs hidden until referenced geometry is hovered.";
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

        if (_document.Metadata.Mode == DrawingDocumentMode.ReferenceOnly)
        {
            _status = "Reference-only document cannot be edited.";
            _ = InvokeAsync(StateHasChanged);
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
        var sidecarName = DxfDownloadFileName.SidecarFromSourceName(_fileName);
        var sidecarText = DxferSidecarWriter.Write(
            CreateExportDocument(downloadName),
            normalizedContent: exportText);
        _exportText = exportText;
        _downloadModule ??= await JsRuntime.InvokeAsync<IJSObjectReference>("import", DownloadModulePath);
        await _downloadModule.InvokeVoidAsync(
            "downloadTextFile",
            downloadName,
            exportText,
            "application/dxf;charset=utf-8");
        await _downloadModule.InvokeVoidAsync(
            "downloadTextFile",
            sidecarName,
            sidecarText,
            "application/json;charset=utf-8");
        _status = $"Saved {downloadName} and {sidecarName}.";
    }

    private DrawingDocument CreateExportDocument(string normalizedFileName)
    {
        var metadata = _document.Metadata with
        {
            SourceFileName = _document.Metadata.SourceFileName ?? _fileName,
            NormalizedFileName = normalizedFileName
        };

        return new DrawingDocument(
            _document.Entities,
            _document.Dimensions,
            _document.Constraints,
            metadata);
    }

    private static string FormatWarningSummary(IReadOnlyList<DrawingDocumentWarning> warnings)
    {
        if (warnings.Count == 0)
        {
            return string.Empty;
        }

        return $" Warnings: {string.Join("; ", warnings.Select(warning => warning.Message))}";
    }

    private static string ComputeSha256(string content)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(content));
        return string.Concat(hash.Select(part => part.ToString("x2", CultureInfo.InvariantCulture)));
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

        if (_selectedEntityIds.Count > 0
            && TryGetConstraintKind(_activeTool, out var constraintKind)
            && TryApplySketchConstraint(constraintKind, keepModal: true))
        {
            return;
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
            case PolygonEntity polygon:
                items.Add(new LiveReadoutItem("Sides", polygon.NormalizedSideCount.ToString(CultureInfo.InvariantCulture)));
                items.Add(new LiveReadoutItem(polygon.Circumscribed ? "Apothem" : "Radius", FormatNumber(polygon.Radius)));
                AddClosedPathReadouts(items, polygon.GetVertices());
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
            AddClosedPathReadouts(items, vertices);
            return;
        }

        items.Add(new LiveReadoutItem("Path Len", FormatNumber(pathLength)));
        items.Add(new LiveReadoutItem("Segments", Math.Max(0, vertices.Count - 1).ToString(CultureInfo.InvariantCulture)));
    }

    private static void AddClosedPathReadouts(ICollection<LiveReadoutItem> items, IReadOnlyList<Point2> vertices)
    {
        var closedVertices = EnsureClosedPath(vertices);
        items.Add(new LiveReadoutItem("Perimeter", FormatNumber(GetPathLength(closedVertices))));
        items.Add(new LiveReadoutItem("Area", FormatNumber(Math.Abs(GetSignedArea(closedVertices)))));
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
            PolygonEntity polygon => polygon.GetVertices(),
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

    private static IReadOnlyList<Point2> EnsureClosedPath(IReadOnlyList<Point2> points)
    {
        if (points.Count == 0 || IsClosedPath(points))
        {
            return points;
        }

        return points.Concat(new[] { points[0] }).ToArray();
    }

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

    private IReadOnlyList<DrawingEntity> CreateEntitiesForTool(
        string toolName,
        IReadOnlyList<Point2> points,
        IReadOnlyDictionary<string, double> dimensionValues) =>
        SketchCreationEntityFactory.CreateEntitiesForTool(toolName, points, CreateEntityId, _constructionMode, dimensionValues);

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
            tooltip ?? BuildTooltip(id, label, tool, isFuture),
            ToolHotkeys.GetKey(id));

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

    private static string? BuildCommandClass(WorkbenchToolCommand command)
    {
        var classes = new List<string>();
        if (command.IsFuture)
        {
            classes.Add("dxfer-icon-button-future");
        }

        return classes.Count == 0 ? null : string.Join(" ", classes);
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
            case WorkbenchCommandId.Ellipse:
                tool = WorkbenchTool.Ellipse;
                return true;
            case WorkbenchCommandId.ThreePointArc:
                tool = WorkbenchTool.ThreePointArc;
                return true;
            case WorkbenchCommandId.CenterPointArc:
                tool = WorkbenchTool.CenterPointArc;
                return true;
            case WorkbenchCommandId.EllipticalArc:
                tool = WorkbenchTool.EllipticalArc;
                return true;
            case WorkbenchCommandId.Conic:
                tool = WorkbenchTool.Conic;
                return true;
            case WorkbenchCommandId.InscribedPolygon:
                tool = WorkbenchTool.InscribedPolygon;
                return true;
            case WorkbenchCommandId.CircumscribedPolygon:
                tool = WorkbenchTool.CircumscribedPolygon;
                return true;
            case WorkbenchCommandId.Spline:
                tool = WorkbenchTool.Spline;
                return true;
            case WorkbenchCommandId.SplineControlPoint:
                tool = WorkbenchTool.SplineControlPoint;
                return true;
            case WorkbenchCommandId.Point:
                tool = WorkbenchTool.Point;
                return true;
            case WorkbenchCommandId.Slot:
                tool = WorkbenchTool.Slot;
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
            case WorkbenchCommandId.AddSplinePoint:
                tool = WorkbenchTool.AddSplinePoint;
                return true;
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

    private static bool TryGetConstraintKind(WorkbenchCommandId commandId, out SketchConstraintKind kind)
    {
        switch (commandId)
        {
            case WorkbenchCommandId.Coincident:
                kind = SketchConstraintKind.Coincident;
                return true;
            case WorkbenchCommandId.Concentric:
                kind = SketchConstraintKind.Concentric;
                return true;
            case WorkbenchCommandId.Parallel:
                kind = SketchConstraintKind.Parallel;
                return true;
            case WorkbenchCommandId.Tangent:
                kind = SketchConstraintKind.Tangent;
                return true;
            case WorkbenchCommandId.Horizontal:
                kind = SketchConstraintKind.Horizontal;
                return true;
            case WorkbenchCommandId.Vertical:
                kind = SketchConstraintKind.Vertical;
                return true;
            case WorkbenchCommandId.Perpendicular:
                kind = SketchConstraintKind.Perpendicular;
                return true;
            case WorkbenchCommandId.Equal:
                kind = SketchConstraintKind.Equal;
                return true;
            case WorkbenchCommandId.Midpoint:
                kind = SketchConstraintKind.Midpoint;
                return true;
            case WorkbenchCommandId.Fix:
                kind = SketchConstraintKind.Fix;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    private static bool TryGetConstraintKind(WorkbenchTool? tool, out SketchConstraintKind kind)
    {
        if (tool is null)
        {
            kind = default;
            return false;
        }

        switch (tool.Value)
        {
            case WorkbenchTool.Coincident:
                kind = SketchConstraintKind.Coincident;
                return true;
            case WorkbenchTool.Concentric:
                kind = SketchConstraintKind.Concentric;
                return true;
            case WorkbenchTool.Parallel:
                kind = SketchConstraintKind.Parallel;
                return true;
            case WorkbenchTool.Tangent:
                kind = SketchConstraintKind.Tangent;
                return true;
            case WorkbenchTool.Horizontal:
                kind = SketchConstraintKind.Horizontal;
                return true;
            case WorkbenchTool.Vertical:
                kind = SketchConstraintKind.Vertical;
                return true;
            case WorkbenchTool.Perpendicular:
                kind = SketchConstraintKind.Perpendicular;
                return true;
            case WorkbenchTool.Equal:
                kind = SketchConstraintKind.Equal;
                return true;
            case WorkbenchTool.Midpoint:
                kind = SketchConstraintKind.Midpoint;
                return true;
            case WorkbenchTool.Fix:
                kind = SketchConstraintKind.Fix;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    private static bool TryGetConstraintTool(SketchConstraintKind kind, out WorkbenchTool tool)
    {
        switch (kind)
        {
            case SketchConstraintKind.Coincident:
                tool = WorkbenchTool.Coincident;
                return true;
            case SketchConstraintKind.Concentric:
                tool = WorkbenchTool.Concentric;
                return true;
            case SketchConstraintKind.Parallel:
                tool = WorkbenchTool.Parallel;
                return true;
            case SketchConstraintKind.Tangent:
                tool = WorkbenchTool.Tangent;
                return true;
            case SketchConstraintKind.Horizontal:
                tool = WorkbenchTool.Horizontal;
                return true;
            case SketchConstraintKind.Vertical:
                tool = WorkbenchTool.Vertical;
                return true;
            case SketchConstraintKind.Perpendicular:
                tool = WorkbenchTool.Perpendicular;
                return true;
            case SketchConstraintKind.Equal:
                tool = WorkbenchTool.Equal;
                return true;
            case SketchConstraintKind.Midpoint:
                tool = WorkbenchTool.Midpoint;
                return true;
            case SketchConstraintKind.Fix:
                tool = WorkbenchTool.Fix;
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
        WorkbenchTool.Ellipse => "Pick center, major radius, then minor radius.",
        WorkbenchTool.ThreePointArc => "Pick start point, through point, then end point.",
        WorkbenchTool.TangentArc => "Pick start, tangent direction, then endpoint. In a chain, hover the last vertex to switch back to line.",
        WorkbenchTool.CenterPointArc => "Pick center, start radius point, then end angle point.",
        WorkbenchTool.EllipticalArc => "Pick center, major radius, minor radius, then arc endpoint.",
        WorkbenchTool.Conic => "Pick start, control point, then end point.",
        WorkbenchTool.InscribedPolygon => "Pick center, scroll for sides, then vertex radius.",
        WorkbenchTool.CircumscribedPolygon => "Pick center, scroll for sides, then side apothem.",
        WorkbenchTool.Spline => "Pick fit points; double-click to finish.",
        WorkbenchTool.Bezier => "Pick four Bezier control points.",
        WorkbenchTool.SplineControlPoint => "Click a fit-point spline where the new fit point belongs.",
        WorkbenchTool.Point => "Pick a point on the canvas.",
        WorkbenchTool.Construction => "Click geometry to toggle construction state.",
        WorkbenchTool.Slot => "Pick first center, second center, then radius point.",
        WorkbenchTool.PowerTrim => "Click a line, polyline, polygon, circle, arc, ellipse, spline, or point section to trim, or click past a line endpoint to extend.",
        WorkbenchTool.AddSplinePoint => "Click the selected fit-point spline where the new fit point belongs.",
        WorkbenchTool.Offset => "Click the through side or radius point.",
        WorkbenchTool.Translate => "Pick from point, then to point.",
        WorkbenchTool.Rotate => "Pick center, reference point, then target point.",
        WorkbenchTool.Scale => "Pick center, reference radius, then target radius.",
        WorkbenchTool.Mirror => "Pick two points for the mirror axis.",
        WorkbenchTool.LinearPattern => "Pick from point, then spacing point.",
        WorkbenchTool.CircularPattern => "Pick center, reference point, then target angle point.",
        _ => "Pick two points on the canvas."
    };

    private static string ConstraintTooltip(SketchConstraintKind kind) =>
        $"{FormatConstraintKind(kind)}. {GetConstraintPrompt(kind)} Works with preselection or post-selection; Esc cancels.";

    private static string GetConstraintPrompt(SketchConstraintKind kind) => kind switch
    {
        SketchConstraintKind.Coincident => "Select two editable points.",
        SketchConstraintKind.Concentric => "Select two circles or arcs.",
        SketchConstraintKind.Parallel => "Select two lines or polyline segments.",
        SketchConstraintKind.Tangent => "Select a line/circle/arc plus another circle/arc or line.",
        SketchConstraintKind.Horizontal => "Select one line or two editable points.",
        SketchConstraintKind.Vertical => "Select one line or two editable points.",
        SketchConstraintKind.Perpendicular => "Select two lines or polyline segments.",
        SketchConstraintKind.Equal => "Select two lines or two circle/arc radii.",
        SketchConstraintKind.Midpoint => "Select one line and one editable point.",
        SketchConstraintKind.Fix => "Select geometry or editable points to fix.",
        _ => "Select eligible references."
    };

    private static string FormatConstraintKind(SketchConstraintKind kind) => kind switch
    {
        SketchConstraintKind.Coincident => "Coincident",
        SketchConstraintKind.Concentric => "Concentric",
        SketchConstraintKind.Parallel => "Parallel",
        SketchConstraintKind.Tangent => "Tangent",
        SketchConstraintKind.Horizontal => "Horizontal",
        SketchConstraintKind.Vertical => "Vertical",
        SketchConstraintKind.Perpendicular => "Perpendicular",
        SketchConstraintKind.Equal => "Equal",
        SketchConstraintKind.Midpoint => "Midpoint",
        SketchConstraintKind.Fix => "Fix",
        _ => kind.ToString()
    };

    private static Point2 ToPoint(CanvasPointDto point) => new(point.X, point.Y);

    private static DrawingDocument CreateBlankDocument() => new(Array.Empty<DrawingEntity>());

    private static string NormalizeToolName(string toolName) =>
        toolName.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();

    private static string FormatCommandName(WorkbenchCommandId commandId) => commandId switch
    {
        WorkbenchCommandId.NewBlankDocument => "New File",
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
        WorkbenchCommandId.SplineControlPoint => "Add spline point",
        WorkbenchCommandId.PowerTrim => "Power trim/extend",
        WorkbenchCommandId.DeleteSelection => "Delete selected geometry",
        WorkbenchCommandId.SplitAtPoint => "Split at point",
        WorkbenchCommandId.AddSplinePoint => "Add spline point",
        WorkbenchCommandId.SaveDxf => "Save DXF",
        WorkbenchCommandId.LinearPattern => "Linear pattern",
        WorkbenchCommandId.CircularPattern => "Circular pattern",
        WorkbenchCommandId.Rotate90Clockwise => "Rotate 90 CW",
        WorkbenchCommandId.Rotate90CounterClockwise => "Rotate 90 CCW",
        WorkbenchCommandId.RemoveDuplicates => "Remove duplicates",
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
        "ellipse" => "Ellipse",
        "threepointarc" => "Three-point arc",
        "tangentarc" => "Tangent arc",
        "centerpointarc" => "Center point arc",
        "ellipticalarc" => "Elliptical arc",
        "conic" => "Conic",
        "inscribedpolygon" => "Inscribed polygon",
        "circumscribedpolygon" => "Circumscribed polygon",
        "spline" => "Spline",
        "bezier" => "Bezier",
        "splinecontrolpoint" => "Add spline point",
        "point" => "Point",
        "slot" => "Slot",
        "powertrim" => "Power trim/extend",
        "splitatpoint" => "Split at point",
        "addsplinepoint" => "Add spline point",
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
        SketchDimensionKind.Count => "count",
        _ => kind.ToString()
    };

    private static string FormatDeleteStatus(SelectionDeleteResult result)
    {
        if (result.DeletedGeometryCount == 0 && result.DeletedDimensions == 0 && result.DeletedConstraints > 0)
        {
            return result.DeletedConstraints == 1
                ? "Deleted 1 constraint."
                : $"Deleted {result.DeletedConstraints} constraints.";
        }

        if (result.DeletedGeometryCount == 0 && result.DeletedConstraints == 0 && result.DeletedDimensions > 0)
        {
            return result.DeletedDimensions == 1
                ? "Deleted 1 dimension."
                : $"Deleted {result.DeletedDimensions} dimensions.";
        }

        if (result.DeletedDimensions > 0 || result.DeletedConstraints > 0)
        {
            var sketchItems = new List<string>();
            if (result.DeletedDimensions > 0)
            {
                sketchItems.Add(result.DeletedDimensions == 1 ? "1 dimension" : $"{result.DeletedDimensions} dimensions");
            }

            if (result.DeletedConstraints > 0)
            {
                sketchItems.Add(result.DeletedConstraints == 1 ? "1 constraint" : $"{result.DeletedConstraints} constraints");
            }

            return result.DeletedGeometryCount == 0
                ? $"Deleted {string.Join(" and ", sketchItems)}."
                : $"Deleted {FormatGeometryDeleteSummary(result)} and {string.Join(" and ", sketchItems)}.";
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

    private readonly record struct PendingCircleSplit(string CircleEntityId, Point2 FirstPoint);
}

internal enum DockResizeTarget
{
    None,
    ToolPanel,
    Inspector
}

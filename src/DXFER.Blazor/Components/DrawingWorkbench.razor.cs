using System.Globalization;
using System.Text.Json;
using DXFER.Blazor.Interop;
using DXFER.Blazor.Selection;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.IO;
using DXFER.Core.Operations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;

namespace DXFER.Blazor.Components;

public partial class DrawingWorkbench : IDisposable
{
    private const long MaxDxfFileSize = 25 * 1024 * 1024;
    private const string SegmentKeySeparator = "|segment|";
    private const string PointKeySeparator = "|point|";
    private const int MinToolPanelWidth = 184;
    private const int MaxToolPanelWidth = 360;
    private const int MinInspectorWidth = 220;
    private const int MaxInspectorWidth = 520;

    private DrawingCanvas? _canvas;
    private DrawingDocument _document = SampleDrawingFactory.CreateCanvasPrototype();
    private string _fileName = "Sample flat pattern";
    private string _status = "Ready. Select a line to align a vector or load a DXF.";
    private string? _hoveredEntityId;
    private string _exportText = string.Empty;
    private GrainDirection _grainDirection = GrainDirection.None;
    private WorkbenchTool? _activeTool;
    private bool _showOriginAxes = true;
    private bool _isToolPanelCollapsed;
    private bool _isInspectorCollapsed = true;
    private int _toolPanelWidth = 220;
    private int _inspectorWidth = 280;
    private int _selectionResetToken;
    private int _documentFitToken = 1;
    private int _createdEntitySequence;
    private DockResizeTarget _resizeTarget = DockResizeTarget.None;
    private double _resizeStartClientX;
    private int _resizeStartToolPanelWidth;
    private int _resizeStartInspectorWidth;
    private readonly HashSet<string> _selectedEntityIds = new(StringComparer.Ordinal);
    private readonly Stack<DrawingDocument> _undoStack = new();
    private readonly Stack<DrawingDocument> _redoStack = new();

    [Inject]
    private WorkbenchMenuCommandService MenuCommandService { get; set; } = default!;

    protected override void OnInitialized()
    {
        MenuCommandService.CommandRequested += InvokeWorkbenchCommand;
    }

    public void Dispose()
    {
        MenuCommandService.CommandRequested -= InvokeWorkbenchCommand;
    }

    private bool HasDocument => _document.Entities.Count > 0;

    private bool HasSelection => _selectedEntityIds.Count > 0;

    private bool CanDeleteSelection =>
        SelectionDeleteResolver.CanDeleteSelection(_document, _selectedEntityIds);

    private bool CanUndo => _undoStack.Count > 0;

    private bool CanRedo => _redoStack.Count > 0;

    private bool CanAlignSelectedVector =>
        SelectionVectorResolver.TryGetAlignmentVector(_document, _selectedEntityIds, out _, out _);

    private bool CanMoveSelectedPointToOrigin =>
        SelectionPointResolver.TryGetPointToOriginReference(_document, _selectedEntityIds, out _);

    private Bounds2 Bounds => _document.GetBounds();

    private IReadOnlyList<WorkbenchToolGroup> ToolGroups => new[]
    {
        new WorkbenchToolGroup("View", new[]
        {
            Command(WorkbenchCommandId.Measure, WorkbenchTool.Measure, CadIconName.Measure, "Measure"),
            Command(WorkbenchCommandId.FitExtents, null, CadIconName.Fit, "Fit extents", !HasDocument),
            Command(WorkbenchCommandId.OriginAxes, null, CadIconName.OriginAxes, "Origin axes", pressed: _showOriginAxes)
        }),
        new WorkbenchToolGroup("Sketch", new[]
        {
            Command(WorkbenchCommandId.Line, WorkbenchTool.Line, CadIconName.Line, "Line"),
            Command(WorkbenchCommandId.MidpointLine, WorkbenchTool.MidpointLine, CadIconName.MidpointLine, "Midpoint line"),
            Command(WorkbenchCommandId.TwoPointRectangle, WorkbenchTool.TwoPointRectangle, CadIconName.Rectangle, "Two-point rectangle"),
            Command(WorkbenchCommandId.CenterRectangle, WorkbenchTool.CenterRectangle, CadIconName.CenterRectangle, "Center rectangle", disabled: true, isFuture: true),
            Command(WorkbenchCommandId.AlignedRectangle, WorkbenchTool.AlignedRectangle, CadIconName.AlignedRectangle, "Aligned rectangle", disabled: true, isFuture: true),
            Command(WorkbenchCommandId.CenterCircle, WorkbenchTool.CenterCircle, CadIconName.Circle, "Center circle"),
            Command(WorkbenchCommandId.ThreePointCircle, WorkbenchTool.ThreePointCircle, CadIconName.ThreePointCircle, "Three-point circle", disabled: true, isFuture: true),
            Command(WorkbenchCommandId.Ellipse, WorkbenchTool.Ellipse, CadIconName.Ellipse, "Ellipse", disabled: true, isFuture: true),
            Command(WorkbenchCommandId.ThreePointArc, WorkbenchTool.ThreePointArc, CadIconName.Arc, "Three-point arc", disabled: true, isFuture: true),
            Command(WorkbenchCommandId.TangentArc, WorkbenchTool.TangentArc, CadIconName.TangentArc, "Tangent arc", disabled: true, isFuture: true),
            Command(WorkbenchCommandId.CenterPointArc, WorkbenchTool.CenterPointArc, CadIconName.CenterPointArc, "Center point arc", disabled: true, isFuture: true),
            Command(WorkbenchCommandId.EllipticalArc, WorkbenchTool.EllipticalArc, CadIconName.EllipticalArc, "Elliptical arc", disabled: true, isFuture: true),
            Command(WorkbenchCommandId.Conic, WorkbenchTool.Conic, CadIconName.Conic, "Conic", disabled: true, isFuture: true),
            Command(WorkbenchCommandId.InscribedPolygon, WorkbenchTool.InscribedPolygon, CadIconName.InscribedPolygon, "Inscribed polygon", disabled: true, isFuture: true),
            Command(WorkbenchCommandId.CircumscribedPolygon, WorkbenchTool.CircumscribedPolygon, CadIconName.CircumscribedPolygon, "Circumscribed polygon", disabled: true, isFuture: true),
            Command(WorkbenchCommandId.Spline, WorkbenchTool.Spline, CadIconName.Spline, "Spline", disabled: true, isFuture: true),
            Command(WorkbenchCommandId.Bezier, WorkbenchTool.Bezier, CadIconName.Bezier, "Bezier", disabled: true, isFuture: true),
            Command(WorkbenchCommandId.SplineControlPoint, WorkbenchTool.SplineControlPoint, CadIconName.SplineControlPoint, "Spline control point", disabled: true, isFuture: true),
            Command(WorkbenchCommandId.Point, WorkbenchTool.Point, CadIconName.Point, "Point", disabled: true, isFuture: true),
            Command(WorkbenchCommandId.Text, WorkbenchTool.Text, CadIconName.Text, "Text", disabled: true, isFuture: true),
            Command(WorkbenchCommandId.Slot, WorkbenchTool.Slot, CadIconName.Slot, "Slot", disabled: true, isFuture: true)
        }),
        new WorkbenchToolGroup("Modify", new[]
        {
            Command(WorkbenchCommandId.Undo, null, CadIconName.Undo, "Undo", !CanUndo),
            Command(WorkbenchCommandId.Redo, null, CadIconName.Redo, "Redo", !CanRedo),
            Command(WorkbenchCommandId.Construction, WorkbenchTool.Construction, CadIconName.Construction, "Construction", disabled: true, isFuture: true),
            Command(WorkbenchCommandId.DeleteSelection, null, CadIconName.Delete, "Delete selected geometry", !CanDeleteSelection),
            Command(WorkbenchCommandId.PowerTrim, WorkbenchTool.PowerTrim, CadIconName.PowerTrim, "Power trim/extend", disabled: true, isFuture: true),
            Command(WorkbenchCommandId.SplitAtPoint, WorkbenchTool.SplitAtPoint, CadIconName.Split, "Split at point", disabled: true, isFuture: true),
            Command(WorkbenchCommandId.Offset, WorkbenchTool.Offset, CadIconName.Offset, "Offset", disabled: true, isFuture: true),
            Command(WorkbenchCommandId.Fillet, WorkbenchTool.Fillet, CadIconName.Fillet, "Fillet", disabled: true, isFuture: true),
            Command(WorkbenchCommandId.Chamfer, WorkbenchTool.Chamfer, CadIconName.Chamfer, "Chamfer", disabled: true, isFuture: true),
            Command(WorkbenchCommandId.Dimension, WorkbenchTool.Dimension, CadIconName.Dimension, "Dimension", disabled: true, isFuture: true)
        }),
        new WorkbenchToolGroup("Transform", new[]
        {
            Command(WorkbenchCommandId.Translate, WorkbenchTool.Translate, CadIconName.Move, "Translate", disabled: true, isFuture: true),
            Command(WorkbenchCommandId.Rotate, WorkbenchTool.Rotate, CadIconName.Rotate, "Rotate", disabled: true, isFuture: true),
            Command(WorkbenchCommandId.Rotate90Clockwise, null, CadIconName.Rotate90Clockwise, "Rotate 90 CW", !HasDocument),
            Command(WorkbenchCommandId.Rotate90CounterClockwise, null, CadIconName.Rotate90CounterClockwise, "Rotate 90 CCW", !HasDocument),
            Command(WorkbenchCommandId.Scale, WorkbenchTool.Scale, CadIconName.Scale, "Scale", disabled: true, isFuture: true),
            Command(WorkbenchCommandId.Mirror, WorkbenchTool.Mirror, CadIconName.Mirror, "Mirror", disabled: true, isFuture: true),
            Command(WorkbenchCommandId.BoundsToOrigin, null, CadIconName.BoundsToOrigin, "Move bounds minimum to origin", !HasDocument),
            Command(WorkbenchCommandId.PointToOrigin, null, CadIconName.PointToOrigin, "Move selected point to origin", !CanMoveSelectedPointToOrigin),
            Command(WorkbenchCommandId.VectorToX, null, CadIconName.VectorToX, "Align selected vector to X", !CanAlignSelectedVector),
            Command(WorkbenchCommandId.VectorToY, null, CadIconName.VectorToY, "Align selected vector to Y", !CanAlignSelectedVector)
        }),
        new WorkbenchToolGroup("Pattern", new[]
        {
            Command(WorkbenchCommandId.LinearPattern, WorkbenchTool.LinearPattern, CadIconName.LinearPattern, "Linear pattern", disabled: true, isFuture: true),
            Command(WorkbenchCommandId.CircularPattern, WorkbenchTool.CircularPattern, CadIconName.CircularPattern, "Circular pattern", disabled: true, isFuture: true)
        }),
        new WorkbenchToolGroup("Constraints", new[]
        {
            Command(WorkbenchCommandId.Coincident, WorkbenchTool.Coincident, CadIconName.Coincident, "Coincident", disabled: true, isFuture: true),
            Command(WorkbenchCommandId.Concentric, WorkbenchTool.Concentric, CadIconName.Concentric, "Concentric", disabled: true, isFuture: true),
            Command(WorkbenchCommandId.Parallel, WorkbenchTool.Parallel, CadIconName.Parallel, "Parallel", disabled: true, isFuture: true),
            Command(WorkbenchCommandId.Tangent, WorkbenchTool.Tangent, CadIconName.Tangent, "Tangent", disabled: true, isFuture: true),
            Command(WorkbenchCommandId.Horizontal, WorkbenchTool.Horizontal, CadIconName.Horizontal, "Horizontal", disabled: true, isFuture: true),
            Command(WorkbenchCommandId.Vertical, WorkbenchTool.Vertical, CadIconName.Vertical, "Vertical", disabled: true, isFuture: true),
            Command(WorkbenchCommandId.Perpendicular, WorkbenchTool.Perpendicular, CadIconName.Perpendicular, "Perpendicular", disabled: true, isFuture: true),
            Command(WorkbenchCommandId.Equal, WorkbenchTool.Equal, CadIconName.Equal, "Equal", disabled: true, isFuture: true),
            Command(WorkbenchCommandId.Midpoint, WorkbenchTool.Midpoint, CadIconName.Midpoint, "Midpoint", disabled: true, isFuture: true),
            Command(WorkbenchCommandId.Normal, WorkbenchTool.Normal, CadIconName.Normal, "Normal", disabled: true, isFuture: true),
            Command(WorkbenchCommandId.Pierce, WorkbenchTool.Pierce, CadIconName.Pierce, "Pierce", disabled: true, isFuture: true),
            Command(WorkbenchCommandId.Symmetric, WorkbenchTool.Symmetric, CadIconName.Symmetric, "Symmetric", disabled: true, isFuture: true),
            Command(WorkbenchCommandId.Fix, WorkbenchTool.Fix, CadIconName.Fix, "Fix", disabled: true, isFuture: true),
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
        WorkbenchTool.InscribedPolygon => "Inscribed polygon",
        WorkbenchTool.CircumscribedPolygon => "Circumscribed polygon",
        WorkbenchTool.SplineControlPoint => "Spline control point",
        WorkbenchTool.PowerTrim => "Power trim/extend",
        WorkbenchTool.ThreePointArc => "Three-point arc",
        WorkbenchTool.SplitAtPoint => "Split at point",
        WorkbenchTool.LinearPattern => "Linear pattern",
        WorkbenchTool.CircularPattern => "Circular pattern",
        _ => _activeTool?.ToString() ?? "Selection"
    };

    private string HoverText => FormatSelectionKey(_hoveredEntityId);

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

    private async Task OpenFileAsync(InputFileChangeEventArgs args)
    {
        var file = args.File;
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
            _status = "No supported DXF entities were found. V1 reads LINE, CIRCLE, ARC, LWPOLYLINE, POLYLINE, and SPLINE.";
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
                DeleteSelectedGeometry();
                break;
            case WorkbenchCommandId.BoundsToOrigin:
                MoveBoundsToOrigin();
                break;
            case WorkbenchCommandId.PointToOrigin:
                MoveSelectedPointToOrigin();
                break;
            case WorkbenchCommandId.VectorToX:
                AlignSelectedVectorToX();
                break;
            case WorkbenchCommandId.VectorToY:
                AlignSelectedVectorToY();
                break;
            case WorkbenchCommandId.Rotate90Clockwise:
                RotateDocumentAboutBoundsCenter(-90, "Rotated drawing 90 degrees clockwise.");
                break;
            case WorkbenchCommandId.Rotate90CounterClockwise:
                RotateDocumentAboutBoundsCenter(90, "Rotated drawing 90 degrees counterclockwise.");
                break;
            default:
                if (TryGetImplementedSketchTool(commandId, out var tool))
                {
                    ActivateTool(tool, $"{FormatCommandName(commandId)} active. Pick two points on the canvas. Press Esc to cancel.");
                    ResetSelection();
                    break;
                }

                _status = $"{FormatCommandName(commandId)} is not implemented yet.";
                break;
        }
    }

    private void OnToolCommitRequested(string toolName, IReadOnlyList<CanvasPointDto> points)
    {
        if (points.Count < 2)
        {
            return;
        }

        var first = ToPoint(points[0]);
        var second = ToPoint(points[1]);
        var newEntities = CreateEntitiesForTool(toolName, first, second).ToArray();
        if (newEntities.Length == 0)
        {
            return;
        }

        ApplyDocumentChange(
            new DrawingDocument(_document.Entities.Concat(newEntities)),
            $"{FormatCreatedToolName(toolName)} added.");
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
        if (result.DeletedGeometryCount == 0)
        {
            _status = "Select whole geometry or polyline segments before deleting.";
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
        if (!SelectionVectorResolver.TryGetAlignmentVector(_document, _selectedEntityIds, out var vectorStart, out var vectorEnd))
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

        _ = InvokeAsync(StateHasChanged);
    }

    private void ResetSelection()
    {
        _selectedEntityIds.Clear();
        _hoveredEntityId = null;
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
            new("Sel", _selectedEntityIds.Count == 0 ? "none" : _selectedEntityIds.Count.ToString(CultureInfo.InvariantCulture))
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

        items.Add(new LiveReadoutItem("Hover", HoverText));
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

    private IEnumerable<DrawingEntity> CreateEntitiesForTool(string toolName, Point2 first, Point2 second)
    {
        var normalizedTool = NormalizeToolName(toolName);
        switch (normalizedTool)
        {
            case "line":
                yield return new LineEntity(CreateEntityId("line"), first, second);
                break;
            case "midpointline":
                var mirroredEndpoint = new Point2((2 * first.X) - second.X, (2 * first.Y) - second.Y);
                yield return new LineEntity(CreateEntityId("line"), mirroredEndpoint, second);
                break;
            case "twopointrectangle":
                var oppositeA = new Point2(second.X, first.Y);
                var oppositeB = new Point2(first.X, second.Y);
                yield return new LineEntity(CreateEntityId("rect"), first, oppositeA);
                yield return new LineEntity(CreateEntityId("rect"), oppositeA, second);
                yield return new LineEntity(CreateEntityId("rect"), second, oppositeB);
                yield return new LineEntity(CreateEntityId("rect"), oppositeB, first);
                break;
            case "centercircle":
                var radius = Math.Sqrt(Math.Pow(second.X - first.X, 2) + Math.Pow(second.Y - first.Y, 2));
                if (radius > 0.000001)
                {
                    yield return new CircleEntity(CreateEntityId("circle"), first, radius);
                }

                break;
        }
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
            tooltip ?? BuildTooltip(label, tool, isFuture));

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

    private static string BuildTooltip(string label, WorkbenchTool? tool, bool isFuture)
    {
        if (isFuture)
        {
            return $"{label}. Not implemented yet.";
        }

        return tool.HasValue
            ? $"{label}. Enters a modal tool; press Esc to return to selection."
            : label;
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
            case WorkbenchCommandId.CenterCircle:
                tool = WorkbenchTool.CenterCircle;
                return true;
            default:
                tool = default;
                return false;
        }
    }

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
        WorkbenchCommandId.LinearPattern => "Linear pattern",
        WorkbenchCommandId.CircularPattern => "Circular pattern",
        WorkbenchCommandId.Rotate90Clockwise => "Rotate 90 CW",
        WorkbenchCommandId.Rotate90CounterClockwise => "Rotate 90 CCW",
        _ => commandId.ToString()
    };

    private static string FormatCreatedToolName(string toolName) => NormalizeToolName(toolName) switch
    {
        "midpointline" => "Midpoint line",
        "twopointrectangle" => "Two-point rectangle",
        "centercircle" => "Center circle",
        _ => "Line"
    };

    private static string FormatDeleteStatus(SelectionDeleteResult result)
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

    private sealed record LiveReadoutItem(string Label, string Value);

    private readonly record struct ReadoutVector(Point2 Start, Point2 End);
}

internal enum DockResizeTarget
{
    None,
    ToolPanel,
    Inspector
}

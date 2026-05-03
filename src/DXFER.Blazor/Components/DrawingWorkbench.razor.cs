using System.Globalization;
using System.Text.Json;
using DXFER.Blazor.Selection;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.IO;
using DXFER.Core.Operations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;

namespace DXFER.Blazor.Components;

public partial class DrawingWorkbench
{
    private const long MaxDxfFileSize = 25 * 1024 * 1024;
    private const string SegmentKeySeparator = "|segment|";
    private const string PointKeySeparator = "|point|";

    private DrawingCanvas? _canvas;
    private DrawingDocument _document = SampleDrawingFactory.CreateCanvasPrototype();
    private string _fileName = "Sample flat pattern";
    private string _status = "Ready. Select a line to align a vector or load a DXF.";
    private string? _hoveredEntityId;
    private string _exportText = string.Empty;
    private GrainDirection _grainDirection = GrainDirection.None;
    private WorkbenchTool _activeTool = WorkbenchTool.Select;
    private bool _showOriginAxes = true;
    private bool _isToolPanelCollapsed;
    private bool _isInspectorCollapsed;
    private int _toolPanelWidth = 220;
    private int _inspectorWidth = 280;
    private int _selectionResetToken;
    private readonly HashSet<string> _selectedEntityIds = new(StringComparer.Ordinal);
    private readonly Stack<DrawingDocument> _undoStack = new();
    private readonly Stack<DrawingDocument> _redoStack = new();

    private bool HasDocument => _document.Entities.Count > 0;

    private bool HasSelection => _selectedEntityIds.Count > 0;

    private bool CanUndo => _undoStack.Count > 0;

    private bool CanRedo => _redoStack.Count > 0;

    private bool CanAlignSelectedVector =>
        SelectionVectorResolver.TryGetAlignmentVector(_document, _selectedEntityIds, out _, out _);

    private bool CanMoveSelectedPointToOrigin =>
        SelectionPointResolver.TryGetPointToOriginReference(_document, _selectedEntityIds, out _);

    private Bounds2 Bounds => _document.GetBounds();

    private IReadOnlyList<WorkbenchToolGroup> ToolGroups => new[]
    {
        new WorkbenchToolGroup("Navigate", new[]
        {
            Command(WorkbenchCommandId.Select, WorkbenchTool.Select, CadIconName.Select, "Select"),
            Command(WorkbenchCommandId.Measure, WorkbenchTool.Measure, CadIconName.Measure, "Measure"),
            Command(WorkbenchCommandId.Undo, null, CadIconName.Undo, "Undo", !CanUndo),
            Command(WorkbenchCommandId.Redo, null, CadIconName.Redo, "Redo", !CanRedo),
            Command(WorkbenchCommandId.FitExtents, null, CadIconName.Fit, "Fit extents", !HasDocument),
            Command(WorkbenchCommandId.OriginAxes, null, CadIconName.OriginAxes, "Origin axes", pressed: _showOriginAxes)
        }),
        new WorkbenchToolGroup("Geometry", new[]
        {
            Command(WorkbenchCommandId.Line, WorkbenchTool.Line, CadIconName.Line, "Line", isFuture: true),
            Command(WorkbenchCommandId.CenterLine, WorkbenchTool.CenterLine, CadIconName.CenterLine, "Centerline", isFuture: true),
            Command(WorkbenchCommandId.TwoPointRectangle, WorkbenchTool.TwoPointRectangle, CadIconName.Rectangle, "Two-point rectangle", isFuture: true),
            Command(WorkbenchCommandId.CenterRectangle, WorkbenchTool.CenterRectangle, CadIconName.CenterRectangle, "Center rectangle", isFuture: true),
            Command(WorkbenchCommandId.CenterCircle, WorkbenchTool.CenterCircle, CadIconName.Circle, "Center circle", isFuture: true),
            Command(WorkbenchCommandId.ThreePointCircle, WorkbenchTool.ThreePointCircle, CadIconName.ThreePointCircle, "Three-point circle", isFuture: true),
            Command(WorkbenchCommandId.ThreePointArc, WorkbenchTool.ThreePointArc, CadIconName.Arc, "Three-point arc", isFuture: true),
            Command(WorkbenchCommandId.Slot, WorkbenchTool.Slot, CadIconName.Slot, "Slot", isFuture: true)
        }),
        new WorkbenchToolGroup("Edit", new[]
        {
            Command(WorkbenchCommandId.Trim, WorkbenchTool.Trim, CadIconName.Trim, "Trim", isFuture: true),
            Command(WorkbenchCommandId.Extend, WorkbenchTool.Extend, CadIconName.Extend, "Extend", isFuture: true),
            Command(WorkbenchCommandId.SplitAtPoint, WorkbenchTool.SplitAtPoint, CadIconName.Split, "Split at point", isFuture: true),
            Command(WorkbenchCommandId.Offset, WorkbenchTool.Offset, CadIconName.Offset, "Offset", isFuture: true),
            Command(WorkbenchCommandId.Fillet, WorkbenchTool.Fillet, CadIconName.Fillet, "Fillet", isFuture: true),
            Command(WorkbenchCommandId.Chamfer, WorkbenchTool.Chamfer, CadIconName.Chamfer, "Chamfer", isFuture: true)
        }),
        new WorkbenchToolGroup("Transform", new[]
        {
            Command(WorkbenchCommandId.Translate, WorkbenchTool.Translate, CadIconName.Move, "Translate", isFuture: true),
            Command(WorkbenchCommandId.Rotate, WorkbenchTool.Rotate, CadIconName.Rotate, "Rotate", isFuture: true),
            Command(WorkbenchCommandId.Scale, WorkbenchTool.Scale, CadIconName.Scale, "Scale", isFuture: true),
            Command(WorkbenchCommandId.Mirror, WorkbenchTool.Mirror, CadIconName.Mirror, "Mirror", isFuture: true),
            Command(WorkbenchCommandId.VectorToX, null, CadIconName.VectorToX, "Align selected vector to X", !CanAlignSelectedVector),
            Command(WorkbenchCommandId.VectorToY, null, CadIconName.VectorToY, "Align selected vector to Y", !CanAlignSelectedVector)
        }),
        new WorkbenchToolGroup("Pattern", new[]
        {
            Command(WorkbenchCommandId.LinearPattern, WorkbenchTool.LinearPattern, CadIconName.LinearPattern, "Linear pattern", isFuture: true),
            Command(WorkbenchCommandId.CircularPattern, WorkbenchTool.CircularPattern, CadIconName.CircularPattern, "Circular pattern", isFuture: true)
        }),
        new WorkbenchToolGroup("Prep", new[]
        {
            Command(WorkbenchCommandId.BoundsToOrigin, null, CadIconName.BoundsToOrigin, "Move bounds minimum to origin", !HasDocument),
            Command(WorkbenchCommandId.PointToOrigin, null, CadIconName.PointToOrigin, "Move selected point to origin", !CanMoveSelectedPointToOrigin),
            Command(WorkbenchCommandId.LongBoundsToX, null, CadIconName.LongBoundsToX, "Orient long bounds to X", !HasDocument),
            Command(WorkbenchCommandId.LongBoundsToY, null, CadIconName.LongBoundsToY, "Orient long bounds to Y", !HasDocument)
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

            return string.Join(" ", classes);
        }
    }

    private string WorkbenchGridStyle =>
        $"--dxfer-tools-width:{_toolPanelWidth}px;--dxfer-inspector-width:{_inspectorWidth}px;";

    private bool IsParametricCommandActive =>
        _activeTool is not WorkbenchTool.Select and not WorkbenchTool.Measure;

    private string ActiveToolLabel => _activeTool switch
    {
        WorkbenchTool.CenterLine => "Centerline",
        WorkbenchTool.TwoPointRectangle => "Two-point rectangle",
        WorkbenchTool.CenterRectangle => "Center rectangle",
        WorkbenchTool.CenterCircle => "Center circle",
        WorkbenchTool.ThreePointCircle => "Three-point circle",
        WorkbenchTool.ThreePointArc => "Three-point arc",
        WorkbenchTool.SplitAtPoint => "Split at point",
        WorkbenchTool.LinearPattern => "Linear pattern",
        WorkbenchTool.CircularPattern => "Circular pattern",
        _ => _activeTool.ToString()
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
            _status = "No supported DXF entities were found. V1 reads LINE, CIRCLE, ARC, LWPOLYLINE, and POLYLINE.";
            return;
        }

        _document = document;
        ClearHistory();
        ResetSelection();
        _status = $"Loaded {document.Entities.Count} supported entities from DXF.";
    }

    private void LoadSample()
    {
        _document = SampleDrawingFactory.CreateCanvasPrototype();
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
            case WorkbenchCommandId.Select:
                ActivateTool(WorkbenchTool.Select, "Select tool active.");
                break;
            case WorkbenchCommandId.Measure:
                ActivateTool(WorkbenchTool.Measure, "Measure tool active.");
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
            case WorkbenchCommandId.LongBoundsToX:
                OrientLongAxisToX();
                break;
            case WorkbenchCommandId.LongBoundsToY:
                OrientLongAxisToY();
                break;
            default:
                ActivateTool(ToFutureTool(commandId), $"{FormatCommandName(commandId)} command shell active.");
                break;
        }
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

    private void OrientLongAxisToX() => OrientLongAxis(AxisDirection.X);

    private void OrientLongAxisToY() => OrientLongAxis(AxisDirection.Y);

    private void ToggleOriginAxes()
    {
        _showOriginAxes = !_showOriginAxes;
        _status = _showOriginAxes
            ? "Origin axes enabled."
            : "Origin axes hidden.";
    }

    private void ToggleToolPanel() => _isToolPanelCollapsed = !_isToolPanelCollapsed;

    private void ToggleInspector() => _isInspectorCollapsed = !_isInspectorCollapsed;

    private void OnToolPanelWidthChanged(ChangeEventArgs args) =>
        _toolPanelWidth = ClampParsedInt(args.Value, 184, 320, _toolPanelWidth);

    private void OnInspectorWidthChanged(ChangeEventArgs args) =>
        _inspectorWidth = ClampParsedInt(args.Value, 220, 460, _inspectorWidth);

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

    private void OrientLongAxis(AxisDirection axis)
    {
        var after = DrawingPrepService.OrientLongBoundsAxis(_document, axis);
        if (ReferenceEquals(_document, after))
        {
            _status = $"Long bounds axis is already global {axis}.";
            return;
        }

        ApplyDocumentChange(after, $"Oriented long bounds axis to global {axis}.");
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

    private WorkbenchToolCommand Command(
        WorkbenchCommandId id,
        WorkbenchTool? tool,
        CadIconName icon,
        string label,
        bool disabled = false,
        bool pressed = false,
        bool isFuture = false) =>
        new(id, tool, icon, label, disabled, pressed || (tool.HasValue && tool.Value == _activeTool), isFuture);

    private static int ClampParsedInt(object? value, int min, int max, int fallback)
    {
        if (value is null
            || !int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return fallback;
        }

        return Math.Clamp(parsed, min, max);
    }

    private static WorkbenchTool ToFutureTool(WorkbenchCommandId commandId) => commandId switch
    {
        WorkbenchCommandId.Line => WorkbenchTool.Line,
        WorkbenchCommandId.CenterLine => WorkbenchTool.CenterLine,
        WorkbenchCommandId.TwoPointRectangle => WorkbenchTool.TwoPointRectangle,
        WorkbenchCommandId.CenterRectangle => WorkbenchTool.CenterRectangle,
        WorkbenchCommandId.CenterCircle => WorkbenchTool.CenterCircle,
        WorkbenchCommandId.ThreePointCircle => WorkbenchTool.ThreePointCircle,
        WorkbenchCommandId.ThreePointArc => WorkbenchTool.ThreePointArc,
        WorkbenchCommandId.Slot => WorkbenchTool.Slot,
        WorkbenchCommandId.Trim => WorkbenchTool.Trim,
        WorkbenchCommandId.Extend => WorkbenchTool.Extend,
        WorkbenchCommandId.SplitAtPoint => WorkbenchTool.SplitAtPoint,
        WorkbenchCommandId.Offset => WorkbenchTool.Offset,
        WorkbenchCommandId.Fillet => WorkbenchTool.Fillet,
        WorkbenchCommandId.Chamfer => WorkbenchTool.Chamfer,
        WorkbenchCommandId.Translate => WorkbenchTool.Translate,
        WorkbenchCommandId.Rotate => WorkbenchTool.Rotate,
        WorkbenchCommandId.Scale => WorkbenchTool.Scale,
        WorkbenchCommandId.Mirror => WorkbenchTool.Mirror,
        WorkbenchCommandId.LinearPattern => WorkbenchTool.LinearPattern,
        WorkbenchCommandId.CircularPattern => WorkbenchTool.CircularPattern,
        _ => WorkbenchTool.Select
    };

    private static string FormatCommandName(WorkbenchCommandId commandId) => commandId switch
    {
        WorkbenchCommandId.TwoPointRectangle => "Two-point rectangle",
        WorkbenchCommandId.CenterRectangle => "Center rectangle",
        WorkbenchCommandId.CenterCircle => "Center circle",
        WorkbenchCommandId.ThreePointCircle => "Three-point circle",
        WorkbenchCommandId.ThreePointArc => "Three-point arc",
        WorkbenchCommandId.SplitAtPoint => "Split at point",
        WorkbenchCommandId.LinearPattern => "Linear pattern",
        WorkbenchCommandId.CircularPattern => "Circular pattern",
        _ => commandId.ToString()
    };
}

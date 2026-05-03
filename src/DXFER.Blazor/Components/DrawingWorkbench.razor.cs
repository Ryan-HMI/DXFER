using System.Globalization;
using System.Text.Json;
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

    private DrawingCanvas? _canvas;
    private DrawingDocument _document = SampleDrawingFactory.CreateCanvasPrototype();
    private string _fileName = "Sample flat pattern";
    private string _status = "Ready. Select a line to align a vector or load a DXF.";
    private string? _hoveredEntityId;
    private string _exportText = string.Empty;
    private GrainDirection _grainDirection = GrainDirection.GlobalX;
    private readonly HashSet<string> _selectedEntityIds = new(StringComparer.Ordinal);

    private bool HasDocument => _document.Entities.Count > 0;

    private bool HasSelection => _selectedEntityIds.Count > 0;

    private Bounds2 Bounds => _document.GetBounds();

    private string MeasurementText
    {
        get
        {
            if (!DrawingPrepService.TryGetMeasurement(_document, _selectedEntityIds, out var measurement))
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
        _selectedEntityIds.Clear();
        _hoveredEntityId = null;
        _status = $"Loaded {document.Entities.Count} supported entities from DXF.";
    }

    private void LoadSample()
    {
        _document = SampleDrawingFactory.CreateCanvasPrototype();
        _fileName = "Sample flat pattern";
        _status = "Sample drawing loaded.";
        _exportText = string.Empty;
        _selectedEntityIds.Clear();
        _hoveredEntityId = null;
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
        _document = DrawingPrepService.MoveBoundsMinimumToOrigin(_document);
        _status = "Moved drawing bounds minimum to global origin.";
        _exportText = string.Empty;
    }

    private void MoveSelectedPointToOrigin()
    {
        if (!DrawingPrepService.TryGetFirstPoint(_document, _selectedEntityIds, out var point))
        {
            _status = "Select an entity with a reference point first.";
            return;
        }

        _document = DrawingPrepService.MovePointToOrigin(_document, point);
        _status = "Moved first point from the selected entity to global origin.";
        _exportText = string.Empty;
    }

    private void AlignSelectedVectorToX() => AlignSelectedVector(AxisDirection.X);

    private void AlignSelectedVectorToY() => AlignSelectedVector(AxisDirection.Y);

    private void OrientLongAxisToX() => OrientLongAxis(AxisDirection.X);

    private void OrientLongAxisToY() => OrientLongAxis(AxisDirection.Y);

    private void AlignSelectedVector(AxisDirection axis)
    {
        var vectorId = _selectedEntityIds.FirstOrDefault();
        if (vectorId is null)
        {
            _status = "Select a line or polyline segment to use as the alignment vector.";
            return;
        }

        var before = _document;
        _document = DrawingPrepService.AlignVectorToAxis(_document, vectorId, axis);
        _status = ReferenceEquals(before, _document)
            ? "Selected entity does not expose a usable vector."
            : $"Aligned selected vector to global {axis}.";
        _exportText = string.Empty;
    }

    private void OrientLongAxis(AxisDirection axis)
    {
        _document = DrawingPrepService.OrientLongBoundsAxis(_document, axis);
        _status = $"Oriented long bounds axis to global {axis}.";
        _exportText = string.Empty;
    }

    private void SetGrainDirection(GrainDirection grainDirection)
    {
        _grainDirection = grainDirection;
        _status = grainDirection == GrainDirection.GlobalX
            ? "Grain annotation set to global X."
            : "Grain annotation set to global Y.";
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

    private static string FormatRange(double min, double max) => $"{FormatNumber(min)} to {FormatNumber(max)}";

    private static string FormatSize(double width, double height) => $"{FormatNumber(width)} x {FormatNumber(height)}";

    private static string FormatNumber(double value) => Round(value).ToString("0.###", CultureInfo.InvariantCulture);

    private static double Round(double value) => Math.Round(value, 4);
}

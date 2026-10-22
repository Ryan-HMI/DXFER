using System.Globalization;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Operations;
using DXFER.Core.Sketching;

namespace DXFER.Blazor.Components;

public partial class DrawingWorkbench
{
    private PatternDraft? _patternDraft;
    private PatternDefinition? _editingPattern;
    private DrawingDocument? _patternSourceDocument;
    private DrawingDocument? _patternPreview;
    private string? _patternError;
    private bool _confirmDissolvePattern;
    private readonly HashSet<string> _hiddenPatternControls = new(StringComparer.Ordinal);
    private string? _shownPatternControlsId;
    private bool _patternCenterDragging;
    private string? _patternCenterOriginalX;
    private string? _patternCenterOriginalY;
    private string? _patternDirectionPicking;
    private bool _patternDirectionSelectionConsumed;
    private (string Key, bool Additive)? _pendingPatternCenterPick;
    private bool PatternEditing => _patternDraft is not null;
    private DrawingDocument? PatternPreview => ReferenceEquals(_patternSourceDocument, _document) ? _patternPreview : null;
    private bool CanConfirmPatternPreview => !_patternCenterDragging && _patternDirectionPicking is null
        && PatternPreview is not null && _patternError is null;

    private PatternDefinition? ViewportPattern => ViewportPatternCandidate is { } pattern
        && !_hiddenPatternControls.Contains(pattern.Id) ? pattern : null;

    private PatternDefinition? ViewportPatternCandidate => PatternEditing
        ? PatternPreview?.Metadata.Patterns.FirstOrDefault(p => p.Id == _editingPattern?.Id) ?? _editingPattern
        : _document.Metadata.Patterns.FirstOrDefault(p => p.Id == _shownPatternControlsId)
          ?? SelectedPattern;

    private PatternDefinition? SelectedPattern => _document.Metadata.Patterns.FirstOrDefault(p => p.SourceEntityIds.Concat(p.Members.Select(m => m.EntityId))
            .Concat(p.CenterPointId is { } centerId ? new[] { centerId } : Array.Empty<string>())
            .Any(id => _selectedEntityIds.Any(key => key == id || key.StartsWith(id + "|", StringComparison.Ordinal))));

    private bool CanTogglePatternControls(PatternDefinition pattern) => !_patternCenterDragging && _patternDirectionPicking is null
        && (!PatternEditing || _editingPattern?.Id == pattern.Id);

    private void RetainSelectedPatternControls()
    {
        // Tool activation clears selection, but the selected pattern's center
        // must remain reachable as the next constraint or dimension reference.
        if (!PatternEditing && SelectedPattern is { } pattern && !_hiddenPatternControls.Contains(pattern.Id))
            _shownPatternControlsId = pattern.Id;
    }

    private void TogglePatternControls(PatternDefinition pattern)
    {
        if (!CanTogglePatternControls(pattern)) return;
        if (ViewportPattern?.Id == pattern.Id)
        {
            _hiddenPatternControls.Add(pattern.Id);
            if (_shownPatternControlsId == pattern.Id) _shownPatternControlsId = null;
        }
        else
        {
            _hiddenPatternControls.Remove(pattern.Id);
            _shownPatternControlsId = pattern.Id;
        }
        CloseContextMenu();
    }

    private DXFER.Core.Geometry.Point2 PatternSourceCenter(PatternDefinition pattern)
    {
        var bounds = PatternSourceBounds(pattern);
        return new((bounds.MinX + bounds.MaxX) / 2, (bounds.MinY + bounds.MaxY) / 2);
    }

    private Bounds2 PatternSourceBounds(PatternDefinition pattern) =>
        new DrawingDocument(_document.Entities.Where(e => pattern.SourceEntityIds.Contains(e.Id.Value))).GetBounds();

    private bool CanEditViewportPattern(PatternDefinition pattern) => !LayersReadOnly
        && pattern.SourceEntityIds.Concat(pattern.Members.Select(m => m.EntityId))
            .Concat(pattern.CenterPointId is { } centerId ? new[] { centerId } : Array.Empty<string>())
            .All(id => DrawingLayerService.CanEdit(_document, id));

    private void EditViewportPattern(PatternDefinition pattern)
    {
        if (CanEditViewportPattern(pattern)) OpenPatternEditor(pattern);
    }

    private Point2 PatternDirection(PatternDefinition pattern, bool uAxis)
    {
        return DrawingPatternService.TryGetDirections(PatternPreview ?? _document, pattern, out var u, out var v, out _)
            ? uAxis ? u : v : uAxis ? new(1, 0) : new(0, 1);
    }

    private bool CanChangePatternAxis => !_patternCenterDragging && _patternDraft is { Mode: not "Radial" }
        && _editingPattern is not null && ReferenceEquals(_patternSourceDocument, _document)
        && CanEditViewportPattern(_editingPattern);

    private void FlipPatternAxis(string axis)
    {
        if (axis is not ("U" or "V") || !CanChangePatternAxis) return;
        var pitch = axis == "U" ? _patternDraft!.PitchX : _patternDraft!.PitchY;
        if (!PatternNumber(pitch, out var value))
        {
            _patternPreview = null;
            _patternError = $"Enter a finite {axis} pitch before flipping direction.";
            _status = _patternError;
            return;
        }
        if (axis == "U") _patternDraft!.PitchX = Text(-value);
        else _patternDraft!.PitchY = Text(-value);
        RefreshPatternPreview();
    }

    private void BeginPatternDirectionPick(string axis)
    {
        if (axis is not ("U" or "V") || !CanChangePatternAxis) return;
        if (_patternDirectionPicking == axis) { CancelPatternDirectionPick(); return; }
        _patternDirectionPicking = axis;
        _hiddenPatternControls.Remove(_editingPattern!.Id);
        _patternError = null;
        _status = $"Select a line or straight polyline edge for pattern {axis} direction.";
    }

    private bool CancelPatternDirectionPick()
    {
        if (_patternDirectionPicking is null) return false;
        _patternDirectionPicking = null;
        RefreshPatternPreview();
        return true;
    }

    private void ClearPatternDirection(string axis)
    {
        if (axis is not ("U" or "V") || !CanChangePatternAxis) return;
        if (axis == "U") _patternDraft!.UDirectionReference = null;
        else _patternDraft!.VDirectionReference = null;
        _patternDirectionPicking = null;
        RefreshPatternPreview();
    }

    private bool TryConsumePatternDirectionSelection(IReadOnlySet<string> keys)
    {
        if (_patternDirectionPicking is not { } axis)
        {
            if (!PatternEditing) return false;
            // Delayed picker notifications must not replace the staged pattern's source selection.
            _patternDirectionSelectionConsumed = true;
            return true;
        }
        _patternDirectionSelectionConsumed = true;
        if (!CanChangePatternAxis)
        {
            _patternError = "Drawing changed or pattern geometry is protected. Cancel and reopen the pattern.";
        }
        else if (keys.Count == 0) return true;
        else if (keys.Count != 1 || !SketchReference.TryParse(keys.Single(), out var reference) || !reference.IsEntity)
        {
            _patternError = $"Select one line or straight polyline edge for pattern {axis} direction.";
        }
        else
        {
            var candidate = _editingPattern! with
            {
                Kind = _patternDraft!.Staggered ? PatternKind.Staggered : PatternKind.Rectangular,
                Columns = PatternCount(_patternDraft.Columns, out var columns) ? columns : _editingPattern.Columns,
                Rows = PatternCount(_patternDraft.Rows, out var rows) ? rows : _editingPattern.Rows,
                AngleDegrees = PatternNumber(_patternDraft.Angle, out var angle) ? angle : _editingPattern.AngleDegrees,
                UDirectionReference = axis == "U" ? reference.ToString() : _patternDraft.UDirectionReference,
                VDirectionReference = axis == "V" ? reference.ToString() : _patternDraft.VDirectionReference
            };
            if (DrawingPatternService.TryGetDirections(_document, candidate, out _, out _, out var reason))
            {
                if (axis == "U") _patternDraft.UDirectionReference = reference.ToString();
                else _patternDraft.VDirectionReference = reference.ToString();
                _patternDirectionPicking = null;
                RefreshPatternPreview();
                return true;
            }
            _patternError = reason;
        }
        _patternPreview = null;
        _status = _patternError;
        return true;
    }

    private void PickPatternDirection(string patternId, string axis, string reference)
    {
        if (_editingPattern?.Id != patternId || _patternDirectionPicking != axis || axis is not ("U" or "V")) return;
        TryConsumePatternDirectionSelection(new HashSet<string>(StringComparer.Ordinal) { reference });
        _patternDirectionSelectionConsumed = false;
        _ = InvokeAsync(StateHasChanged);
    }

    private void MovePatternCenter(string patternId, double x, double y, string phase)
    {
        if (phase is not ("begin" or "update" or "end" or "cancel")) return;
        if (phase == "begin")
        {
            if (_patternCenterDragging || !double.IsFinite(x) || !double.IsFinite(y)) return;
            var pattern = ViewportPattern;
            if (pattern is null || pattern.Id != patternId || pattern.Kind != PatternKind.Radial
                || !CanEditViewportPattern(pattern)) return;
            if (PatternEditing)
            {
                if (!ReferenceEquals(_patternSourceDocument, _document) || _patternDraft!.Mode != "Radial") return;
            }
            else
            {
                pattern = _document.Metadata.Patterns.FirstOrDefault(p => p.Id == patternId);
                if (pattern is null) return;
                OpenPatternEditor(pattern);
            }
            _patternCenterOriginalX = _patternDraft!.CenterX;
            _patternCenterOriginalY = _patternDraft.CenterY;
            _patternCenterDragging = true;
        }
        else
        {
            if (!_patternCenterDragging || _editingPattern?.Id != patternId || _patternDraft is null) return;
            var valid = double.IsFinite(x) && double.IsFinite(y) && _patternDraft.Mode == "Radial"
                && ReferenceEquals(_patternSourceDocument, _document) && CanEditViewportPattern(_editingPattern);
            if (phase == "update" && !valid) return;
            if (phase == "cancel" || !valid)
            {
                _patternDraft.CenterX = _patternCenterOriginalX!;
                _patternDraft.CenterY = _patternCenterOriginalY!;
                ClearPatternCenterDrag();
            }
            else
            {
                _patternDraft.CenterX = Text(x);
                _patternDraft.CenterY = Text(y);
                if (phase == "end") ClearPatternCenterDrag();
            }
            RefreshPatternPreview();
            if (phase == "end" && !valid && _patternError is null)
            {
                _patternPreview = null;
                _patternError = "Pattern center drag is no longer valid. Previous center restored.";
                _status = _patternError;
            }
        }
        _ = InvokeAsync(StateHasChanged);
    }

    private void SelectPatternCenter(string patternId, bool additive)
    {
        if (PatternEditing)
        {
            _status = "Confirm the pattern before constraining its center.";
        }
        else if (DrawingPatternService.TryExposeCenter(_document, patternId, out var next, out var reason))
        {
            if (!ReferenceEquals(next, _document)
                && !ApplyDocumentChangeCore(next, "Pattern center available for constraints.", patternCommand: true)) return;
            var pointId = _document.Metadata.Patterns.Single(p => p.Id == patternId).CenterPointId;
            if (pointId is not null)
            {
                _shownPatternControlsId = patternId;
                _pendingPatternCenterPick = (pointId, additive);
            }
        }
        else _status = reason;
        _ = InvokeAsync(StateHasChanged);
    }

    private void ClearPatternCenterDrag()
    {
        _patternCenterDragging = false;
        _patternCenterOriginalX = null;
        _patternCenterOriginalY = null;
    }

    private static string PatternLabel(PatternDefinition pattern) => pattern.Kind == PatternKind.Radial
        ? $"Radial ({pattern.Count})" : $"{pattern.Kind} ({pattern.Columns} x {pattern.Rows})";

    private IEnumerable<PatternDefinition> ContextPatterns => _document.Metadata.Patterns.Where(p =>
        p.SourceEntityIds.Any(id => _contextEntities.Contains(id)) || p.Members.Any(m => _contextEntities.Contains(m.EntityId))
        || p.CenterPointId is { } centerId && _contextEntities.Contains(centerId));

    private void BeginPattern(bool radial)
    {
        var ids = GetWholeEntityIdsForOperations().ToArray();
        if (ids.Length == 0 || LayersReadOnly)
        {
            _status = "Select the source geometry for a pattern.";
            return;
        }
        var bounds = new DrawingDocument(_document.Entities.Where(e => ids.Contains(e.Id.Value))).GetBounds();
        var definition = new PatternDefinition
        {
            Id = Guid.NewGuid().ToString("N"), SourceEntityIds = ids,
            Kind = radial ? PatternKind.Radial : PatternKind.Rectangular,
            Columns = 3, Rows = 2, Count = 6,
            XPitch = Math.Max(1, bounds.Width * 1.5), YPitch = Math.Max(1, bounds.Height * 1.5),
            Center = new Point2(0, 0), FullCircle = true, StepAngleDegrees = 30
        };
        OpenPatternEditor(definition);
    }

    private void OpenPatternEditor(PatternDefinition pattern)
    {
        CloseContextMenu();
        _hiddenPatternControls.Remove(pattern.Id);
        _shownPatternControlsId = pattern.Id;
        ActivateTool(pattern.Kind == PatternKind.Radial ? WorkbenchTool.CircularPattern : WorkbenchTool.LinearPattern, "Pattern preview.");
        _editingPattern = pattern;
        _patternSourceDocument = _document;
        _patternDraft = new PatternDraft
        {
            Mode = pattern.Kind.ToString(), Columns = Text(pattern.Columns), Rows = Text(pattern.Rows),
            PitchX = Text(pattern.XPitch), PitchY = Text(pattern.YPitch), Angle = Text(pattern.AngleDegrees),
            UDirectionReference = pattern.UDirectionReference, VDirectionReference = pattern.VDirectionReference,
            StaggerFraction = Text(pattern.StaggerFraction),
            Count = Text(pattern.Count), CenterX = Text(pattern.Center.X), CenterY = Text(pattern.Center.Y),
            StepAngle = Text(pattern.StepAngleDegrees), FullCircle = pattern.FullCircle
        };
        RefreshPatternPreview();
    }

    private static string Text(double value) => value.ToString("G17", CultureInfo.InvariantCulture);
    private static bool PatternNumber(string text, out double value) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
    private static bool PatternCount(string text, out int value) => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value > 0;

    private void RefreshPatternPreview()
    {
        _patternPreview = null;
        _patternError = null;
        if (_patternDraft is not { } draft || _editingPattern is null) return;
        if (!ReferenceEquals(_patternSourceDocument, _document))
        {
            _patternError = "Drawing changed. Cancel and reopen the pattern.";
            return;
        }
        if (!Enum.TryParse<PatternKind>(draft.Mode, out var mode)) return;
        if (mode == PatternKind.Radial) _patternDirectionPicking = null;
        var definition = _editingPattern with { Kind = mode };
        if (mode == PatternKind.Radial)
        {
            if (!PatternCount(draft.Count, out var count) || count < 2 ||
                !PatternNumber(draft.CenterX, out var x) || !PatternNumber(draft.CenterY, out var y) ||
                (!draft.FullCircle && !PatternNumber(draft.StepAngle, out _)))
                _patternError = "Enter a count of at least 2 and finite center/angle values.";
            else definition = definition with { Count = count, Center = new(x, y), FullCircle = draft.FullCircle,
                StepAngleDegrees = draft.FullCircle ? 360d / count : double.Parse(draft.StepAngle, CultureInfo.InvariantCulture) };
        }
        else
        {
            if (!PatternCount(draft.Rows, out var rows) || !PatternCount(draft.Columns, out var cols) ||
                !PatternNumber(draft.PitchX, out var x) || !PatternNumber(draft.PitchY, out var y) || !PatternNumber(draft.Angle, out var angle))
                _patternError = "Enter positive whole counts and finite pitch/rotation values.";
            else if (!PatternNumber(draft.StaggerFraction, out var stagger) || stagger is < 0 or > 1)
                _patternError = "Enter a stagger fraction between 0 and 1.";
            else definition = definition with
            {
                Rows = rows, Columns = cols, XPitch = x, YPitch = y, AngleDegrees = angle,
                UDirectionReference = draft.UDirectionReference, VDirectionReference = draft.VDirectionReference,
                StaggerFraction = stagger
            };
        }
        if (_patternError is null)
        {
            var exists = _document.Metadata.Patterns.Any(p => p.Id == definition.Id);
            DrawingDocument next;
            string reason;
            var accepted = exists
                ? DrawingPatternService.TryUpdate(_document, definition, out next, out reason)
                : DrawingPatternService.TryCreate(_document, definition, out next, out reason);
            if (accepted)
                _patternPreview = next;
            else _patternError = reason;
        }
        _status = _patternError ?? $"{mode} pattern preview.";
    }

    private void CommitPatternPreview()
    {
        if (!CanConfirmPatternPreview || PatternPreview is not { } preview) return;
        var previous = _document;
        ApplyDocumentChangeCore(preview, "Pattern saved.", patternCommand: true);
        if (!ReferenceEquals(previous, _document))
        {
            HideEditedPatternControls();
            ClearPatternEditor();
            _activeTool = null;
            ResetSelection();
            _focusCanvasAfterRender = true;
        }
    }

    private void DissolvePattern()
    {
        if (_editingPattern is not { } pattern || !ReferenceEquals(_patternSourceDocument, _document)) return;
        if (DrawingPatternService.TryDissolve(_document, pattern.Id, out var next, out var reason))
        {
            ApplyDocumentChangeCore(next, "Pattern unlinked; geometry retained.", patternCommand: true);
            ClearPatternEditor();
            _activeTool = null;
            ResetSelection();
        }
        else _patternError = reason;
    }

    private void HideEditedPatternControls()
    {
        if (_editingPattern is not { } pattern) return;
        _hiddenPatternControls.Add(pattern.Id);
        if (_shownPatternControlsId == pattern.Id) _shownPatternControlsId = null;
    }

    private void ClearPatternEditor()
    {
        ClearPatternCenterDrag();
        _patternDirectionPicking = null;
        _patternDirectionSelectionConsumed = false;
        _patternDraft = null; _editingPattern = null; _patternSourceDocument = null;
        _patternPreview = null; _patternError = null; _confirmDissolvePattern = false;
    }

    private void CommitStagedPreview()
    {
        if (PatternEditing) CommitPatternPreview(); else CommitCornerPreview();
    }
}

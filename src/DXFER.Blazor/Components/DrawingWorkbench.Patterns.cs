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
    private bool PatternEditing => _patternDraft is not null;
    private DrawingDocument? PatternPreview => ReferenceEquals(_patternSourceDocument, _document) ? _patternPreview : null;

    private static string PatternLabel(PatternDefinition pattern) => pattern.Kind == PatternKind.Radial
        ? $"Radial ({pattern.Count})" : $"{pattern.Kind} ({pattern.Columns} x {pattern.Rows})";

    private IEnumerable<PatternDefinition> ContextPatterns => _document.Metadata.Patterns.Where(p =>
        p.SourceEntityIds.Any(id => _contextEntities.Contains(id)) || p.Members.Any(m => _contextEntities.Contains(m.EntityId)));

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
        ActivateTool(pattern.Kind == PatternKind.Radial ? WorkbenchTool.CircularPattern : WorkbenchTool.LinearPattern, "Pattern preview.");
        _editingPattern = pattern;
        _patternSourceDocument = _document;
        _patternDraft = new PatternDraft
        {
            Mode = pattern.Kind.ToString(), Columns = Text(pattern.Columns), Rows = Text(pattern.Rows),
            PitchX = Text(pattern.XPitch), PitchY = Text(pattern.YPitch), Angle = Text(pattern.AngleDegrees),
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
            else definition = definition with { Rows = rows, Columns = cols, XPitch = x, YPitch = y, AngleDegrees = angle };
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
        if (PatternPreview is not { } preview || _patternError is not null) return;
        var previous = _document;
        ApplyDocumentChangeCore(preview, "Pattern saved.", patternCommand: true);
        if (!ReferenceEquals(previous, _document))
        {
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

    private void ClearPatternEditor()
    {
        _patternDraft = null; _editingPattern = null; _patternSourceDocument = null;
        _patternPreview = null; _patternError = null; _confirmDissolvePattern = false;
    }

    private void CommitStagedPreview()
    {
        if (PatternEditing) CommitPatternPreview(); else CommitCornerPreview();
    }
}

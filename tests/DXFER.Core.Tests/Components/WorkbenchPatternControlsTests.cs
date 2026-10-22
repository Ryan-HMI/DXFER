using System.Reflection;
using DXFER.Blazor.Components;
using DXFER.Core.Documents;
using DXFER.Core.Operations;
using FluentAssertions;

namespace DXFER.Core.Tests.Components;

public sealed class WorkbenchPatternControlsTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [Fact]
    public void HidingDuringEditPreservesDraftAndDoesNotAllowAnotherPatternToReplaceIt()
    {
        var (workbench, document, pattern) = Fixture();
        var draft = new PatternDraft { Columns = "5", PitchX = "1.25" };
        Set(workbench, "_patternDraft", draft);
        Set(workbench, "_editingPattern", pattern);
        Set(workbench, "_contextDocument", document);
        Property(workbench, "ContextEditable").Should().Be(false);
        Invoke(workbench, "TogglePatternControls", pattern);
        Property(workbench, "ViewportPattern").Should().BeNull();
        Field(workbench, "_patternDraft").Should().BeSameAs(draft);
        Field(workbench, "_document").Should().BeSameAs(document);
        Invoke(workbench, "TogglePatternControls", pattern with { Id = "other" });
        Property(workbench, "ViewportPattern").Should().BeNull();
        Invoke(workbench, "TogglePatternControls", pattern);
        Property(workbench, "ViewportPattern").Should().BeSameAs(pattern);
        ((Stack<DrawingDocument>)Field(workbench, "_undoStack")!).Should().BeEmpty();
    }

    private static (DrawingWorkbench, DrawingDocument, PatternDefinition) Fixture()
    {
        var source = new DrawingDocument([new CircleEntity(EntityId.Create("seed"), new(10, 10), 1)]);
        var definition = new PatternDefinition { Id = "pattern", SourceEntityIds = ["seed"], Columns = 3, Rows = 2, XPitch = 3, YPitch = 3 };
        DrawingPatternService.TryCreate(source, definition, out var document, out var reason).Should().BeTrue(reason);
        var workbench = new DrawingWorkbench();
        Set(workbench, "_document", document);
        return (workbench, document, document.Metadata.Patterns.Single());
    }

    private static object? Field(object target, string name) => typeof(DrawingWorkbench).GetField(name, Private)!.GetValue(target);
    private static object? Property(object target, string name) => typeof(DrawingWorkbench).GetProperty(name, Private)!.GetValue(target);
    private static void Set(object target, string name, object value) => typeof(DrawingWorkbench).GetField(name, Private)!.SetValue(target, value);
    private static void Invoke(object target, string name, params object[] args) => typeof(DrawingWorkbench).GetMethod(name, Private)!.Invoke(target, args);
}

using System.Reflection;
using DXFER.Blazor.Components;
using DXFER.Core.Documents;
using DXFER.Core.Operations;
using FluentAssertions;

namespace DXFER.Core.Tests.Components;

public sealed class WorkbenchDuplicateTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [Fact]
    public void CleanupUsesUndoRedoAndReadoutsFollowTheSameWorkingGeometry()
    {
        var workbench = new DrawingWorkbench();
        typeof(DrawingWorkbench).GetProperty("ToolHotkeys", Private)!.SetValue(workbench, new ToolHotkeyService());
        var field = typeof(DrawingWorkbench).GetField("_document", Private)!;
        var document = new DrawingDocument([
            new CircleEntity(EntityId.Create("first"), new(0, 0), 1),
            new CircleEntity(EntityId.Create("second"), new(0, 0), 1)]);
        field.SetValue(workbench, document);
        var commands = (IReadOnlyList<WorkbenchToolCommand>)typeof(DrawingWorkbench).GetProperty("SyncCleanupCommands", Private)!.GetValue(workbench)!;
        commands.Should().Contain(c => c.Label == "Remove duplicates");
        Invoke(workbench, "RemoveExactDuplicates");
        var cleaned = (DrawingDocument)field.GetValue(workbench)!;
        cleaned.Entities.Should().HaveCount(1);
        ClosedContourService.Calculate(cleaned).ClosedContourCount.Should().Be(1);
        Invoke(workbench, "UndoLastDocumentChange");
        field.GetValue(workbench).Should().BeSameAs(document);
        Invoke(workbench, "RedoLastDocumentChange");
        field.GetValue(workbench).Should().BeSameAs(cleaned);
        Invoke(workbench, "RemoveExactDuplicates");
        Invoke(workbench, "UndoLastDocumentChange");
        field.GetValue(workbench).Should().BeSameAs(document, "a no-op must not create another undo step");
    }

    private static void Invoke(DrawingWorkbench workbench, string method) =>
        typeof(DrawingWorkbench).GetMethod(method, Private)!.Invoke(workbench, null);
}

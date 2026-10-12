using System.Reflection;
using DXFER.Blazor.Components;
using DXFER.Core.Documents;
using FluentAssertions;

namespace DXFER.Core.Tests.Components;

public sealed class WorkbenchFullEditingTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [Fact]
    public void CleanupRemainsDefault_AndFullEditingIsExplicit()
    {
        var workbench = Create();
        Groups(workbench).Select(g => g.Name).Should().Equal("Edit", "Cleanup", "Grain");
        typeof(DrawingWorkbench).GetProperty(nameof(DrawingWorkbench.EnableFullEditing))!.SetValue(workbench, true);
        Groups(workbench).Select(g => g.Name).Should().Contain(["View", "Sketch", "Modify", "Transform", "Pattern", "Constraints", "Cleanup", "Grain"]);
        Groups(workbench).SelectMany(g => g.Commands).Should().Contain(c => c.Id == WorkbenchCommandId.Line && !c.Disabled);
    }

    [Fact]
    public void FullEditorRetainsImplementedDuplicateCleanup()
    {
        var workbench = Create();
        typeof(DrawingWorkbench).GetProperty(nameof(DrawingWorkbench.EnableFullEditing))!.SetValue(workbench, true);
        typeof(DrawingWorkbench).GetField("_document", Private)!.SetValue(workbench,
            new DrawingDocument([new CircleEntity(EntityId.Create("one"), new(0, 0), 1)]));
        var command = Groups(workbench).SelectMany(g => g.Commands).Single(c => c.Id == WorkbenchCommandId.RemoveDuplicates);
        command.Disabled.Should().BeFalse();
        command.IsFuture.Should().BeFalse();
    }

    private static DrawingWorkbench Create()
    {
        var workbench = new DrawingWorkbench();
        typeof(DrawingWorkbench).GetProperty("ToolHotkeys", Private)!.SetValue(workbench, new ToolHotkeyService());
        return workbench;
    }

    private static IReadOnlyList<WorkbenchToolGroup> Groups(DrawingWorkbench workbench) =>
        (IReadOnlyList<WorkbenchToolGroup>)typeof(DrawingWorkbench).GetProperty("ToolGroups", Private)!.GetValue(workbench)!;
}

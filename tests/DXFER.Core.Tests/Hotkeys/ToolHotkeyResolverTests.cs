using DXFER.Blazor.Components;
using FluentAssertions;

namespace DXFER.Core.Tests.Hotkeys;

public sealed class ToolHotkeyResolverTests
{
    [Fact]
    public void ExposesImplementedWorkbenchToolsForHotkeyEditing()
    {
        ToolHotkeyResolver.ToolCommandIds.Should().ContainInOrder(
            WorkbenchCommandId.Undo,
            WorkbenchCommandId.Redo,
            WorkbenchCommandId.Measure,
            WorkbenchCommandId.Line,
            WorkbenchCommandId.MidpointLine,
            WorkbenchCommandId.TwoPointRectangle,
            WorkbenchCommandId.CenterRectangle,
            WorkbenchCommandId.AlignedRectangle,
            WorkbenchCommandId.CenterCircle,
            WorkbenchCommandId.ThreePointCircle,
            WorkbenchCommandId.ThreePointArc,
            WorkbenchCommandId.CenterPointArc,
            WorkbenchCommandId.Point,
            WorkbenchCommandId.Construction,
            WorkbenchCommandId.SplitAtPoint,
            WorkbenchCommandId.Dimension);
        ToolHotkeyResolver.ToolCommandIds.Should().NotContain(WorkbenchCommandId.TangentArc);
    }

    [Fact]
    public void DefaultUndoRedoHotkeysUseControlZChords()
    {
        var bindings = ToolHotkeyResolver.GetDefaultBindings();

        ToolHotkeyResolver.TryResolve(
                bindings,
                new ToolHotkeyPress("z", CtrlKey: true),
                out var undoCommand)
            .Should().BeTrue();
        undoCommand.Should().Be(WorkbenchCommandId.Undo);

        ToolHotkeyResolver.TryResolve(
                bindings,
                new ToolHotkeyPress("Z", CtrlKey: true, ShiftKey: true),
                out var redoCommand)
            .Should().BeTrue();
        redoCommand.Should().Be(WorkbenchCommandId.Redo);
    }

    [Theory]
    [InlineData(WorkbenchCommandId.AlignedRectangle, "Aligned rectangle")]
    [InlineData(WorkbenchCommandId.CenterRectangle, "Center rectangle")]
    [InlineData(WorkbenchCommandId.ThreePointCircle, "Three-point circle")]
    [InlineData(WorkbenchCommandId.ThreePointArc, "Three-point arc")]
    [InlineData(WorkbenchCommandId.CenterPointArc, "Center point arc")]
    [InlineData(WorkbenchCommandId.SplitAtPoint, "Split at point")]
    public void FormatsImplementedToolNamesForHotkeyEditing(
        WorkbenchCommandId commandId,
        string expectedName)
    {
        ToolHotkeyResolver.FormatCommandName(commandId).Should().Be(expectedName);
    }

    [Theory]
    [InlineData("M", WorkbenchCommandId.MidpointLine)]
    [InlineData("r", WorkbenchCommandId.TwoPointRectangle)]
    [InlineData("C", WorkbenchCommandId.CenterCircle)]
    public void ResolvesDefaultCadToolHotkeys(string key, WorkbenchCommandId expectedCommand)
    {
        var bindings = ToolHotkeyResolver.GetDefaultBindings();

        ToolHotkeyResolver.TryResolve(bindings, ToolHotkeyPress.Plain(key), out var commandId)
            .Should().BeTrue();
        commandId.Should().Be(expectedCommand);
    }

    [Fact]
    public void DefaultLineHotkeyUsesShiftA()
    {
        var bindings = ToolHotkeyResolver.GetDefaultBindings();

        ToolHotkeyResolver.TryResolve(
                bindings,
                new ToolHotkeyPress("A", ShiftKey: true),
                out var commandId)
            .Should().BeTrue();
        commandId.Should().Be(WorkbenchCommandId.Line);
        ToolHotkeyResolver.TryResolve(bindings, ToolHotkeyPress.Plain("A"), out _)
            .Should().BeFalse();
    }

    [Fact]
    public void IgnoresHotkeysFromEditableTargets()
    {
        var bindings = ToolHotkeyResolver.GetDefaultBindings();

        var resolved = ToolHotkeyResolver.TryResolve(
            bindings,
            new ToolHotkeyPress("L", IsEditableTarget: true),
            out _);

        resolved.Should().BeFalse();
    }

    [Fact]
    public void ResolvesCustomModifierChord()
    {
        var bindings = ToolHotkeyResolver.UpdateBinding(
            ToolHotkeyResolver.GetDefaultBindings(),
            WorkbenchCommandId.Line,
            "ctrl+shift+l");

        ToolHotkeyResolver.TryResolve(
                bindings,
                new ToolHotkeyPress("l", CtrlKey: true, ShiftKey: true),
                out var commandId)
            .Should().BeTrue();
        commandId.Should().Be(WorkbenchCommandId.Line);
        ToolHotkeyResolver.TryResolve(bindings, ToolHotkeyPress.Plain("L"), out _)
            .Should().BeFalse();

        bindings.Single(binding => binding.CommandId == WorkbenchCommandId.Line)
            .Key.Should().Be("Ctrl+Shift+L");
    }

    [Theory]
    [InlineData("F2", "F2")]
    [InlineData("ctrl+arrowleft", "Ctrl+ArrowLeft")]
    [InlineData("Alt+Delete", "Alt+Delete")]
    [InlineData("Shift+/", "Shift+Slash")]
    [InlineData("Ctrl++", null)]
    [InlineData("Ctrl+Plus", "Ctrl+Plus")]
    public void NormalizesNonLetterHotkeys(string key, string? expectedKey)
    {
        ToolHotkeyResolver.NormalizeKey(key).Should().Be(expectedKey);
    }

    [Theory]
    [InlineData("F2", false, false, false, "F2")]
    [InlineData("ArrowLeft", true, false, false, "Ctrl+ArrowLeft")]
    [InlineData("Delete", false, true, false, "Alt+Delete")]
    [InlineData("/", false, false, true, "Shift+Slash")]
    [InlineData("Shift", false, false, false, null)]
    public void NormalizesNonLetterHotkeyPresses(
        string key,
        bool ctrlKey,
        bool altKey,
        bool shiftKey,
        string? expectedKey)
    {
        ToolHotkeyResolver.NormalizePress(new ToolHotkeyPress(key, ctrlKey, altKey, shiftKey))
            .Should()
            .Be(expectedKey);
    }

    [Fact]
    public void ResolvesCustomNonLetterHotkeys()
    {
        var bindings = ToolHotkeyResolver.UpdateBinding(
            ToolHotkeyResolver.GetDefaultBindings(),
            WorkbenchCommandId.Dimension,
            "F2");

        bindings = ToolHotkeyResolver.UpdateBinding(
            bindings,
            WorkbenchCommandId.DeleteSelection,
            "Delete");

        ToolHotkeyResolver.TryResolve(bindings, ToolHotkeyPress.Plain("F2"), out var dimensionCommand)
            .Should().BeTrue();
        dimensionCommand.Should().Be(WorkbenchCommandId.Dimension);

        ToolHotkeyResolver.TryResolve(bindings, ToolHotkeyPress.Plain("Delete"), out var deleteCommand)
            .Should().BeTrue();
        deleteCommand.Should().Be(WorkbenchCommandId.DeleteSelection);
    }

    [Fact]
    public void CustomBindingReplacesPreviousBindingForCommand()
    {
        var bindings = ToolHotkeyResolver.UpdateBinding(
            ToolHotkeyResolver.GetDefaultBindings(),
            WorkbenchCommandId.Line,
            "G");

        bindings.Should().ContainSingle(binding =>
            binding.CommandId == WorkbenchCommandId.Line && binding.Key == "G");
        ToolHotkeyResolver.TryResolve(bindings, ToolHotkeyPress.Plain("G"), out var commandId)
            .Should().BeTrue();
        commandId.Should().Be(WorkbenchCommandId.Line);
        ToolHotkeyResolver.TryResolve(bindings, ToolHotkeyPress.Plain("L"), out _)
            .Should().BeFalse();
    }

    [Fact]
    public void RejectsDuplicateCustomBinding()
    {
        var update = () => ToolHotkeyResolver.UpdateBinding(
            ToolHotkeyResolver.GetDefaultBindings(),
            WorkbenchCommandId.CenterCircle,
            "shift+a");

        update.Should().Throw<InvalidOperationException>()
            .WithMessage("*already assigned*");
    }

    [Fact]
    public void EmptyCustomBindingUnassignsCommand()
    {
        var bindings = ToolHotkeyResolver.UpdateBinding(
            ToolHotkeyResolver.GetDefaultBindings(),
            WorkbenchCommandId.Line,
            string.Empty);

        bindings.Should().ContainSingle(binding =>
            binding.CommandId == WorkbenchCommandId.Line && binding.Key == string.Empty);
        ToolHotkeyResolver.TryResolve(bindings, ToolHotkeyPress.Plain("L"), out _)
            .Should().BeFalse();
    }

    [Fact]
    public void LoadingStoredHotkeysAddsNewDefaultCommandsWithoutOverwritingCustomOnes()
    {
        var service = new ToolHotkeyService();

        service.Load(new[]
        {
            new ToolHotkeyBinding(WorkbenchCommandId.Line, "G")
        });

        service.GetKey(WorkbenchCommandId.Line).Should().Be("G");
        service.GetKey(WorkbenchCommandId.Undo).Should().Be("Ctrl+Z");
        service.GetKey(WorkbenchCommandId.Redo).Should().Be("Ctrl+Shift+Z");
    }

    [Fact]
    public void LoadingStoredHotkeysPreservesExplicitlyUnassignedCommands()
    {
        var service = new ToolHotkeyService();

        service.Load(new[]
        {
            new ToolHotkeyBinding(WorkbenchCommandId.Undo, string.Empty)
        });

        service.GetKey(WorkbenchCommandId.Undo).Should().Be(string.Empty);
    }
}

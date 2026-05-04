using DXFER.Blazor.Components;
using FluentAssertions;

namespace DXFER.Core.Tests.Hotkeys;

public sealed class ToolHotkeyResolverTests
{
    [Fact]
    public void ExposesImplementedWorkbenchToolsForHotkeyEditing()
    {
        ToolHotkeyResolver.ToolCommandIds.Should().ContainInOrder(
            WorkbenchCommandId.Measure,
            WorkbenchCommandId.Line,
            WorkbenchCommandId.MidpointLine,
            WorkbenchCommandId.TwoPointRectangle,
            WorkbenchCommandId.CenterRectangle,
            WorkbenchCommandId.AlignedRectangle,
            WorkbenchCommandId.CenterCircle,
            WorkbenchCommandId.ThreePointCircle,
            WorkbenchCommandId.ThreePointArc,
            WorkbenchCommandId.TangentArc,
            WorkbenchCommandId.CenterPointArc,
            WorkbenchCommandId.Point,
            WorkbenchCommandId.Construction,
            WorkbenchCommandId.SplitAtPoint,
            WorkbenchCommandId.Dimension);
    }

    [Theory]
    [InlineData(WorkbenchCommandId.AlignedRectangle, "Aligned rectangle")]
    [InlineData(WorkbenchCommandId.CenterRectangle, "Center rectangle")]
    [InlineData(WorkbenchCommandId.ThreePointCircle, "Three-point circle")]
    [InlineData(WorkbenchCommandId.ThreePointArc, "Three-point arc")]
    [InlineData(WorkbenchCommandId.TangentArc, "Tangent arc")]
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

        bindings.Should().NotContain(binding => binding.CommandId == WorkbenchCommandId.Line);
    }
}

using DXFER.Blazor.Components;
using FluentAssertions;

namespace DXFER.Core.Tests.Hotkeys;

public sealed class ToolHotkeyResolverTests
{
    [Theory]
    [InlineData("l", WorkbenchCommandId.Line)]
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
    public void IgnoresHotkeysFromEditableTargets()
    {
        var bindings = ToolHotkeyResolver.GetDefaultBindings();

        var resolved = ToolHotkeyResolver.TryResolve(
            bindings,
            new ToolHotkeyPress("L", IsEditableTarget: true),
            out _);

        resolved.Should().BeFalse();
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void IgnoresModifierChords(bool ctrlKey, bool altKey, bool metaKey)
    {
        var bindings = ToolHotkeyResolver.GetDefaultBindings();

        var resolved = ToolHotkeyResolver.TryResolve(
            bindings,
            new ToolHotkeyPress("L", CtrlKey: ctrlKey, AltKey: altKey, MetaKey: metaKey),
            out _);

        resolved.Should().BeFalse();
    }

    [Fact]
    public void AllowsShiftForLetterCaseWithoutChangingResolution()
    {
        var bindings = ToolHotkeyResolver.GetDefaultBindings();

        ToolHotkeyResolver.TryResolve(
                bindings,
                new ToolHotkeyPress("L", ShiftKey: true),
                out var commandId)
            .Should().BeTrue();
        commandId.Should().Be(WorkbenchCommandId.Line);
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
            "L");

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

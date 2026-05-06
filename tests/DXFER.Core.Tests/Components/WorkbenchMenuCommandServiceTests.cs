using DXFER.Blazor.Components;
using FluentAssertions;

namespace DXFER.Core.Tests.Components;

public sealed class WorkbenchMenuCommandServiceTests
{
    [Fact]
    public async Task InvokeAsyncRaisesBlankNewFileCommand()
    {
        var service = new WorkbenchMenuCommandService();
        WorkbenchCommandId? requested = null;
        service.CommandRequested += commandId =>
        {
            requested = commandId;
            return Task.CompletedTask;
        };

        await service.InvokeAsync(WorkbenchCommandId.NewBlankDocument);

        requested.Should().Be(WorkbenchCommandId.NewBlankDocument);
    }

    [Fact]
    public void OpenHotkeyOptionsRaisesRequest()
    {
        var service = new WorkbenchMenuCommandService();
        var requested = false;
        service.HotkeyOptionsRequested += () => requested = true;

        service.OpenHotkeyOptions();

        requested.Should().BeTrue();
    }
}

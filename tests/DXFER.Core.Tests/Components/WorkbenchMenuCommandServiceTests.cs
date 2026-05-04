using DXFER.Blazor.Components;
using FluentAssertions;

namespace DXFER.Core.Tests.Components;

public sealed class WorkbenchMenuCommandServiceTests
{
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

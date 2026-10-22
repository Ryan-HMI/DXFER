using DXFER.Blazor.Components;
using FluentAssertions;

namespace DXFER.Core.Tests.Components;

public sealed class WorkbenchMenuCommandServiceTests
{
    [Fact]
    public void WheelInversionDefaultsOffAndNotifiesOnlyViewChanges()
    {
        var service = new WorkbenchMenuCommandService();
        var values = new List<bool>();
        var snapChanges = 0;
        service.ViewSettingsChanged += () => values.Add(service.InvertWheelZoom);
        service.SnapSettingsChanged += () => snapChanges++;

        service.InvertWheelZoom.Should().BeFalse();
        service.InvertWheelZoom = false;
        service.InvertWheelZoom = true;
        service.InvertWheelZoom = true;
        service.InvertWheelZoom = false;

        values.Should().Equal(true, false);
        snapChanges.Should().Be(0);
        service.AutomaticSnapConstraints.Should().BeTrue();
        service.DocumentState.HasUnsavedChanges.Should().BeFalse();
    }

    [Fact]
    public void DocumentStateStartsEmptyAndEqualPublicationIsANoOp()
    {
        var service = new WorkbenchMenuCommandService();
        var original = service.DocumentState;
        var notifications = 0;
        service.DocumentStateChanged += () => notifications++;

        original.Should().Be(new WorkbenchDocumentState(string.Empty, string.Empty, false, false, false));
        service.SetDocumentState(string.Empty, string.Empty, false, false, false);

        service.DocumentState.Should().BeSameAs(original);
        notifications.Should().Be(0);
    }

    [Theory]
    [InlineData("part.dxf", "in", false, true, false)]
    [InlineData("other.dxf", "in", false, true, false)]
    [InlineData("part.dxf", "mm", false, true, false)]
    [InlineData("part.dxf", "in", true, true, false)]
    [InlineData("part.dxf", "in", false, false, false)]
    [InlineData("part.dxf", "in", false, true, true)]
    public void ChangedSnapshotPublishesAllValuesBeforeNotificationAndSuppressesDuplicates(
        string fileName, string units, bool modified, bool hasDocument, bool referenceOnly)
    {
        var service = new WorkbenchMenuCommandService();
        var snapshots = new List<WorkbenchDocumentState>();
        service.DocumentStateChanged += () => snapshots.Add(service.DocumentState);
        service.SetDocumentState("part.dxf", "in", false, true, false);
        var original = service.DocumentState;
        var expected = new WorkbenchDocumentState(fileName, units, modified, hasDocument, referenceOnly);

        service.SetDocumentState(fileName, units, modified, hasDocument, referenceOnly);
        var updated = service.DocumentState;
        service.SetDocumentState(fileName, units, modified, hasDocument, referenceOnly);

        updated.Should().Be(expected);
        service.DocumentState.Should().BeSameAs(updated);
        snapshots.Should().HaveCount(original == expected ? 1 : 2);
        snapshots[^1].Should().BeSameAs(updated);
        original.Should().Be(new WorkbenchDocumentState("part.dxf", "in", false, true, false));
    }

    [Fact]
    public void DocumentStateDoesNotInferSavedOrEditableStateFromSyncAndCanBeCleared()
    {
        var service = new WorkbenchMenuCommandService();
        var syncChanges = 0;
        var documentChanges = 0;
        service.SyncDocumentChanged += () => syncChanges++;
        service.DocumentStateChanged += () => documentChanges++;
        service.SetDocumentState("reference.dxf", "", true, true, true);
        var document = service.DocumentState;
        service.SetSyncDocument(true);
        service.DocumentState.Should().BeSameAs(document);
        document.HasUnsavedChanges.Should().BeTrue();
        document.IsReferenceOnly.Should().BeTrue();
        document.Units.Should().BeEmpty();

        service.SetDocumentState("", "", false, false, false);
        service.HasSyncDocument.Should().BeTrue();
        service.DocumentState.HasDocument.Should().BeFalse();
        syncChanges.Should().Be(1);
        documentChanges.Should().Be(2);
    }

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

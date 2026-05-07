using FluentAssertions;

namespace DXFER.Core.Tests.Components;

public sealed class WorkbenchRenderBoundaryTests
{
    [Fact]
    public void HomePageDoesNotCreateNestedInteractiveServerBoundary()
    {
        var homePage = FindRepositoryFile("src", "DXFER.Web", "Components", "Pages", "Home.razor");
        var source = File.ReadAllText(homePage);

        source.Should().NotContain(
            "@rendermode InteractiveServer",
            "Routes already owns the interactive server boundary; nesting the page boundary creates a separate scoped menu service from the layout");
    }

    [Fact]
    public void WorkbenchFileOpenRequestsScheduleWorkbenchRender()
    {
        var workbench = FindRepositoryFile("src", "DXFER.Blazor", "Components", "DrawingWorkbench.razor.cs");
        var source = File.ReadAllText(workbench);
        var methodStart = source.IndexOf("private async Task OpenFileAsync(IBrowserFile file)", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("    private void LoadSample()", StringComparison.Ordinal);

        methodStart.Should().BeGreaterThanOrEqualTo(0);
        methodEnd.Should().BeGreaterThan(methodStart);
        var methodBody = source[methodStart..methodEnd];

        methodBody.Should().Contain(
            "await InvokeAsync(StateHasChanged);",
            "header file-open requests are raised by the layout service, so the workbench must schedule its own render after loading the document");
    }

    [Fact]
    public void WorkbenchSaveDownloadsNormalizedDxfAndSidecar()
    {
        var workbench = FindRepositoryFile("src", "DXFER.Blazor", "Components", "DrawingWorkbench.razor.cs");
        var source = File.ReadAllText(workbench);
        var methodStart = source.IndexOf("private async Task DownloadDxfAsync()", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("    private void OnHoveredEntityChanged", StringComparison.Ordinal);

        methodStart.Should().BeGreaterThanOrEqualTo(0);
        methodEnd.Should().BeGreaterThan(methodStart);
        var methodBody = source[methodStart..methodEnd];

        methodBody.Should().Contain("DxferSidecarWriter.Write");
        methodBody.Should().Contain("DxfDownloadFileName.SidecarFromSourceName");
        methodBody.Split("\"downloadTextFile\"", StringSplitOptions.None)
            .Should().HaveCount(3, "Save DXF should emit the DXF download and the matching .dxfer.json sidecar download");
    }

    [Fact]
    public void WorkbenchBlocksDocumentChangesWhenDocumentIsReferenceOnly()
    {
        var workbench = FindRepositoryFile("src", "DXFER.Blazor", "Components", "DrawingWorkbench.razor.cs");
        var source = File.ReadAllText(workbench);
        var methodStart = source.IndexOf("private void ApplyDocumentChange(DrawingDocument nextDocument, string status)", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("    private void ClearHistory()", StringComparison.Ordinal);

        methodStart.Should().BeGreaterThanOrEqualTo(0);
        methodEnd.Should().BeGreaterThan(methodStart);
        var methodBody = source[methodStart..methodEnd];

        methodBody.Should().Contain("DrawingDocumentMode.ReferenceOnly");
        methodBody.Should().Contain("Reference-only document cannot be edited");
        methodBody.Should().Contain("return;");
    }

    private static string FindRepositoryFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate repository file '{Path.Combine(segments)}'.");
    }
}

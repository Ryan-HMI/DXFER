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

    [Fact]
    public void WorkbenchRoutesSketchSolvesThroughSolverAdapter()
    {
        var workbench = FindRepositoryFile("src", "DXFER.Blazor", "Components", "DrawingWorkbench.razor.cs");
        var source = File.ReadAllText(workbench);

        source.Should().Contain("private readonly ISketchSolver _sketchSolver = new LegacySketchSolverAdapter();");
        source.Should().Contain("private SketchSolveResult SolveSketchChange(");
        source.Should().NotContain("SketchConstraintService.ApplyConstraint(");
        source.Should().NotContain("SketchConstraintService.ApplyConstraints(");
        source.Should().NotContain("SketchDimensionSolverService.ApplyDimension(");
    }

    [Fact]
    public void WorkbenchSaveUsesSyncCallbackWhenLaunchContextIsPresent()
    {
        var workbench = FindRepositoryFile("src", "DXFER.Blazor", "Components", "DrawingWorkbench.razor.cs");
        var source = File.ReadAllText(workbench);

        source.Should().Contain("DrawingNormalizationService.AutoNormalize");
        source.Should().Contain("SyncCallbackClient");
        source.Should().Contain("manualOverride");
        source.Should().Contain("ExportJobFolderFallbackAsync");
        source.Should().Contain("SyncLaunchOptionsParser");
        source.Should().Contain("ParseQueryString");
    }

    [Fact]
    public void WorkbenchShowsVisibleSaveBackToSyncControls()
    {
        var markup = File.ReadAllText(FindRepositoryFile("src", "DXFER.Blazor", "Components", "DrawingWorkbench.razor"));
        var source = File.ReadAllText(FindRepositoryFile("src", "DXFER.Blazor", "Components", "DrawingWorkbench.razor.cs"));

        markup.Should().Contain("@if (IsSyncLaunch)");
        markup.Should().Contain("dxfer-sync-control-bar");
        markup.Should().Contain("Save back to Sync");
        markup.Should().Contain("Return to Sync");
        markup.Should().Contain("CanSaveBackToSync");
        markup.Should().Contain("SaveBackToSyncAsync");
        source.Should().Contain("private bool CanSaveBackToSync =>");
        source.Should().Contain("_syncLaunchOptions.IsCallbackConfigured");
        source.Should().Contain("_isSyncSaveInFlight");
        source.Should().Contain("private async Task SaveBackToSyncAsync()");
        source.Should().Contain("await SaveToSyncCallbackAsync();");
        source.Should().Contain("private void ReturnToSync()");
    }

    [Fact]
    public void WorkbenchSyncCallbackSaveBlocksDoubleSubmit()
    {
        var workbench = FindRepositoryFile("src", "DXFER.Blazor", "Components", "DrawingWorkbench.razor.cs");
        var source = File.ReadAllText(workbench);
        var methodStart = source.IndexOf("private async Task SaveToSyncCallbackAsync()", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("    private async Task<bool> ExportJobFolderFallbackAsync", StringComparison.Ordinal);

        methodStart.Should().BeGreaterThanOrEqualTo(0);
        methodEnd.Should().BeGreaterThan(methodStart);
        var methodBody = source[methodStart..methodEnd];

        methodBody.Should().Contain("if (_isSyncSaveInFlight)");
        methodBody.Should().Contain("_isSyncSaveInFlight = true;");
        methodBody.Should().Contain("_isSyncSaveInFlight = false;");
        methodBody.Should().Contain("await InvokeAsync(StateHasChanged);");
    }

    [Fact]
    public void WorkbenchSyncSaveControlsDoNotOverlapCommandBar()
    {
        var css = File.ReadAllText(FindRepositoryFile("src", "DXFER.Blazor", "Components", "DrawingWorkbench.razor.css"));

        css.Should().Contain("grid-template-rows: minmax(0, 1fr) auto auto !important;");
        css.Should().Contain(".dxfer-sync-control-bar {\n    grid-column: 1 / -1 !important;\n    grid-row: 2 !important;");
        css.Should().Contain(".dxfer-command-bar {\n    grid-column: 1 / -1 !important;\n    grid-row: 3 !important;");
        css.Should().NotContain("grid-template-rows: minmax(0, 1fr) auto !important;");
    }

    [Fact]
    public void SyncLaunchUsesProductionToolGroups()
    {
        var workbench = FindRepositoryFile("src", "DXFER.Blazor", "Components", "DrawingWorkbench.razor.cs");
        var source = File.ReadAllText(workbench);
        var productionStart = source.IndexOf("private IReadOnlyList<WorkbenchToolGroup> ProductionToolGroups", StringComparison.Ordinal);
        var allGroupsStart = source.IndexOf("    private IReadOnlyList<WorkbenchToolGroup> AllToolGroups", StringComparison.Ordinal);

        productionStart.Should().BeGreaterThanOrEqualTo(0);
        allGroupsStart.Should().BeGreaterThan(productionStart);
        var productionGroups = source[productionStart..allGroupsStart];

        source.Should().Contain("private IReadOnlyList<WorkbenchToolGroup> ToolGroups => IsSyncLaunch ? ProductionToolGroups : AllToolGroups;");
        productionGroups.Should().Contain("private IReadOnlyList<WorkbenchToolGroup> ProductionToolGroups => new[]");
        productionGroups.Should().Contain("new WorkbenchToolGroup(\"Cleanup\", SyncCleanupCommands, \"Prep\"");
        productionGroups.Should().Contain("private IReadOnlyList<WorkbenchToolCommand> SyncCleanupCommands => new[]");
        productionGroups.Should().Contain("Command(WorkbenchCommandId.BoundsToOrigin");
        productionGroups.Should().Contain("Command(WorkbenchCommandId.VectorToX");
        productionGroups.Should().NotContain("new WorkbenchToolGroup(\"View\", new[]");
        productionGroups.Should().NotContain("WorkbenchCommandId.RemoveDuplicates");
    }

    [Fact]
    public void WebProgramMapsSyncApiEndpoints()
    {
        var program = FindRepositoryFile("src", "DXFER.Web", "Program.cs");
        var source = File.ReadAllText(program);

        source.Should().Contain("app.MapGet(\"/api/dxfer/capabilities\"");
        source.Should().Contain("app.MapPost(\"/api/dxfer/normalize\"");
        source.Should().Contain("DrawingNormalizationService.AutoNormalize");
        source.Should().Contain("DxfDocumentReader.Read");
        source.Should().Contain("DxfDocumentWriter.Write");
    }

    [Fact]
    public void MainLayoutUsesCleanupOnlyMenuForSyncLaunch()
    {
        var layout = FindRepositoryFile("src", "DXFER.Web", "Components", "Layout", "MainLayout.razor");
        var source = File.ReadAllText(layout);
        var syncMenuStart = source.IndexOf("@if (IsSyncLaunch)", StringComparison.Ordinal);
        var nonSyncMenuStart = source.IndexOf("@if (!IsSyncLaunch)", StringComparison.Ordinal);

        syncMenuStart.Should().BeGreaterThanOrEqualTo(0);
        nonSyncMenuStart.Should().BeGreaterThan(syncMenuStart);
        var syncMenu = source[syncMenuStart..nonSyncMenuStart];

        source.Should().Contain("SyncLaunchOptionsParser");
        source.Should().Contain("ParseQueryString");
        source.Should().Contain("@if (!IsSyncLaunch)");
        source.Should().NotContain("Canvas prototype");
        source.Should().NotContain("Sync cleanup");
        syncMenu.Should().Contain("@if (IsSyncLaunch)");
        syncMenu.Should().Contain("WorkbenchCommandId.SaveDxf");
        syncMenu.Should().Contain("WorkbenchCommandId.BoundsToOrigin");
        syncMenu.Should().Contain("WorkbenchCommandId.VectorToX");
        syncMenu.Should().NotContain("WorkbenchCommandId.LoadSample");
        syncMenu.Should().NotContain("WorkbenchCommandId.RemoveDuplicates");
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

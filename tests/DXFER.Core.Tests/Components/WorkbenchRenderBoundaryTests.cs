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
    public void WorkbenchSaveDownloadsNormalizedDxfOnly()
    {
        var workbench = FindRepositoryFile("src", "DXFER.Blazor", "Components", "DrawingWorkbench.razor.cs");
        var source = File.ReadAllText(workbench);
        var methodStart = source.IndexOf("private async Task DownloadDxfAsync()", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("    private async Task SaveBackToSyncAsync()", StringComparison.Ordinal);

        methodStart.Should().BeGreaterThanOrEqualTo(0);
        methodEnd.Should().BeGreaterThan(methodStart);
        var methodBody = source[methodStart..methodEnd];

        methodBody.Should().Contain("await DownloadDxfFilesAsync();");
        methodBody.Should().NotContain("SaveBackToSyncAsync");
        methodBody.Should().NotContain("IsCallbackConfigured");

        var downloadStart = source.IndexOf("private async Task DownloadDxfFilesAsync()", StringComparison.Ordinal);
        var downloadEnd = source.IndexOf("    private async Task SaveToSyncCallbackAsync()", StringComparison.Ordinal);
        downloadStart.Should().BeGreaterThanOrEqualTo(0);
        downloadEnd.Should().BeGreaterThan(downloadStart);
        var downloadBody = source[downloadStart..downloadEnd];

        downloadBody.Should().Contain("CreateDxfWriteOptions");
        source.Should().Contain("GRAIN");
        downloadBody.Should().NotContain("DxferSidecarWriter.Write");
        downloadBody.Should().NotContain("DxfDownloadFileName.SidecarFromSourceName");
        downloadBody.Should().NotContain("metadataJson");
        downloadBody.Split("\"downloadTextFile\"", StringSplitOptions.None)
            .Should().HaveCount(2, "Download DXF should emit only the normalized DXF download");
    }

    [Fact]
    public void WorkbenchDoesNotRenderInspectorOrJsonDebugPanel()
    {
        var markup = File.ReadAllText(FindRepositoryFile("src", "DXFER.Blazor", "Components", "DrawingWorkbench.razor"));
        var source = File.ReadAllText(FindRepositoryFile("src", "DXFER.Blazor", "Components", "DrawingWorkbench.razor.cs"));
        var css = File.ReadAllText(FindRepositoryFile("src", "DXFER.Blazor", "Components", "DrawingWorkbench.razor.css"));

        markup.Should().NotContain("dxfer-inspector");
        markup.Should().NotContain("Show inspector");
        markup.Should().NotContain("Sync Metadata");
        markup.Should().NotContain("DXF Export");
        source.Should().NotContain("_exportText");
        source.Should().NotContain("BuildSyncMetadataJson");
        source.Should().NotContain("ToggleInspector");
        css.Should().NotContain("dxfer-inspector");
        css.Should().NotContain("dxfer-metadata");
        css.Should().NotContain("dxfer-export");
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
        source.Should().NotContain("dxfer.json");
        source.Should().NotContain("metadataJson");
        source.Should().Contain("SyncLaunchOptionsParser");
        source.Should().Contain("ParseQueryString");
    }

    [Fact]
    public void WorkbenchShowsCompactSyncStatusAndSingleSaveAction()
    {
        var markup = File.ReadAllText(FindRepositoryFile("src", "DXFER.Blazor", "Components", "DrawingWorkbench.razor"));
        var source = File.ReadAllText(FindRepositoryFile("src", "DXFER.Blazor", "Components", "DrawingWorkbench.razor.cs"));

        markup.Should().Contain("@if (IsSyncLaunch)");
        markup.Should().Contain("dxfer-sync-control-bar");
        markup.Should().Contain("aria-label=\"Sync connection status\"");
        markup.Should().Contain("@SyncCallbackStateText");
        markup.Should().Contain("dxfer-sync-send-button");
        markup.Should().Contain("@SyncSaveButtonText");
        markup.Should().Contain("WorkbenchCommandId.SendToSync");
        markup.Should().NotContain("Open local DXF/DWG");
        markup.Should().NotContain("Download DXF");
        markup.Should().NotContain("Send to Sync");
        markup.Should().NotContain("Save back to Sync");
        markup.Should().NotContain("<InputFile OnChange=\"OpenInlineFileAsync\"");
        markup.Should().NotContain("@onclick=\"DownloadDxfFilesAsync\"");
        markup.Should().NotContain("Return to Sync");
        source.Should().Contain("private bool CanSaveBackToSync =>");
        source.Should().Contain("_syncLaunchOptions.IsCallbackConfigured");
        source.Should().Contain("_isSyncSaveInFlight");
        source.Should().Contain("private async Task SaveBackToSyncAsync()");
        source.Should().Contain("await SaveToSyncCallbackAsync();");
        source.Should().Contain("TryCloseAfterSuccessfulSyncSaveAsync");
        source.Should().Contain("closeLaunchedSyncTab");
        source.Should().NotContain("private void ReturnToSync()");
        source.Should().NotContain("Navigation.NavigateTo(_syncLaunchOptions.ReturnUrl");
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
    public void WorkbenchSyncSaveButtonStaysClickableWhenCallbackIsReady()
    {
        var workbench = FindRepositoryFile("src", "DXFER.Blazor", "Components", "DrawingWorkbench.razor.cs");
        var source = File.ReadAllText(workbench);
        var propertyStart = source.IndexOf("private bool CanSaveBackToSync =>", StringComparison.Ordinal);
        var propertyEnd = source.IndexOf("    private string SyncCallbackStateText", StringComparison.Ordinal);
        var saveStart = source.IndexOf("private async Task SaveToSyncCallbackAsync()", StringComparison.Ordinal);
        var saveEnd = source.IndexOf("    private async Task TryCloseAfterSuccessfulSyncSaveAsync()", StringComparison.Ordinal);

        propertyStart.Should().BeGreaterThanOrEqualTo(0);
        propertyEnd.Should().BeGreaterThan(propertyStart);
        saveStart.Should().BeGreaterThanOrEqualTo(0);
        saveEnd.Should().BeGreaterThan(saveStart);

        var propertyBody = source[propertyStart..propertyEnd];
        var saveBody = source[saveStart..saveEnd];

        propertyBody.Should().Contain("_syncLaunchOptions.IsCallbackConfigured");
        propertyBody.Should().Contain("!_isSyncSaveInFlight");
        propertyBody.Should().NotContain("HasDocument",
            "the button should remain clickable for ready Sync launches so a failed/missing document can report a status instead of looking inert");
        saveBody.Should().Contain("if (!HasDocument)");
    }

    [Fact]
    public void WorkbenchAutoCleanupRefitsCanvasAfterNormalization()
    {
        var workbench = FindRepositoryFile("src", "DXFER.Blazor", "Components", "DrawingWorkbench.razor.cs");
        var source = File.ReadAllText(workbench);
        var methodStart = source.IndexOf("private void ApplyAutoCleanup()", StringComparison.Ordinal);
        var methodEnd = source.IndexOf("    private void DeleteSelectedGeometry()", StringComparison.Ordinal);

        methodStart.Should().BeGreaterThanOrEqualTo(0);
        methodEnd.Should().BeGreaterThan(methodStart);
        var methodBody = source[methodStart..methodEnd];

        methodBody.Should().Contain("DrawingNormalizationService.AutoNormalize(_document)");
        methodBody.Should().Contain("_documentFitToken++;",
            "auto-normalize can move distant geometry to origin and must refit the canvas immediately");
    }

    [Fact]
    public void WorkbenchSyncSaveControlsDoNotOverlapCommandBar()
    {
        var css = File.ReadAllText(FindRepositoryFile("src", "DXFER.Blazor", "Components", "DrawingWorkbench.razor.css"));

        css.Should().Contain("grid-template-rows: minmax(0, 1fr) auto !important;");
        css.Should().Contain(".dxfer-sync-control-bar {\n    position: absolute !important;");
        css.Should().Contain("right: 0.55rem !important;");
        css.Should().Contain("top: 0.55rem !important;");
        css.Should().Contain(".dxfer-command-bar {\n    grid-column: 1 / -1 !important;\n    grid-row: 2 !important;");
        css.Should().NotContain("grid-template-rows: minmax(0, 1fr) auto auto !important;");
    }

    [Fact]
    public void ProductionSurfaceUsesCleanupOnlyToolGroups()
    {
        var workbench = FindRepositoryFile("src", "DXFER.Blazor", "Components", "DrawingWorkbench.razor.cs");
        var source = File.ReadAllText(workbench);
        var productionStart = source.IndexOf("private IReadOnlyList<WorkbenchToolGroup> ProductionToolGroups", StringComparison.Ordinal);
        var allGroupsStart = source.IndexOf("    private IReadOnlyList<WorkbenchToolGroup> AllToolGroups", StringComparison.Ordinal);

        productionStart.Should().BeGreaterThanOrEqualTo(0);
        allGroupsStart.Should().BeGreaterThan(productionStart);
        var productionGroups = source[productionStart..allGroupsStart];

        source.Should().Contain("private IReadOnlyList<WorkbenchToolGroup> ToolGroups => ProductionToolGroups;");
        source.Should().NotContain("private IReadOnlyList<WorkbenchToolGroup> ToolGroups => IsSyncLaunch ? ProductionToolGroups : AllToolGroups;");
        productionGroups.Should().Contain("private IReadOnlyList<WorkbenchToolGroup> ProductionToolGroups => new[]");
        productionGroups.Should().Contain("new WorkbenchToolGroup(\"Cleanup\", SyncCleanupCommands, \"Prep\"");
        productionGroups.Should().Contain("new WorkbenchToolGroup(\"Grain\", GrainCommands, \"Mark\"");
        productionGroups.Should().Contain("private IReadOnlyList<WorkbenchToolCommand> SyncCleanupCommands => new[]");
        productionGroups.Should().Contain("private IReadOnlyList<WorkbenchToolCommand> GrainCommands => new[]");
        productionGroups.Should().Contain("Command(WorkbenchCommandId.FitExtents");
        productionGroups.Should().Contain("Command(WorkbenchCommandId.AutoCleanup");
        productionGroups.Should().Contain("Command(WorkbenchCommandId.AutoCleanup, null, CadIconName.AutoCleanup, \"Auto\", !HasDocument");
        productionGroups.Should().NotContain("Command(WorkbenchCommandId.AutoCleanup, null, CadIconName.AutoCleanup, \"Auto\", !CanModifySelectedGeometry");
        productionGroups.Should().Contain("CadIconName.AutoCleanup");
        productionGroups.Should().Contain("Command(WorkbenchCommandId.Rotate, WorkbenchTool.Rotate");
        productionGroups.Should().Contain("Command(WorkbenchCommandId.BoundsToOrigin");
        productionGroups.Should().Contain("Command(WorkbenchCommandId.VectorToX");
        productionGroups.Should().Contain("CadIconName.GrainNone");
        productionGroups.Should().Contain("CadIconName.GrainX");
        productionGroups.Should().Contain("CadIconName.GrainY");
        productionGroups.Should().Contain("CadIconName.GrainVector");
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
        source.Should().Contain("syncImportEndpoint = \"/api/dxfer/normalize\"");
        source.Should().Contain("syncExportCallbackPath = \"/api/dxfer/edit-callback\"");
        source.Should().Contain("manualFileControls = new[]");
        source.Should().Contain("DrawingNormalizationService.AutoNormalize");
        source.Should().Contain("DxfDocumentReader.Read");
        source.Should().Contain("DxfDocumentWriter.Write");
        source.Should().Contain("AddAuthentication");
        source.Should().Contain("AddGoogle");
        source.Should().Contain("UseAuthentication");
        source.Should().Contain("UseForwardedHeaders",
            "Google redirects must honor Caddy's X-Forwarded-Proto header so redirect_uri stays https://dxfer.sync.harrisonmetals.com/signin-google");
        source.Should().Contain("RequireDxferAccessAsync");
        source.Should().Contain("IsSyncLaunchRequest");
        source.Should().Contain("X-DXFER-API-Key");
        source.Should().Contain("SignInSyncLaunchSessionAsync");
        source.Should().Contain("harrisonmetals.com");
        source.Should().NotContain("metadataJson");
    }

    [Fact]
    public void MainLayoutUsesCleanupOnlyMenuInProduction()
    {
        var layout = FindRepositoryFile("src", "DXFER.Web", "Components", "Layout", "MainLayout.razor");
        var source = File.ReadAllText(layout);

        source.Should().Contain("SyncLaunchOptionsParser");
        source.Should().Contain("@if (IsSyncLaunch)");
        source.Should().NotContain("@if (!IsSyncLaunch)");
        source.Should().NotContain("Canvas prototype");
        source.Should().NotContain("Sync cleanup");
        source.Should().Contain("Open local DXF/DWG...");
        source.Should().Contain("Download DXF");
        source.Should().Contain("WorkbenchCommandId.SendToSync");
        source.Should().Contain("Save to Sync");
        source.Should().NotContain("WorkbenchCommandId.ReturnToSync");
        source.Should().NotContain("Return to Sync");
        source.Should().Contain("WorkbenchCommandId.SaveDxf");
        source.Should().Contain("WorkbenchCommandId.BoundsToOrigin");
        source.Should().Contain("WorkbenchCommandId.VectorToX");
        source.Should().NotContain(">Save DXF<");
        source.Should().NotContain("WorkbenchCommandId.LoadSample");
        source.Should().NotContain("WorkbenchCommandId.ExportDxfText");
        source.Should().NotContain("WorkbenchCommandId.Line");
        source.Should().NotContain("WorkbenchCommandId.RemoveDuplicates");
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

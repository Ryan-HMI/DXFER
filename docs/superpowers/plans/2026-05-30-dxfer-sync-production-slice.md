# DXFER Sync Production Slice Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a production DXFER slice that launches from HMI-Sync, auto-normalizes a trusted DXF, and saves the result back through a Sync callback API.

**Architecture:** Keep Sync integration behind small request/result contracts in `DXFER.Core`, keep DXF serialization in `DXFER.CadIO`, and expose HTTP behavior from `DXFER.Web` minimal endpoints/services. The existing feature-rich Blazor workbench remains available on the `dev` branch, while `production` adds a Sync-safe service path and hides unneeded editing surface from Sync launch flow.

**Tech Stack:** .NET 8/10 SDK build environment, ASP.NET Core minimal APIs, Razor components, existing `DXFER.Core`, `DXFER.CadIO`, xUnit, FluentAssertions, Node test runner for Blazor JavaScript.

---

## File Structure

- Create `src/DXFER.Core/Operations/DrawingNormalizationService.cs`: best-fit rotation, origin relocation, and normalization result.
- Create `src/DXFER.Core/Operations/DrawingNormalizationResult.cs`: immutable result with before/after bounds, rotation, shift, and manual override flag.
- Create `src/DXFER.Core/Sync/SyncEditLaunchOptions.cs`: parsed launch contract.
- Create `src/DXFER.Core/Sync/SyncSavePackage.cs`: callback package metadata independent of ASP.NET types.
- Create `src/DXFER.Core/Sync/GrainDirectionOption.cs`: callback-safe enum `None|X|Y`.
- Create `src/DXFER.Web/Sync/SyncLaunchOptionsParser.cs`: reads query parameters into `SyncEditLaunchOptions`.
- Create `src/DXFER.Web/Sync/SyncCallbackClient.cs`: multipart callback post implementation.
- Create `src/DXFER.Web/Sync/SyncCallbackOptions.cs`: callback endpoint settings.
- Modify `src/DXFER.Web/Program.cs`: register parser/client and add Sync production endpoints.
- Modify `src/DXFER.Blazor/Components/DrawingWorkbench.razor.cs`: add launch-state loading and callback save hooks, keeping normal file save available outside Sync launch.
- Test `tests/DXFER.Core.Tests/Operations/DrawingNormalizationServiceTests.cs`.
- Test `tests/DXFER.Core.Tests/Sync/SyncContractTests.cs`.
- Test `tests/DXFER.Core.Tests/Components/WorkbenchRenderBoundaryTests.cs` for production surface guardrails if UI source assertions are needed.

### Task 1: Normalization Service

**Files:**
- Create: `src/DXFER.Core/Operations/DrawingNormalizationResult.cs`
- Create: `src/DXFER.Core/Operations/DrawingNormalizationService.cs`
- Test: `tests/DXFER.Core.Tests/Operations/DrawingNormalizationServiceTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact]
public void AutoNormalizeRotatedRectangleMinimizesBoundsAndMovesMinimumToOrigin()
{
    var original = RectangleDocument(width: 20, height: 10, degrees: 30, offsetX: 50, offsetY: -7);

    var result = DrawingNormalizationService.AutoNormalize(original);

    result.RotationDegrees.Should().BeApproximately(-30, 0.01);
    result.NormalizedDocument.GetBounds().MinX.Should().BeApproximately(0, 0.0001);
    result.NormalizedDocument.GetBounds().MinY.Should().BeApproximately(0, 0.0001);
    result.NormalizedDocument.GetBounds().Width.Should().BeApproximately(20, 0.01);
    result.NormalizedDocument.GetBounds().Height.Should().BeApproximately(10, 0.01);
    result.ManualOverride.Should().BeFalse();
}

[Fact]
public void AutoNormalizeReportsOriginShiftAppliedAfterRotation()
{
    var document = new DrawingDocument(new DrawingEntity[]
    {
        new LineEntity(EntityId.Create("edge"), new Point2(12, -4), new Point2(22, -4))
    });

    var result = DrawingNormalizationService.AutoNormalize(document);

    result.OriginShiftX.Should().BeApproximately(-12, 0.0001);
    result.OriginShiftY.Should().BeApproximately(4, 0.0001);
}
```

- [ ] **Step 2: Verify tests fail**

Run: `dotnet test tests\DXFER.Core.Tests\DXFER.Core.Tests.csproj --filter DrawingNormalizationServiceTests`

Expected: compile failure because `DrawingNormalizationService` is not defined.

- [ ] **Step 3: Implement minimal service**

Create result and service types. Use supported entity sample points, candidate angles derived from point pairs, `DrawingPrepService.Transform`, `Transform2.RotationDegreesAbout`, and `Transform2.Translation`.

- [ ] **Step 4: Verify service tests pass**

Run: `dotnet test tests\DXFER.Core.Tests\DXFER.Core.Tests.csproj --filter DrawingNormalizationServiceTests`

Expected: all `DrawingNormalizationServiceTests` pass.

### Task 2: Sync Contract Types

**Files:**
- Create: `src/DXFER.Core/Sync/GrainDirectionOption.cs`
- Create: `src/DXFER.Core/Sync/SyncEditLaunchOptions.cs`
- Create: `src/DXFER.Core/Sync/SyncSavePackage.cs`
- Test: `tests/DXFER.Core.Tests/Sync/SyncContractTests.cs`

- [ ] **Step 1: Write failing contract tests**

```csharp
[Fact]
public void SavePackageKeepsSyncAsSourceOfTruth()
{
    var package = new SyncSavePackage(
        ArtifactId: "artifact-1",
        JobId: "job-1",
        EditToken: "token",
        NormalizedDxfFileName: "normalized.dxf",
        NormalizedDxfContent: "0\nEOF\n",
        BoundingWidth: 20,
        BoundingHeight: 10,
        RotationDegrees: -30,
        OriginShiftX: -4,
        OriginShiftY: 8,
        GrainDirection: GrainDirectionOption.X,
        ManualOverride: true);

    package.GrainDirection.Should().Be(GrainDirectionOption.X);
    package.ManualOverride.Should().BeTrue();
}
```

- [ ] **Step 2: Verify contract tests fail**

Run: `dotnet test tests\DXFER.Core.Tests\DXFER.Core.Tests.csproj --filter SyncContractTests`

Expected: compile failure because `SyncSavePackage` is not defined.

- [ ] **Step 3: Implement contract records**

Add immutable records with required string validation only at API boundary, not constructors, so tests can build simple examples.

- [ ] **Step 4: Verify contract tests pass**

Run: `dotnet test tests\DXFER.Core.Tests\DXFER.Core.Tests.csproj --filter SyncContractTests`

Expected: all `SyncContractTests` pass.

### Task 3: Web Launch Parsing And Callback Client

**Files:**
- Create: `src/DXFER.Web/Sync/SyncLaunchOptionsParser.cs`
- Create: `src/DXFER.Web/Sync/SyncCallbackClient.cs`
- Create: `src/DXFER.Web/Sync/SyncCallbackOptions.cs`
- Modify: `src/DXFER.Web/Program.cs`
- Test: `tests/DXFER.Core.Tests/Sync/SyncContractTests.cs`

- [ ] **Step 1: Write failing parser tests**

```csharp
[Fact]
public void LaunchParserReadsSyncQueryContract()
{
    var query = new Dictionary<string, string?>
    {
        ["syncBaseUrl"] = "https://sync.local",
        ["artifactId"] = "artifact-1",
        ["jobId"] = "job-1",
        ["editToken"] = "token",
        ["inputPath"] = "C:/temp/input.dxf",
        ["returnUrl"] = "https://sync.local/return",
        ["jobFolder"] = "C:/temp/job"
    };

    var options = SyncLaunchOptionsParser.Parse(query);

    options.SyncBaseUrl.Should().Be("https://sync.local");
    options.ArtifactId.Should().Be("artifact-1");
    options.JobId.Should().Be("job-1");
    options.EditToken.Should().Be("token");
    options.InputPath.Should().Be("C:/temp/input.dxf");
    options.JobFolder.Should().Be("C:/temp/job");
}
```

- [ ] **Step 2: Verify parser test fails**

Run: `dotnet test tests\DXFER.Core.Tests\DXFER.Core.Tests.csproj --filter LaunchParserReadsSyncQueryContract`

Expected: compile failure because parser does not exist.

- [ ] **Step 3: Implement parser and callback client**

Parser accepts `IEnumerable<KeyValuePair<string,string?>>`. Callback client posts multipart form data to `{syncBaseUrl}/api/dxfer/edit-callback` unless configured with a different relative callback path.

- [ ] **Step 4: Verify parser/client tests pass**

Run: `dotnet test tests\DXFER.Core.Tests\DXFER.Core.Tests.csproj --filter SyncContractTests`

Expected: all Sync contract tests pass.

### Task 4: Production Save Flow And Fallback Export

**Files:**
- Modify: `src/DXFER.Blazor/Components/DrawingWorkbench.razor.cs`
- Modify: `src/DXFER.Blazor/Components/DrawingWorkbench.razor`
- Modify: `src/DXFER.Web/Components/Pages/Home.razor`
- Test: `tests/DXFER.Core.Tests/Components/WorkbenchRenderBoundaryTests.cs`

- [ ] **Step 1: Write failing source-boundary tests**

```csharp
[Fact]
public void WorkbenchSaveUsesSyncCallbackWhenLaunchContextIsPresent()
{
    var workbench = FindRepositoryFile("src", "DXFER.Blazor", "Components", "DrawingWorkbench.razor.cs");
    var source = File.ReadAllText(workbench);

    source.Should().Contain("SyncCallbackClient");
    source.Should().Contain("manualOverride");
    source.Should().Contain("ExportJobFolderFallbackAsync");
}
```

- [ ] **Step 2: Verify source-boundary test fails**

Run: `dotnet test tests\DXFER.Core.Tests\DXFER.Core.Tests.csproj --filter WorkbenchSaveUsesSyncCallbackWhenLaunchContextIsPresent`

Expected: failure because the Sync callback terms are absent.

- [ ] **Step 3: Implement production save flow**

Load launch options from query on app startup, auto-normalize after file load, build `SyncSavePackage` on Save, call `SyncCallbackClient`, and write `normalized.dxf` only when callback fails or fallback is explicitly requested.

- [ ] **Step 4: Verify source-boundary test passes**

Run: `dotnet test tests\DXFER.Core.Tests\DXFER.Core.Tests.csproj --filter WorkbenchSaveUsesSyncCallbackWhenLaunchContextIsPresent`

Expected: test passes.

### Task 5: Full Verification

**Files:**
- All modified production files.

- [ ] **Step 1: Restore packages**

Run: `dotnet restore DXFER.slnx`

Expected: restore exits 0.

- [ ] **Step 2: Build production worktree**

Run: `dotnet build DXFER.slnx --no-restore`

Expected: build exits 0. If DLL copy fails because `DXFER.Web.exe` is running, stop the stale process and rerun before treating it as a code issue.

- [ ] **Step 3: Run .NET tests**

Run: `dotnet test tests\DXFER.Core.Tests\DXFER.Core.Tests.csproj --no-build`

Expected: all xUnit tests pass.

- [ ] **Step 4: Run Blazor JavaScript tests**

Run: `node --test tests\DXFER.Blazor.Tests\*.test.mjs`

Expected: all Node tests pass.

- [ ] **Step 5: Review branch diff**

Run: `git diff --check`

Expected: no whitespace errors.

## Plan Self Review

- Spec coverage: branch/worktree layout, normalization, launch contract, callback save, job-folder fallback, and no direct Sync DB/storage writes are represented.
- Placeholder scan: no `TBD`, `TODO`, or future-fill steps remain.
- Type consistency: contract names are stable across tasks: `DrawingNormalizationService`, `DrawingNormalizationResult`, `SyncEditLaunchOptions`, `SyncSavePackage`, `GrainDirectionOption`, `SyncLaunchOptionsParser`, and `SyncCallbackClient`.

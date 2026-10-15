using System.Reflection;
using DXFER.Blazor.Components;
using DXFER.CadIO;
using DXFER.Core.Documents;
using DXFER.Core.Operations;
using DXFER.Core.Sync;
using DXFER.Core.Sketching;
using FluentAssertions;
using Microsoft.JSInterop;
using Microsoft.AspNetCore.Components;

namespace DXFER.Core.Tests.Components;

public sealed class WorkbenchDocumentLifecycleTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ControlSplineTangentEditSurvivesWorkbenchUndoRedoAndDxfReopen(bool start)
    {
        var workbench = Create();
        var spline = new SplineEntity(EntityId.Create("spline"), 3,
            new DXFER.Core.Geometry.Point2[] { new(0, 0), new(1, 0), new(3, 1), new(4, 1) },
            new double[] { 0, 0, 0, 0, 1, 1, 1, 1 });
        var original = new DrawingDocument(new DrawingEntity[] { spline });
        Set(workbench, "_document", original);
        var from = start ? new DXFER.Core.Geometry.Point2(0.25, 0) : new(3.75, 1);
        SketchGeometryDragService.TryApplyDrag(original, $"spline|point|tangent-{(start ? "start" : "end")}|0|0",
            from, new(from.X, from.Y + 0.4), false, out var changed, out var status).Should().BeTrue();
        Invoke(workbench, "ApplyDocumentChange", changed, status);
        var changedDxf = DxfDocumentWriter.Write(changed);
        Invoke(workbench, "UndoLastDocumentChange");
        Get<DrawingDocument>(workbench, "_document").Should().BeSameAs(original);
        Invoke(workbench, "RedoLastDocumentChange");
        DxfDocumentWriter.Write(Get<DrawingDocument>(workbench, "_document")).Should().Be(changedDxf);
        var reopened = DxfDocumentReader.Read(changedDxf).Entities.OfType<SplineEntity>().Single();
        reopened.Degree.Should().Be(3);
        reopened.ControlPoints.Should().HaveCount(4);
        reopened.ControlPoints[start ? 1 : 2].Y.Should().BeApproximately(start ? 1.6 : 2.6, 1e-12);
        reopened.Knots.Should().Equal(spline.Knots);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void InvalidVisibleCornerSizeNeverAppliesPreviousValue(string text)
    {
        foreach (var command in new[] { WorkbenchCommandId.Fillet, WorkbenchCommandId.Chamfer })
        {
            var workbench = Create();
            var original = Get<DrawingDocument>(workbench, "_document");
            Invoke(workbench, "OnCornerModifyDistanceChanged", new ChangeEventArgs { Value = "0.5" });
            Invoke(workbench, "OnCornerModifyDistanceChanged", new ChangeEventArgs { Value = text });
            Invoke(workbench, "TryApplyCornerModifyTool", command, true).Should().Be(false);
            Get<DrawingDocument>(workbench, "_document").Should().BeSameAs(original);
            Get<string>(workbench, "_status").Should().Contain("Geometry unchanged");
            typeof(DrawingWorkbench).GetProperty("CornerModifyDistanceValue", Private)!.GetValue(workbench)
                .Should().Be(text);
            Invoke(workbench, "OnCornerModifyDistanceChanged", new ChangeEventArgs { Value = "0.0005" });
            Get<bool>(workbench, "_cornerModifyDistanceInvalid").Should().BeFalse();
            Invoke(workbench, "GetCornerModifyDistance").Should().Be(0.0005);
        }
    }

    [Theory]
    [InlineData(WorkbenchTool.Fillet)]
    [InlineData(WorkbenchTool.Chamfer)]
    [InlineData(WorkbenchTool.PowerTrim)]
    public void ModifyToolDoesNotHideOperationResultsBehindGenericPrompt(WorkbenchTool tool)
    {
        var workbench = Create();
        Set(workbench, "_activeTool", tool);
        foreach (var status in new[] { "Operation not applied. Geometry unchanged.", "Operation applied at 0.7." })
        {
            Set(workbench, "_status", status);
            typeof(DrawingWorkbench).GetProperty("CommandPromptText", Private)!.GetValue(workbench)
                .Should().Be(status);
        }
    }

    [Fact]
    public void FailedSolveShowsFailureAndPreservesIntentThroughUndoRedo()
    {
        var workbench = Create();
        var original = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("edge"), new(0, 0), new(10, 0))
        }, Array.Empty<SketchDimension>(), new[]
        {
            new SketchConstraint("horizontal", SketchConstraintKind.Horizontal, new[] { "edge" }, SketchConstraintState.Satisfied)
        });
        Set(workbench, "_document", original);
        var result = new LegacySketchSolverAdapter().Solve(new SketchSolveRequest(original,
            new[] { new SketchConstraint("vertical", SketchConstraintKind.Vertical, new[] { "edge" }) },
            Array.Empty<SketchDimension>()));

        Invoke(workbench, "ApplySketchSolveResult", result, "Successfully added constraint.");
        Get<string>(workbench, "_status").Should().Contain("could not be satisfied").And.Contain("Geometry unchanged");
        Set(workbench, "_activeTool", WorkbenchTool.Vertical);
        typeof(DrawingWorkbench).GetProperty("CommandPromptText", Private)!.GetValue(workbench)!
            .ToString().Should().Contain("could not be satisfied", "the modal tool prompt must not hide solve errors");
        Get<DrawingDocument>(workbench, "_document").Entities.Should().Equal(original.Entities);
        Invoke(workbench, "UndoLastDocumentChange");
        Get<DrawingDocument>(workbench, "_document").Should().BeSameAs(original);
        Get<string?>(workbench, "_solveFailureStatus").Should().BeNull();
        Invoke(workbench, "RedoLastDocumentChange");
        Get<DrawingDocument>(workbench, "_document").Should().BeSameAs(result.Document);
        result.Document.Constraints.Should().Contain(c => c.Id == "vertical" && c.State == SketchConstraintState.Unsatisfied);
        Get<string>(workbench, "_status").Should().Contain("unsatisfied");
        typeof(DrawingWorkbench).GetProperty("CommandPromptText", Private)!.GetValue(workbench)!
            .ToString().Should().Contain("unsatisfied");
        Invoke(workbench, "RedoLastDocumentChange");
        Get<string>(workbench, "_solveFailureStatus").Should().Contain("unsatisfied", "an empty redo stack must not dismiss a remaining conflict");
        Invoke(workbench, "ApplyDocumentChange", original, "Removed conflict.");
        Invoke(workbench, "UndoLastDocumentChange");
        Get<string>(workbench, "_solveFailureStatus").Should().Contain("unsatisfied", "undoing conflict removal restores its explanation too");
        Invoke(workbench, "RedoLastDocumentChange");
        Get<string?>(workbench, "_solveFailureStatus").Should().BeNull();
    }

    [Fact]
    public void HistoryRestoresDimensionOnlyFailureFeedback()
    {
        var workbench = Create();
        var original = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("edge"), new(0, 0), new(10, 0))
        });
        var failed = new DrawingDocument(original.Entities, new[]
        {
            new SketchDimension("length", SketchDimensionKind.LinearDistance, new[] { "edge" }, 20, isDriving: true)
        }, Array.Empty<SketchConstraint>());
        Set(workbench, "_document", original);
        Invoke(workbench, "ApplyDocumentChange", failed, "Failed dimension.");
        Invoke(workbench, "UndoLastDocumentChange");
        Get<string?>(workbench, "_solveFailureStatus").Should().BeNull();
        Invoke(workbench, "RedoLastDocumentChange");
        Get<string>(workbench, "_solveFailureStatus").Should().Contain("unsatisfied");
        Get<DrawingDocument>(workbench, "_document").Entities.Should().Equal(original.Entities);
    }

    [Theory]
    [InlineData("NewBlankDocument")]
    [InlineData("LoadSample")]
    public void StandaloneReplacementDropsPreviousSyncOwnership(string method)
    {
        var workbench = Create();
        Set(workbench, "_syncLaunchOptions", Launch());
        Invoke(workbench, method);
        Get<SyncEditLaunchOptions>(workbench, "_syncLaunchOptions").Should().Be(SyncEditLaunchOptions.Empty);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    public void OnlyStandaloneFullEditorDownloadIncludesNativeIntent(bool fullEditing, bool sync, bool expected)
    {
        var workbench = Create();
        typeof(DrawingWorkbench).GetProperty(nameof(DrawingWorkbench.EnableFullEditing))!.SetValue(workbench, fullEditing);
        if (sync) Set(workbench, "_syncLaunchOptions", Launch());
        ((DxfWriteOptions)Invoke(workbench, "CreateDownloadWriteOptions")!).IncludeNativeDocument.Should().Be(expected);
        ((DxfWriteOptions)Invoke(workbench, "CreateDxfWriteOptions")!).IncludeNativeDocument.Should().BeFalse();
    }

    [Fact]
    public void NativeReopenPreservesReferenceIdsGeometryAndFailedIntentWithoutCleaningOrSolving()
    {
        var workbench = Create();
        var original = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("edge"), new(5, 2), new(15, 2)),
            new LineEntity(EntityId.Create("duplicate"), new(5, 2), new(15, 2))
        }, new[]
        {
            new SketchDimension("length", SketchDimensionKind.LinearDistance, new[] { "edge" }, 20, isDriving: true)
        }, new[]
        {
            new SketchConstraint("vertical", SketchConstraintKind.Vertical, new[] { "edge" }, SketchConstraintState.Unsatisfied)
        });
        var text = DxfDocumentWriter.Write(original, new DxfWriteOptions(IncludeNativeDocument: true));
        Invoke(workbench, "LoadDxfText", "editable.dxf", text, false, true);
        var reopened = Get<DrawingDocument>(workbench, "_document");
        reopened.Entities.Should().BeEquivalentTo(original.Entities);
        reopened.Dimensions.Should().BeEquivalentTo(original.Dimensions);
        reopened.Constraints.Should().BeEquivalentTo(original.Constraints);
        reopened.Metadata.TrustedSource.Should().BeFalse();
        Get<string>(workbench, "_solveFailureStatus").Should().Contain("unsatisfied");
        Dirty(workbench).Should().BeFalse();
    }

    [Fact]
    public void EmptyNativeSnapshotCanReplaceAnExistingDocument()
    {
        var workbench = Create();
        Set(workbench, "_document", Document());
        var text = DxfDocumentWriter.Write(new DrawingDocument(Array.Empty<DrawingEntity>()),
            new DxfWriteOptions(IncludeNativeDocument: true));
        Invoke(workbench, "LoadDxfText", "blank.dxf", text, false, false);
        Get<DrawingDocument>(workbench, "_document").Entities.Should().BeEmpty();
        Dirty(workbench).Should().BeFalse();
    }

    [Fact]
    public async Task NativeSerializationFailureDoesNotDownloadOrMarkDocumentSaved()
    {
        var workbench = Create();
        typeof(DrawingWorkbench).GetProperty(nameof(DrawingWorkbench.EnableFullEditing))!.SetValue(workbench, true);
        var edge = new LineEntity(EntityId.Create("duplicate-id"), new(0, 0), new(10, 0));
        var invalid = new DrawingDocument(new DrawingEntity[] { edge, edge });
        Set(workbench, "_document", invalid);
        await (Task)Invoke(workbench, "DownloadDxfFilesAsync")!;
        Dirty(workbench).Should().BeTrue();
        Get<DrawingDocument>(workbench, "_document").Should().BeSameAs(invalid);
        Get<string>(workbench, "_status").Should().Contain("Could not download");
    }

    [Fact]
    public void RejectedImportPreservesDocumentNameGrainAndOwnership()
    {
        var workbench = Create();
        var original = Document();
        var launch = Launch();
        Set(workbench, "_document", original);
        Set(workbench, "_fileName", "original.dxf");
        Set(workbench, "_grainDirection", GrainDirection.GlobalX);
        Set(workbench, "_syncLaunchOptions", launch);
        Invoke(workbench, "LoadDxfText", "empty.dxf", "0\nSECTION\n2\nENTITIES\n0\nENDSEC\n0\nEOF\n", false, false);
        Get<DrawingDocument>(workbench, "_document").Should().BeSameAs(original);
        Get<string>(workbench, "_fileName").Should().Be("original.dxf");
        Get<GrainDirection>(workbench, "_grainDirection").Should().Be(GrainDirection.GlobalX);
        Get<SyncEditLaunchOptions>(workbench, "_syncLaunchOptions").Should().BeSameAs(launch);
        typeof(DrawingWorkbench).GetProperty("CommandPromptText", Private)!.GetValue(workbench)!
            .ToString().Should().Contain("No supported DXF entities");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OnlyTrustedSyncLoadKeepsItsCallback(bool trusted)
    {
        var workbench = Create();
        var launch = Launch();
        Set(workbench, "_syncLaunchOptions", launch);
        Invoke(workbench, "LoadDxfText", "part.dxf", DxfDocumentWriter.Write(Document()), trusted, false);
        Get<SyncEditLaunchOptions>(workbench, "_syncLaunchOptions").Should().Be(trusted ? launch : SyncEditLaunchOptions.Empty);
    }

    [Fact]
    public void SavedIdentityFollowsUndoRedoAndGrainChanges()
    {
        var workbench = Create();
        Dirty(workbench).Should().BeFalse();
        var original = Get<DrawingDocument>(workbench, "_document");
        Invoke(workbench, "ApplyDocumentChange", Document(), "edit");
        Dirty(workbench).Should().BeTrue();
        Invoke(workbench, "UndoLastDocumentChange");
        Dirty(workbench).Should().BeFalse();
        Invoke(workbench, "RedoLastDocumentChange");
        Dirty(workbench).Should().BeTrue();
        var saved = Get<DrawingDocument>(workbench, "_document");
        Invoke(workbench, "MarkDocumentSaved", saved, GrainDirection.None, null!);
        Dirty(workbench).Should().BeFalse();
        Invoke(workbench, "SetGrainDirection", GrainDirection.GlobalX);
        Dirty(workbench).Should().BeTrue();
        Invoke(workbench, "SetGrainDirection", GrainDirection.None);
        Dirty(workbench).Should().BeFalse();
        Invoke(workbench, "UndoLastDocumentChange");
        Get<DrawingDocument>(workbench, "_document").Should().BeSameAs(original);
        Dirty(workbench).Should().BeTrue("undo away from the saved state is unsaved work");
        Invoke(workbench, "NewBlankDocument");
        Dirty(workbench).Should().BeFalse();
        Invoke(workbench, "LoadSample");
        Dirty(workbench).Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DownloadDoesNotClaimSyncWasSaved(bool connected)
    {
        var workbench = Create();
        Set(workbench, "_document", Document());
        if (connected) Set(workbench, "_syncLaunchOptions", Launch());
        Set(workbench, "_downloadModule", new FakeModule());
        await (Task)Invoke(workbench, "DownloadDxfFilesAsync")!;
        Dirty(workbench).Should().Be(connected);
    }

    [Fact]
    public async Task FailedDownloadDoesNotClearDirtyState()
    {
        var workbench = Create();
        Set(workbench, "_document", Document());
        Set(workbench, "_downloadModule", new FakeModule { Action = () => throw new JSException("download failed") });
        var save = () => (Task)Invoke(workbench, "DownloadDxfFilesAsync")!;
        await save.Should().ThrowAsync<JSException>();
        Dirty(workbench).Should().BeTrue();
    }

    [Fact]
    public async Task EditsDuringDownloadRemainUnsaved()
    {
        var workbench = Create();
        Set(workbench, "_document", Document());
        Set(workbench, "_downloadModule", new FakeModule { Action = () =>
            Invoke(workbench, "ApplyDocumentChange", Document(), "another edit") });
        await (Task)Invoke(workbench, "DownloadDxfFilesAsync")!;
        Dirty(workbench).Should().BeTrue();
        Invoke(workbench, "UndoLastDocumentChange");
        Dirty(workbench).Should().BeFalse();
    }

    [Fact]
    public async Task CancelReplacementRetainsOwnershipAndSaveInFlightRefusesReplacement()
    {
        var workbench = Create();
        var launch = Launch();
        Set(workbench, "_syncLaunchOptions", launch);
        var js = new FakeRuntime();
        typeof(DrawingWorkbench).GetProperty("JsRuntime", Private)!.SetValue(workbench, js);
        (await (Task<bool>)Invoke(workbench, "ConfirmDocumentReplacementAsync")!).Should().BeFalse();
        js.Calls.Should().Be(1);
        Get<SyncEditLaunchOptions>(workbench, "_syncLaunchOptions").Should().BeSameAs(launch);
        Set(workbench, "_isSyncSaveInFlight", true);
        (await (Task<bool>)Invoke(workbench, "ConfirmDocumentReplacementAsync")!).Should().BeFalse();
        js.Calls.Should().Be(1, "do not open another confirmation during a Sync save");
    }

    private static bool Dirty(DrawingWorkbench target) =>
        (bool)typeof(DrawingWorkbench).GetProperty("HasUnsavedChanges", Private)!.GetValue(target)!;

    private sealed class FakeRuntime : IJSRuntime
    {
        public int Calls { get; private set; }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, default, args);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            Calls++;
            identifier.Should().Be("confirm");
            return ValueTask.FromResult((TValue)(object)false);
        }
    }

    private sealed class FakeModule : IJSObjectReference
    {
        public Action? Action { get; init; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, default, args);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            Action?.Invoke();
            return ValueTask.FromResult(default(TValue)!);
        }
    }

    private static DrawingDocument Document() => new([
        new CircleEntity(EntityId.Create("circle"), new(0, 0), 1)]);

    private static SyncEditLaunchOptions Launch() => SyncLaunchOptionsParser.ParseQueryString(
        "?syncBaseUrl=https://example.invalid&artifactId=one&jobId=job&editToken=token");

    private static DrawingWorkbench Create()
    {
        var workbench = new DrawingWorkbench();
        typeof(DrawingWorkbench).GetProperty("MenuCommandService", Private)!.SetValue(workbench, new WorkbenchMenuCommandService());
        return workbench;
    }

    private static void Set(DrawingWorkbench target, string name, object value) =>
        typeof(DrawingWorkbench).GetField(name, Private)!.SetValue(target, value);

    private static T Get<T>(DrawingWorkbench target, string name) =>
        (T)typeof(DrawingWorkbench).GetField(name, Private)!.GetValue(target)!;

    private static object? Invoke(DrawingWorkbench target, string name, params object[] args) =>
        typeof(DrawingWorkbench).GetMethod(name, Private)!.Invoke(target, args);
}

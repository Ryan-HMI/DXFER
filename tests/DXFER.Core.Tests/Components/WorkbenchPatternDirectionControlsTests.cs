using System.Reflection;
using DXFER.Blazor.Components;
using DXFER.Blazor.Interop;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Operations;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DXFER.Core.Tests.Components;

public sealed class WorkbenchPatternDirectionControlsTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [Theory]
    [InlineData("U", "u-line")]
    [InlineData("V", "poly|segment|1")]
    public async Task DedicatedPickAcceptsCurrentPatternAndArmedAxisWithoutSelectionOrHistoryChanges(string axis, string key)
    {
        await using var f = new Fixture();
        await f.Run(() =>
        {
            f.Invoke("BeginPatternDirectionPick", axis);
            f.Invoke("PickPatternDirection", "pattern", axis, key);
            (axis == "U" ? f.Draft.UDirectionReference : f.Draft.VDirectionReference).Should().Be(key);
            var pattern = f.Preview!.Metadata.Patterns.Single();
            (axis == "U" ? pattern.UDirectionReference : pattern.VDirectionReference).Should().Be(key);
            f.Get<string?>("_patternDirectionPicking").Should().BeNull();
            f.Get<bool>("_patternDirectionSelectionConsumed").Should().BeFalse();
            f.SourceSelectionUnchanged();
            f.Unchanged();
        });
    }

    [Theory]
    [InlineData("stale-pattern", "U")]
    [InlineData("pattern", "V")]
    [InlineData("pattern", "invalid")]
    public async Task DedicatedPickIgnoresStalePatternOrWrongAxis(string patternId, string axis)
    {
        await using var f = new Fixture();
        await f.Run(() =>
        {
            f.Invoke("BeginPatternDirectionPick", "U");
            var preview = f.Preview;
            f.Invoke("PickPatternDirection", patternId, axis, "u-line");
            f.Draft.UDirectionReference.Should().BeNull();
            f.Draft.VDirectionReference.Should().BeNull();
            f.Preview.Should().BeSameAs(preview);
            f.Get<string>("_patternDirectionPicking").Should().Be("U");
            f.SourceSelectionUnchanged();
            f.Unchanged();
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DedicatedPickAfterPickerOrEditorCancellationIsIgnored(bool closeEditor)
    {
        await using var f = new Fixture();
        await f.Run(() =>
        {
            f.Invoke("BeginPatternDirectionPick", "U");
            f.Invoke("OnToolCanceled");
            if (closeEditor) f.Invoke("ClearPatternEditor");
            var draft = f.Get<PatternDraft?>("_patternDraft");
            var preview = f.Preview;
            f.Invoke("PickPatternDirection", "pattern", "U", "u-line");
            f.Get<PatternDraft?>("_patternDraft").Should().BeSameAs(draft);
            if (draft is not null) draft.UDirectionReference.Should().BeNull();
            f.Preview.Should().BeSameAs(preview);
            f.Get<string?>("_patternDirectionPicking").Should().BeNull();
            f.SourceSelectionUnchanged();
            f.Unchanged();
        });
    }

    [Theory]
    [InlineData("U", "u-line")]
    [InlineData("V", "poly|segment|1")]
    public async Task PickPersistsExactReferenceInDraftWithoutChangingSourceSelection(string axis, string key)
    {
        await using var f = new Fixture();
        await f.Run(() =>
        {
            f.Invoke("BeginPatternDirectionPick", axis);
            f.Property("CanConfirmPatternPreview").Should().Be(false);
            f.Invoke("CommitPatternPreview");
            f.Unchanged();
            f.Pick(key);
            (axis == "U" ? f.Draft.UDirectionReference : f.Draft.VDirectionReference).Should().Be(key);
            var pattern = f.Preview!.Metadata.Patterns.Single();
            (axis == "U" ? pattern.UDirectionReference : pattern.VDirectionReference).Should().Be(key);
            pattern.SourceEntityIds.Should().Equal("seed");
            f.SourceSelectionUnchanged();
            f.Unchanged();
            f.Invoke("CommitPatternPreview");
            var committed = f.Document.Metadata.Patterns.Single();
            (axis == "U" ? committed.UDirectionReference : committed.VDirectionReference).Should().Be(key);
            f.Undo.Should().ContainSingle();
            f.Invoke("UndoLastDocumentChange");
            f.Document.Should().BeSameAs(f.Original);
            f.Invoke("RedoLastDocumentChange");
            f.Document.Metadata.Patterns.Single().Should().Be(committed);
        });
    }

    [Theory]
    [InlineData("seed")]
    [InlineData("poly")]
    [InlineData("missing")]
    [InlineData("zero")]
    [InlineData("member")]
    public async Task InvalidGeometryKeepsPickerArmedAndShowsCoreReason_ValidRetryRecovers(string key)
    {
        await using var f = new Fixture();
        await f.Run(() =>
        {
            if (key == "member") key = f.Original.Metadata.Patterns.Single().Members.First().EntityId;
            var candidate = f.Original.Metadata.Patterns.Single() with { UDirectionReference = key };
            DrawingPatternService.TryGetDirections(f.Original, candidate, out _, out _, out var reason).Should().BeFalse();
            f.Invoke("BeginPatternDirectionPick", "U");
            f.Pick(key);
            f.Get<string>("_patternError").Should().Be(reason);
            f.Get<string>("_patternDirectionPicking").Should().Be("U");
            f.Draft.UDirectionReference.Should().BeNull();
            f.Preview.Should().BeNull();
            f.Pick("u-line");
            f.Get<string?>("_patternError").Should().BeNull();
            f.Get<string?>("_patternDirectionPicking").Should().BeNull();
            f.Preview.Should().NotBeNull();
            f.SourceSelectionUnchanged();
            f.Unchanged();
        });
    }

    [Fact]
    public async Task PointAndMultiplePicksAreRefused_EscapeCancelsPickerNotDraft()
    {
        await using var f = new Fixture();
        await f.Run(() =>
        {
            f.Draft.PitchX = "4.2500";
            f.Invoke("RefreshPatternPreview");
            f.Invoke("BeginPatternDirectionPick", "U");
            f.Pick("u-line:start");
            f.Get<string>("_patternError").Should().Be("Select one line or straight polyline edge for pattern U direction.");
            f.Invoke("OnSelectionChanged", new HashSet<string> { "u-line", "poly|segment|1" });
            f.Draft.UDirectionReference.Should().BeNull();
            f.Invoke("OnToolCanceled");
            f.Pick("u-line");
            f.Draft.PitchX.Should().Be("4.2500");
            f.Draft.UDirectionReference.Should().BeNull("a delayed pick after Escape must not set a direction");
            f.Get<string?>("_patternDirectionPicking").Should().BeNull();
            f.Preview.Should().NotBeNull();
            f.SourceSelectionUnchanged();
            f.Unchanged();
        });
    }

    [Fact]
    public async Task DirectionValidationUsesDraftCountsAndExplicitVIsIndependent()
    {
        await using var f = new Fixture();
        await f.Run(() =>
        {
            f.Draft.Rows = "1";
            f.Invoke("RefreshPatternPreview");
            f.Invoke("BeginPatternDirectionPick", "V");
            f.Pick("horizontal");
            f.Draft.VDirectionReference.Should().Be("horizontal", "one staged row allows collinear directions");
            f.Preview.Should().NotBeNull();
            f.Draft.Rows = "2";
            f.Invoke("RefreshPatternPreview");
            f.Get<string>("_patternError").Should().Contain("collinear");
            f.Invoke("BeginPatternDirectionPick", "U");
            f.Pick("u-line");
            f.Preview.Should().NotBeNull();
            var pattern = f.Preview!.Metadata.Patterns.Single();
            DrawingPatternService.TryGetDirections(f.Preview, pattern, out var u, out var v, out var reason).Should().BeTrue(reason);
            u.Should().Be(new Point2(0, 1));
            v.Should().Be(new Point2(1, 0));
            f.Unchanged();
        });
    }

    [Theory]
    [InlineData("U")]
    [InlineData("V")]
    public async Task FlipAndClearOnlyChangeTheirAxisAndPreservePendingText(string axis)
    {
        await using var f = new Fixture();
        await f.Run(() =>
        {
            f.Draft.Count = "9.000";
            f.Draft.StaggerFraction = "0.2500";
            f.Invoke("BeginPatternDirectionPick", axis);
            f.Pick(axis == "U" ? "u-line" : "poly|segment|1");
            f.Invoke("FlipPatternAxis", axis);
            f.Preview.Should().NotBeNull();
            (axis == "U" ? f.Draft.PitchX : f.Draft.PitchY).Should().Be("-3");
            (axis == "U" ? f.Draft.PitchY : f.Draft.PitchX).Should().Be("3");
            f.Invoke("ClearPatternDirection", axis);
            (axis == "U" ? f.Draft.UDirectionReference : f.Draft.VDirectionReference).Should().BeNull();
            f.Draft.Count.Should().Be("9.000");
            f.Draft.StaggerFraction.Should().Be("0.2500");
            f.SourceSelectionUnchanged();
            f.Unchanged();
        });
    }

    [Theory]
    [InlineData("-0.1")]
    [InlineData("1.1")]
    [InlineData("NaN")]
    public async Task InvalidStaggerCannotCommitAndTogglePreservesValidFraction(string invalid)
    {
        await using var f = new Fixture();
        await f.Run(() =>
        {
            f.Draft.Staggered = true;
            f.Draft.StaggerFraction = invalid;
            f.Invoke("RefreshPatternPreview");
            f.Get<string>("_patternError").Should().Be("Enter a stagger fraction between 0 and 1.");
            f.Invoke("CommitPatternPreview");
            f.Unchanged();
            f.Draft.StaggerFraction = "0.2500";
            f.Invoke("RefreshPatternPreview");
            var pattern = f.Preview!.Metadata.Patterns.Single();
            pattern.Kind.Should().Be(PatternKind.Staggered);
            pattern.StaggerFraction.Should().Be(0.25);
            var shifted = pattern.Members.Single(m => m.Row == 1 && m.Column == 0);
            ((CircleEntity)f.Preview.Entities.Single(e => e.Id.Value == shifted.EntityId)).Center.Should().Be(new Point2(10.75, 13));
            f.Draft.Staggered = false;
            f.Invoke("RefreshPatternPreview");
            f.Preview!.Metadata.Patterns.Single().Kind.Should().Be(PatternKind.Rectangular);
            f.Draft.StaggerFraction.Should().Be("0.2500");
            f.Draft.Staggered = true;
            f.Invoke("RefreshPatternPreview");
            f.Preview!.Metadata.Patterns.Single().StaggerFraction.Should().Be(0.25);
            f.Unchanged();
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StaleOrProtectedDraftCannotAcceptDirectionOrFlip(bool protect)
    {
        await using var f = new Fixture();
        await f.Run(() =>
        {
            f.Invoke("BeginPatternDirectionPick", "U");
            var replacement = DrawingLayerService.WithMetadata(f.Original, f.Original.Metadata with
            {
                Mode = protect ? DrawingDocumentMode.ReferenceOnly : DrawingDocumentMode.Editable
            });
            f.Set("_document", replacement);
            f.Pick("u-line");
            f.Invoke("FlipPatternAxis", "U");
            f.Invoke("CommitPatternPreview");
            f.Draft.UDirectionReference.Should().BeNull();
            f.Draft.PitchX.Should().Be("3");
            f.Get<string>("_patternError").Should().Contain("Drawing changed or pattern geometry is protected");
            f.Document.Should().BeSameAs(replacement);
            f.SourceSelectionUnchanged();
            f.Undo.Should().BeEmpty();
        });
    }

    [Fact]
    public void BothEditorsKeepNumericTextBrowserOwnedUntilChange()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src/DXFER.Blazor/Components"))) directory = directory.Parent;
        directory.Should().NotBeNull();
        foreach (var file in new[] { "PatternEditor.razor", "PatternViewportControls.razor" })
        {
            var source = File.ReadAllText(Path.Combine(directory!.FullName, "src/DXFER.Blazor/Components", file));
            source.Should().NotContain("oninput");
            source.Should().Contain("min=\"0\" max=\"1\" step=\"any\"");
        }
    }

    [Theory]
    [InlineData("PatternEditor")]
    [InlineData("PatternViewportControls")]
    public void WorkbenchBindsPickerStateAsRazorExpression_NotStringLiteral(string component)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        const string path = "src/DXFER.Blazor/Components/DrawingWorkbench.razor";
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, path))) directory = directory.Parent;
        directory.Should().NotBeNull();
        var source = File.ReadAllText(Path.Combine(directory!.FullName, path));
        var start = source.IndexOf($"<{component} ", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        var end = source.IndexOf("/>", start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start);
        var invocation = source[start..end];
        invocation.Should().Contain("PickingDirection=\"@_patternDirectionPicking\"",
            "string component parameters require an explicit Razor expression to receive live U/V picker state");
        invocation.Should().NotContain("PickingDirection=\"_patternDirectionPicking\"");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EnterNeverConfirmsBeforeNumericChangeCallback(bool viewport)
    {
        object editor = viewport ? new PatternViewportControls() : new PatternEditor();
        var type = editor.GetType();
        var draft = new PatternDraft { PitchX = "3", PitchY = "4.2500" };
        var confirmations = 0;
        var changes = 0;
        type.GetProperty("Draft")!.SetValue(editor, draft);
        type.GetProperty("CanConfirm")!.SetValue(editor, true);
        type.GetProperty("Confirm")!.SetValue(editor, EventCallback.Factory.Create(new object(), () => confirmations++));
        type.GetProperty("Changed")!.SetValue(editor, EventCallback.Factory.Create(new object(), () => changes++));

        await (Task)type.GetMethod(viewport ? "KeyDown" : "OnKeyDown", Private)!
            .Invoke(editor, [new KeyboardEventArgs { Key = "Enter" }])!;
        confirmations.Should().Be(0, "Enter must not commit the old draft before the browser change event");
        changes.Should().Be(0, "keyboard handling must not re-render partially typed numeric text");

        draft.PitchX = "3.7500";
        await (Task)type.GetMethod("NotifyChanged", Private)!.Invoke(editor, [])!;
        changes.Should().Be(1);
        confirmations.Should().Be(0);
        draft.PitchX.Should().Be("3.7500");
        draft.PitchY.Should().Be("4.2500");
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly ServiceProvider _services = new ServiceCollection().BuildServiceProvider();
        private readonly TestRenderer _renderer;
        private readonly HeadlessWorkbench _workbench = new();
        public DrawingDocument Original { get; }
        public DrawingDocument Document => Get<DrawingDocument>("_document");
        public DrawingDocument? Preview => (DrawingDocument?)Property("PatternPreview");
        public PatternDraft Draft => Get<PatternDraft>("_patternDraft");
        public Stack<DrawingDocument> Undo => Get<Stack<DrawingDocument>>("_undoStack");
        public Fixture()
        {
            var source = new DrawingDocument([
                new CircleEntity(EntityId.Create("seed"), new(10, 10), 1),
                new LineEntity(EntityId.Create("u-line"), new(0, 0), new(0, 5)),
                new LineEntity(EntityId.Create("horizontal"), new(0, 0), new(5, 0)),
                new LineEntity(EntityId.Create("zero"), new(0, 0), new(0, 0)),
                new PolylineEntity(EntityId.Create("poly"), [new(0, 0), new(4, 0), new(6, 3)])
            ]);
            var pattern = new PatternDefinition { Id = "pattern", SourceEntityIds = ["seed"], Columns = 3, Rows = 2, XPitch = 3, YPitch = 3 };
            DrawingPatternService.TryCreate(source, pattern, out var document, out var reason).Should().BeTrue(reason);
            Original = document;
            Set("_document", document);
            Get<HashSet<string>>("_selectedEntityIds").Add("seed");
            Set("_activeSelectionKey", "seed");
            typeof(DrawingWorkbench).GetProperty("MenuCommandService", Private)!.SetValue(_workbench, new WorkbenchMenuCommandService());
            typeof(DrawingWorkbench).GetProperty("ToolHotkeys", Private)!.SetValue(_workbench, new ToolHotkeyService());
            _renderer = new TestRenderer(_services);
            _renderer.Attach(_workbench);
            Invoke("EditViewportPattern", document.Metadata.Patterns.Single());
        }
        public Task Run(Action action) => _renderer.Dispatcher.InvokeAsync(action);
        public void Pick(string key)
        {
            Invoke("OnSelectionChanged", new HashSet<string> { key });
            Invoke("OnActiveSelectionChanged", key);
        }
        public void Invoke(string name, params object[] args) => typeof(DrawingWorkbench).GetMethod(name, Private)!.Invoke(_workbench, args);
        public object? Property(string name) => typeof(DrawingWorkbench).GetProperty(name, Private)!.GetValue(_workbench);
        public T Get<T>(string name) => (T)typeof(DrawingWorkbench).GetField(name, Private)!.GetValue(_workbench)!;
        public void Set(string name, object value) => typeof(DrawingWorkbench).GetField(name, Private)!.SetValue(_workbench, value);
        public void SourceSelectionUnchanged()
        {
            Get<HashSet<string>>("_selectedEntityIds").Should().Equal("seed");
            Get<string>("_activeSelectionKey").Should().Be("seed");
        }
        public void Unchanged()
        {
            Document.Should().BeSameAs(Original);
            Undo.Should().BeEmpty();
            Get<Stack<DrawingDocument>>("_redoStack").Should().BeEmpty();
        }
        public async ValueTask DisposeAsync()
        {
#pragma warning disable BL0006 // Dispose the attached test renderer.
            await _renderer.DisposeAsync();
#pragma warning restore BL0006
            await _services.DisposeAsync();
        }
    }
    private sealed class HeadlessWorkbench : DrawingWorkbench
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder) { }
        protected override Task OnAfterRenderAsync(bool firstRender) => Task.CompletedTask;
    }
#pragma warning disable BL0006 // Exercise actual handlers without unrelated browser modules.
    private sealed class TestRenderer(IServiceProvider services) : Renderer(services, NullLoggerFactory.Instance)
    {
        public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();
        public int Attach(IComponent component) => AssignRootComponentId(component);
        protected override Task UpdateDisplayAsync(in RenderBatch batch) => Task.CompletedTask;
        protected override void HandleException(Exception exception) => throw exception;
    }
#pragma warning restore BL0006
}

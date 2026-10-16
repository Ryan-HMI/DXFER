using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using DXFER.Blazor.Components;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

namespace DXFER.Core.Tests.Components;

public sealed class WorkbenchPaletteLifecycleTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [Fact]
    public async Task ResizeUsesDisplayScaleAndPreservesPeersThenCancelRestoresPreferredWidth()
    {
        await using var fixture = new Fixture();
        await fixture.Render("Sketch", "View");
        await fixture.AfterRender(true);
        var group = new WorkbenchToolGroup("Sketch", []);
        var peer = fixture.State("View").ToJsonString();
        fixture.Module.ResizeScale = 2;
        await fixture.Invoke("BeginResize", group, new PointerEventArgs { PointerId = 7, ClientX = 300 });
        await fixture.Invoke("OnPalettePointerMove", new PointerEventArgs { PointerId = 8, ClientX = 500 });
        fixture.State("Sketch")["labelWidth"]!.GetValue<double>().Should().Be(232);
        await fixture.Invoke("OnPalettePointerMove", new PointerEventArgs { PointerId = 7, ClientX = 500 });
        fixture.State("Sketch")["labelWidth"]!.GetValue<double>().Should().Be(332);
        fixture.Module.Saves.Should().BeEmpty();
        await fixture.Invoke("OnPalettePointerUp", new PointerEventArgs { PointerId = 7, Type = "pointercancel" });
        fixture.State("Sketch")["labelWidth"]!.GetValue<double>().Should().Be(232);
        fixture.State("View").ToJsonString().Should().Be(peer);
        fixture.Module.Saves.Should().ContainSingle();
    }

    [Fact]
    public async Task WidthKeyboardBoundsAndLabelTogglesPreserveIndependentPreference()
    {
        await using var fixture = new Fixture();
        await fixture.Render("Sketch");
        await fixture.AfterRender(true);
        var group = new WorkbenchToolGroup("Sketch", []);
        await fixture.Invoke("ResizeByKey", group, new KeyboardEventArgs { Key = "End" });
        await fixture.Invoke("ResizeByKey", group, new KeyboardEventArgs { Key = "ArrowRight", ShiftKey = true });
        fixture.State("Sketch")["labelWidth"]!.GetValue<double>().Should().Be(480);
        await fixture.Invoke("ToggleLabels", group);
        await fixture.Invoke("ToggleLabels", group);
        fixture.State("Sketch")["labelWidth"]!.GetValue<double>().Should().Be(480);
        await fixture.Invoke("ResizeByKey", group, new KeyboardEventArgs { Key = "Home" });
        fixture.State("Sketch")["labelWidth"]!.GetValue<double>().Should().Be(232);
        for (var i = 0; i < 4; i++)
            await fixture.Invoke("ResizeByKey", group, new KeyboardEventArgs { Key = "ArrowLeft", ShiftKey = true });
        fixture.State("Sketch")["labelWidth"]!.GetValue<double>().Should().Be(176);
    }

    [Fact]
    public async Task CleanupAndGrainDefaultRightWhileOtherGroupsStayLeft()
    {
        await using var fixture = new Fixture();
        await fixture.Render("View", "Sketch", "Modify", "Transform", "Pattern", "Constraints", "Cleanup", "Grain");
        fixture.State("Cleanup")["side"]!.GetValue<int>().Should().Be(2);
        fixture.State("Grain")["side"]!.GetValue<int>().Should().Be(2);
        fixture.State("Sketch")["side"]!.GetValue<int>().Should().Be(1);
        fixture.State("View")["side"]!.GetValue<int>().Should().Be(1);
    }

    [Fact]
    public async Task EmptyFirstRenderAndLaterGroupsRestoreEachIdentityOnlyOnce()
    {
        await using var fixture = new Fixture();
        await fixture.Render();
        await fixture.AfterRender(true);
        fixture.Module.Loads.Should().BeEmpty();
        await fixture.Render("Sketch");
        await fixture.AfterRender(false);
        fixture.State("Sketch")["side"]!.GetValue<int>().Should().Be(0);
        await fixture.Render("Sketch", "View");
        await fixture.AfterRender(false);
        fixture.State("View")["floatingX"]!.GetValue<double>().Should().Be(700);
        await fixture.Render("Sketch", "View");
        await fixture.AfterRender(false);
        fixture.Module.Loads.SelectMany(names => names).Should().Equal("Sketch", "View");
        fixture.Module.Saves.Should().BeEmpty("loading preferences is not an edit");
        fixture.Runtime.Imports.Should().Be(1);
    }

    [Fact]
    public async Task DelayedRestorePreservesEarlyEditsAndLoadsGroupsAddedWhileAwaitingInterop()
    {
        await using var fixture = new Fixture();
        fixture.Module.Delay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await fixture.Render("Sketch");
        var loading = fixture.AfterRender(true);
        await fixture.Module.LoadStarted.Task;
        await fixture.Invoke("ToggleSideCompaction", new WorkbenchToolGroup("Sketch", []));
        await fixture.Render("Sketch", "View");
        await fixture.AfterRender(false);
        fixture.Module.Saves.Should().BeEmpty("do not overwrite un-restored groups with defaults");
        fixture.Module.Delay.SetResult();
        await loading;
        fixture.State("Sketch")["side"]!.GetValue<int>().Should().Be(1);
        fixture.State("Sketch")["compaction"]!.GetValue<int>().Should().Be(1);
        fixture.State("View")["side"]!.GetValue<int>().Should().Be(0);
        fixture.Module.Loads.SelectMany(names => names).Should().Equal("Sketch", "View");
        fixture.Module.Saves.Should().ContainSingle();
        fixture.Module.Saves[0].AsArray().Should().HaveCount(2);
    }

    [Fact]
    public async Task GroupsArrivingDuringBoundsRefreshScheduleAnotherRenderAndRestore()
    {
        await using var fixture = new Fixture();
        fixture.Module.Observer.Delay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await fixture.Render();
        var refreshing = fixture.AfterRender(true);
        await fixture.Module.Observer.RefreshStarted.Task;
        await fixture.Render("Sketch");
        await fixture.AfterRender(false);
        var renders = fixture.Palette.RenderNotifications;
        fixture.Module.Observer.Delay.SetResult();
        await refreshing;
        fixture.Palette.RenderNotifications.Should().BeGreaterThan(renders);
        await fixture.AfterRender(false);
        fixture.State("Sketch")["side"]!.GetValue<int>().Should().Be(0);
    }

    [Fact]
    public async Task CompletedDragPersistsAnArrayNotSpreadArgumentsAndSurvivesParameterRefresh()
    {
        await using var fixture = new Fixture();
        await fixture.Render("Sketch", "View");
        await fixture.AfterRender(true);
        var view = fixture.State("View").ToJsonString();
        var group = new WorkbenchToolGroup("Sketch", []);
        await fixture.Invoke("BeginGroupDrag", group, new PointerEventArgs { ClientX = 400, ClientY = 300 });
        await fixture.Invoke("OnPalettePointerMove", new PointerEventArgs { ClientX = 500, ClientY = 350 });
        fixture.Module.Saves.Should().BeEmpty();
        await fixture.Invoke("OnPalettePointerUp", new PointerEventArgs { ClientX = 500, ClientY = 350 });
        fixture.Module.Saves.Should().ContainSingle();
        fixture.Module.Saves[0].AsArray().Should().HaveCount(2);
        fixture.State("Sketch")["floatingX"]!.GetValue<double>().Should().Be(484);
        fixture.State("View").ToJsonString().Should().Be(view);
        await fixture.Render("Sketch", "View");
        await fixture.AfterRender(false);
        fixture.Module.Loads.Should().ContainSingle();
        fixture.State("Sketch")["floatingX"]!.GetValue<double>().Should().Be(484);
    }

    [Fact]
    public async Task RightDockUsesReducedCanvasBoundsWithInspectorOpen()
    {
        await using var fixture = new Fixture();
        await fixture.Render("Sketch", "View");
        await fixture.AfterRender(true);
        fixture.Module.BoundsWidth = 984;
        await fixture.Invoke("BeginGroupDrag", new WorkbenchToolGroup("Sketch", []), new PointerEventArgs { ClientX = 400, ClientY = 300 });
        await fixture.Invoke("OnPalettePointerUp", new PointerEventArgs { ClientX = 930, ClientY = 300 });
        fixture.State("Sketch")["side"]!.GetValue<int>().Should().Be(2);
        fixture.State("View")["side"]!.GetValue<int>().Should().Be(0);
    }

    [Fact]
    public async Task LabelsPersistIndependentlyWithoutChangingDockingOrPeers()
    {
        await using var fixture = new Fixture();
        await fixture.Render("Sketch", "View");
        await fixture.AfterRender(true);
        var before = fixture.State("Sketch").AsObject();
        var view = fixture.State("View").ToJsonString();
        fixture.State("Sketch")["showLabels"]!.GetValue<bool>().Should().BeFalse();
        await fixture.Invoke("ToggleLabels", new WorkbenchToolGroup("Sketch", []));
        fixture.State("Sketch")["showLabels"]!.GetValue<bool>().Should().BeTrue();
        fixture.State("Sketch")["side"]!.GetValue<int>().Should().Be(before["side"]!.GetValue<int>());
        fixture.State("Sketch")["floatingX"]!.GetValue<double>().Should().Be(before["floatingX"]!.GetValue<double>());
        fixture.State("View").ToJsonString().Should().Be(view);
        fixture.Module.Saves.Should().ContainSingle();
        await fixture.Render("Sketch", "View");
        await fixture.AfterRender(false);
        fixture.State("Sketch")["showLabels"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public async Task DisposalDuringLoadDoesNotApplyResultsOrPersistAndReleasesInterop()
    {
        await using var fixture = new Fixture();
        fixture.Module.Delay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await fixture.Render("Sketch");
        var loading = fixture.AfterRender(true);
        await fixture.Module.LoadStarted.Task;
        await fixture.Renderer.Dispatcher.InvokeAsync(async () => await fixture.Palette.DisposeAsync());
        fixture.Module.Delay.SetResult();
        await loading;
        fixture.State("Sketch")["side"]!.GetValue<int>().Should().Be(1);
        fixture.Module.Saves.Should().BeEmpty();
        fixture.Module.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task DisconnectedCircuitDuringDisposalDoesNotThrow()
    {
        await using var fixture = new Fixture();
        await fixture.Render("Sketch");
        await fixture.AfterRender(true);
        fixture.Module.Observer.DisconnectOnDispose = true;
        await fixture.Renderer.Dispatcher.InvokeAsync(async () => await fixture.Palette.DisposeAsync());
    }

    [Fact]
    public async Task LoadFailureKeepsControlsUsableAndDisposalReleasesObserver()
    {
        await using var fixture = new Fixture();
        fixture.Module.FailLoad = true;
        await fixture.Render("Sketch");
        await fixture.AfterRender(true);
        fixture.State("Sketch")["side"]!.GetValue<int>().Should().Be(1);
        await fixture.Invoke("ToggleSideCompaction", new WorkbenchToolGroup("Sketch", []));
        fixture.State("Sketch")["compaction"]!.GetValue<int>().Should().Be(1);
        await fixture.Renderer.Dispatcher.InvokeAsync(async () => await fixture.Palette.DisposeAsync());
        fixture.Module.Observer.Disconnected.Should().BeTrue();
        fixture.Module.Observer.Disposed.Should().BeTrue();
    }

    // A real renderer attaches the component and runs parameter/render lifecycle methods.
    // After-render is invoked explicitly so delayed browser interop can be tested deterministically.
    private sealed class TestPalette : WorkbenchToolPalette
    {
        public TestPalette() { }
        public int RenderNotifications { get; private set; }
        protected override Task OnAfterRenderAsync(bool firstRender)
        {
            RenderNotifications++;
            return Task.CompletedTask;
        }
        public Task AfterRender(bool firstRender) => base.OnAfterRenderAsync(firstRender);
    }

#pragma warning disable BL0006 // The test renderer deliberately exercises Blazor lifecycle internals.
    private sealed class TestRenderer(IServiceProvider services) : Renderer(services, NullLoggerFactory.Instance)
    {
        public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();
        public TestPalette Create() => (TestPalette)InstantiateComponent(typeof(TestPalette));
        public int Attach(IComponent component) => AssignRootComponentId(component);
        public Task Render(int id, ParameterView parameters) => RenderRootComponentAsync(id, parameters);
        protected override Task UpdateDisplayAsync(in RenderBatch batch) => Task.CompletedTask;
        protected override void HandleException(Exception exception) => throw exception;
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public FakeModule Module { get; } = new();
        public FakeRuntime Runtime { get; }
        public TestRenderer Renderer { get; }
        public TestPalette Palette { get; }
        private readonly ServiceProvider _services;
        private readonly int _id;
        public Fixture()
        {
            Runtime = new FakeRuntime(Module);
            _services = new ServiceCollection().AddSingleton<IJSRuntime>(Runtime).BuildServiceProvider();
            Renderer = new TestRenderer(_services);
            Palette = Renderer.Create();
            _id = Renderer.Attach(Palette);
        }
        public Task Render(params string[] names) => Renderer.Dispatcher.InvokeAsync(() => Renderer.Render(_id,
            ParameterView.FromDictionary(new Dictionary<string, object?>
            { [nameof(WorkbenchToolPalette.Groups)] = names.Select(name => new WorkbenchToolGroup(name, [])).ToArray() })));
        public Task AfterRender(bool first) => Renderer.Dispatcher.InvokeAsync(() => Palette.AfterRender(first));
        public Task Invoke(string method, params object[] args) => Renderer.Dispatcher.InvokeAsync(async () =>
        {
            var result = typeof(WorkbenchToolPalette).GetMethod(method, Private)!.Invoke(Palette, args);
            if (result is Task task) await task;
        });
        public JsonNode State(string name)
        {
            var states = (IDictionary)typeof(WorkbenchToolPalette).GetField("_groupDockStates", Private)!.GetValue(Palette)!;
            return JsonSerializer.SerializeToNode(states[name], JsonOptions)!;
        }
        public async ValueTask DisposeAsync()
        {
            Module.Delay?.TrySetResult();
            Module.Observer.Delay?.TrySetResult();
            await Renderer.DisposeAsync();
            await _services.DisposeAsync();
        }
    }
#pragma warning restore BL0006

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private sealed class FakeRuntime(FakeModule module) : IJSRuntime
    {
        public int Imports { get; private set; }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, default, args);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            identifier.Should().Be("import");
            Imports++;
            return ValueTask.FromResult((TValue)(object)module);
        }
    }

    private sealed class FakeModule : IJSObjectReference
    {
        public double BoundsWidth { get; set; } = 1440;
        public double ResizeScale { get; set; } = 1;
        public List<string[]> Loads { get; } = [];
        public List<JsonNode> Saves { get; } = [];
        public TaskCompletionSource LoadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource? Delay { get; set; }
        public bool FailLoad { get; set; }
        public bool Disposed { get; private set; }
        public FakeObserver Observer { get; } = new();
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, default, args);
        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (identifier == "observeLayout") return (TValue)(object)Observer;
            if (identifier == "captureResize")
                return JsonSerializer.Deserialize<TValue>(JsonSerializer.Serialize(new
                { groupWidth = 232, scaleX = ResizeScale }), JsonOptions)!;
            if (identifier == "getBounds")
                return JsonSerializer.Deserialize<TValue>(JsonSerializer.Serialize(new
                { left = 0, top = 48, width = BoundsWidth, height = 800, scaleX = 1, scaleY = 1 }), JsonOptions)!;
            args.Should().HaveCount(1, "interop must pass one array, not spread its elements as arguments");
            var payload = JsonSerializer.SerializeToNode(args![0], JsonOptions)!.AsArray();
            if (identifier == "saveLayout") { Saves.Add(payload); return default!; }
            identifier.Should().Be("loadLayout");
            Loads.Add(payload.Select(state => state!["groupName"]!.GetValue<string>()).ToArray());
            LoadStarted.TrySetResult();
            if (Delay is not null) await Delay.Task;
            if (FailLoad) throw new JSException("Unavailable storage module");
            foreach (var state in payload)
            {
                state!["side"] = 0;
                state["floatingX"] = 700;
            }
            return payload.Deserialize<TValue>(JsonOptions)!;
        }
    }

    private sealed class FakeObserver : IJSObjectReference
    {
        public TaskCompletionSource RefreshStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource? Delay { get; set; }
        public bool Disconnected { get; private set; }
        public bool DisconnectOnDispose { get; set; }
        public bool Disposed { get; private set; }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => InvokeAsync<TValue>(identifier, default, args);
        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (identifier == "dispose")
            {
                if (DisconnectOnDispose) throw new JSDisconnectedException("Circuit disconnected");
                Disconnected = true;
            }
            else
            {
                identifier.Should().Be("refresh");
                RefreshStarted.TrySetResult();
                if (Delay is not null) await Delay.Task;
            }
            return default!;
        }
    }
}

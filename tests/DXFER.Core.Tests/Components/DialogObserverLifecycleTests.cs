using System.Reflection;
using DXFER.Blazor.Components;
using FluentAssertions;
using Microsoft.JSInterop;

namespace DXFER.Core.Tests.Components;

public sealed class DialogObserverLifecycleTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [Theory]
    [InlineData("appearance")]
    [InlineData("hotkeys")]
    [InlineData("settings")]
    public async Task ReentrantClosedRenderCannotUseObserverAlreadyBeingDisposed(string dialog)
    {
        var component = CreateDialog(dialog);
        var type = component.GetType();
        var observer = new Observer { HoldDisposal = true };
        var module = new Observer();
        type.GetField(dialog == "hotkeys" ? "_dialogObserver" : "_observer", Private)!.SetValue(component, observer);
        type.GetField("_module", Private)!.SetValue(component, module);
        type.GetField("_loaded", Private)!.SetValue(component, true);
        Task Render() => (Task)type.GetMethod("OnAfterRenderAsync", Private)!.Invoke(component, new object[] { false })!;
        var first = Render();
        await observer.DisposalStarted.Task;
        try
        {
            await Render();
            observer.CleanupCalls.Should().Be(1);
        }
        finally
        {
            observer.ReleaseDisposal.TrySetResult();
            await first;
            await ((IAsyncDisposable)component).DisposeAsync();
        }
        observer.Disposals.Should().Be(1);
    }

    [Theory]
    [InlineData("appearance")]
    [InlineData("hotkeys")]
    [InlineData("settings")]
    public async Task ComponentDisposalDuringClosedRenderDoesNotDisposeObserverTwice(string dialog)
    {
        var component = CreateDialog(dialog);
        var type = component.GetType();
        var observer = new Observer { HoldDisposal = true };
        type.GetField(dialog == "hotkeys" ? "_dialogObserver" : "_observer", Private)!.SetValue(component, observer);
        type.GetField("_module", Private)!.SetValue(component, new Observer());
        type.GetField("_loaded", Private)!.SetValue(component, true);
        var rendering = (Task)type.GetMethod("OnAfterRenderAsync", Private)!.Invoke(component, new object[] { false })!;
        await observer.DisposalStarted.Task;
        try { await ((IAsyncDisposable)component).DisposeAsync(); }
        finally { observer.ReleaseDisposal.TrySetResult(); await rendering; }
        observer.CleanupCalls.Should().Be(1);
        observer.Disposals.Should().Be(1);
        await ((IAsyncDisposable)component).DisposeAsync();
    }

    [Theory]
    [InlineData("appearance")]
    [InlineData("hotkeys")]
    [InlineData("settings")]
    public async Task DisconnectedCleanupStillReleasesBothReferencesAndRemainsIdempotent(string dialog)
    {
        var component = CreateDialog(dialog);
        var type = component.GetType();
        var observer = new Observer { DisconnectOnCleanup = true };
        var module = new Observer();
        type.GetField(dialog == "hotkeys" ? "_dialogObserver" : "_observer", Private)!.SetValue(component, observer);
        type.GetField("_module", Private)!.SetValue(component, module);
        await ((IAsyncDisposable)component).DisposeAsync();
        await ((IAsyncDisposable)component).DisposeAsync();
        observer.Disposals.Should().Be(1);
        module.Disposals.Should().Be(1);
    }

    private static object CreateDialog(string dialog) => dialog switch
    {
        "hotkeys" => new ToolHotkeyOptions(),
        "settings" => new WorkbenchSettings(),
        _ => new CanvasAppearance()
    };

    private sealed class Observer : IJSObjectReference
    {
        public bool HoldDisposal { get; init; }
        public bool DisconnectOnCleanup { get; init; }
        public TaskCompletionSource DisposalStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseDisposal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int CleanupCalls { get; private set; }
        public int Disposals { get; private set; }
        private bool _disposed;
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => InvokeAsync<T>(identifier, default, args);
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (identifier == "dispose") CleanupCalls++;
            if (DisconnectOnCleanup) throw new JSDisconnectedException("Disconnected during cleanup");
            return ValueTask.FromResult(default(T)!);
        }
        public async ValueTask DisposeAsync()
        {
            _disposed = true;
            Disposals++;
            DisposalStarted.TrySetResult();
            if (HoldDisposal) await ReleaseDisposal.Task;
        }
    }
}

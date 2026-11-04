using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using DXFER.Blazor.Components;
using DXFER.Core.Sync;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

namespace DXFER.Core.Tests.Components;

public sealed class WorkbenchSyncLoadTests
{
    private const string Secret = "synthetic-signed-token-must-not-leak";
    private const string DownloadUrl = "https://sync.invalid/download?editToken=" + Secret;
    private const string ValidDxf = "0\nSECTION\n2\nENTITIES\n0\nLINE\n10\n0\n20\n0\n11\n7.25\n21\n4.1\n0\nENDSEC\n0\nEOF\n";

    [Theory]
    [InlineData(HttpRequestError.SecureConnectionError, "TLS")]
    [InlineData(HttpRequestError.NameResolutionError, "DNS")]
    [InlineData(HttpRequestError.ConnectionError, "connection")]
    [InlineData(HttpRequestError.Unknown, "network")]
    public async Task NetworkFailureIsVisibleAndDoesNotLeakExceptionOrLaunchSecrets(HttpRequestError error, string expected)
    {
        await using var harness = await Harness.CreateAsync(_ =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException(error, DownloadUrl,
                new IOException(Secret))));

        await harness.InvokeAsync("TryLoadSyncLaunchDocumentAsync");

        var html = await harness.HtmlAsync();
        html.Should().Contain(expected).And.Contain("role=\"status\"").And.Contain("Retry Sync download");
        SaveButton(html).Should().Contain("disabled");
        html.Should().NotContain("Sync connection ready");
        harness.Logger.Messages.Should().Contain(message => message.Contains("failed"));
        AssertNoSecrets(html, harness.Logger);

        await harness.InvokeAsync("SaveToSyncCallbackAsync");
        (await harness.HtmlAsync()).Should().Contain(expected);
        harness.Handler.Requests.Should().HaveCount(1);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(500)]
    public async Task HttpFailureShowsStatusWithoutResponseOrUrl(int status)
    {
        await using var harness = await Harness.CreateAsync(_ => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)
        {
            Content = new StringContent(Secret),
            ReasonPhrase = Secret
        }));

        await harness.InvokeAsync("TryLoadSyncLaunchDocumentAsync");

        var html = await harness.HtmlAsync();
        html.Should().Contain($"HTTP {status}");
        SaveButton(html).Should().Contain("disabled");
        harness.Logger.Messages.Should().Contain(message => message.Contains(status.ToString()));
        AssertNoSecrets(html, harness.Logger);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingRuntimeAssemblyIsASafeServiceLoadFailure(bool wrapped)
    {
        var missingAssembly = new FileNotFoundException(DownloadUrl, Secret + ".dll");
        Exception failure = wrapped ? new HttpRequestException(DownloadUrl, missingAssembly) : missingAssembly;
        await using var harness = await Harness.CreateAsync(_ => Task.FromException<HttpResponseMessage>(failure));

        await harness.InvokeAsync("TryLoadSyncLaunchDocumentAsync");

        var html = await harness.HtmlAsync();
        html.Should().Contain("service dependency unavailable");
        SaveButton(html).Should().Contain("disabled");
        harness.Logger.Messages.Should().Contain(message => message.Contains("Category=service-load"));
        AssertNoSecrets(html, harness.Logger);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html>synthetic-signed-token-must-not-leak</html>")]
    [InlineData("0\nSECTION\n2\nENTITIES\n0\nHATCH\n0\nENDSEC\n0\nEOF\n")]
    public async Task EmptyOrUnsupportedImportIsVisible(string body)
    {
        await using var harness = await Harness.CreateAsync(_ => Task.FromResult(Ok(body)));

        await harness.InvokeAsync("TryLoadSyncLaunchDocumentAsync");

        var html = await harness.HtmlAsync();
        html.Should().Contain("Production cleanup requires review");
        SaveButton(html).Should().Contain("disabled");
        AssertNoSecrets(html, harness.Logger);
    }

    [Fact]
    public async Task LoadingBlocksSaveAndDuplicateRequestsThenTimeoutCanRetrySameLaunch()
    {
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var harness = await Harness.CreateAsync(attempt => attempt == 1
            ? pending.Task
            : Task.FromResult(Ok(ValidDxf)));

        SaveButton(await harness.HtmlAsync()).Should().Contain("disabled");
        var load = await harness.StartLoadAsync();
        var loading = await harness.HtmlAsync();
        loading.Should().Contain("Loading Sync DXF");
        SaveButton(loading).Should().Contain("disabled");
        await harness.InvokeAsync("RetrySyncLaunchDocumentAsync");
        await harness.InvokeAsync("TryLoadSyncLaunchDocumentAsync");
        await harness.InvokeAsync("SaveToSyncCallbackAsync");
        harness.Handler.Requests.Should().HaveCount(1);

        pending.SetException(new TaskCanceledException(DownloadUrl));
        await load;
        var failed = await harness.HtmlAsync();
        failed.Should().Contain("timed out");
        SaveButton(failed).Should().Contain("disabled");

        await harness.InvokeAsync("RetrySyncLaunchDocumentAsync");
        var recovered = await harness.HtmlAsync();
        recovered.Should().Contain("Sync DXF loaded").And.NotContain("timed out");
        SaveButton(recovered).Should().NotContain("disabled");
        harness.Handler.Requests.Should().Equal(new Uri(DownloadUrl), new Uri(DownloadUrl));
        harness.Logger.Messages.Should().Contain(message => message.Contains("loaded") && message.Contains("1 entities"));
        AssertNoSecrets(failed + recovered, harness.Logger);

        await harness.InvokeAsync("RetrySyncLaunchDocumentAsync");
        harness.Handler.Requests.Should().HaveCount(2, "retry must not replace successfully loaded geometry");
    }

    [Fact]
    public async Task MissingSourceIsVisibleAndCannotRetryOrSave()
    {
        await using var harness = await Harness.CreateAsync(_ => Task.FromResult(Ok(ValidDxf)), hasInput: false);

        await harness.InvokeAsync("TryLoadSyncLaunchDocumentAsync");

        var html = await harness.HtmlAsync();
        html.Should().Contain("missing a DXF source").And.NotContain("Retry Sync download");
        SaveButton(html).Should().Contain("disabled");
        harness.Handler.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SuccessfulSaveIsVisibleAndConsumedEvenWhenTabCannotClose(bool closeThrows)
    {
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var harness = await Harness.CreateAsync(attempt => attempt == 1
            ? Task.FromResult(Ok(ValidDxf)) : pending.Task, js: new CloseRuntime(closeThrows));
        await harness.InvokeAsync("TryLoadSyncLaunchDocumentAsync");
        var save = await harness.StartAsync("SaveToSyncCallbackAsync");
        var saving = await harness.HtmlAsync();
        saving.Should().Contain("Saving to Sync").And.NotContain("Removed 0 exact duplicate");
        SaveButton(saving).Should().Contain("disabled");
        await harness.InvokeAsync("SaveToSyncCallbackAsync");
        harness.Handler.Requests.Should().HaveCount(2);

        pending.SetResult(Ok("{}"));
        await save;
        var saved = await harness.HtmlAsync();
        saved.Should().Contain("Saved to Sync").And.Contain("Reopen from Sync")
            .And.NotContain("Removed 0 exact duplicate");
        SaveButton(saved).Should().Contain("disabled");
        await harness.InvokeAsync("SaveToSyncCallbackAsync");
        harness.Handler.Requests.Should().HaveCount(2, "the successful launch job is consumed");
        AssertNoSecrets(saved, harness.Logger);
    }

    [Fact]
    public async Task SaveFailureIsNotHiddenByCleanupAndCanRetryWithoutExposingSecrets()
    {
        await using var harness = await Harness.CreateAsync(attempt => Task.FromResult(attempt switch
        {
            1 => Ok(ValidDxf),
            2 => new HttpResponseMessage(HttpStatusCode.BadRequest) { ReasonPhrase = Secret, Content = new StringContent(Secret) },
            _ => Ok("{}")
        }), js: new CloseRuntime(false));
        await harness.InvokeAsync("TryLoadSyncLaunchDocumentAsync");
        await harness.InvokeAsync("SaveToSyncCallbackAsync");
        var failed = await harness.HtmlAsync();
        failed.Should().Contain("HTTP 400").And.NotContain("Removed 0 exact duplicate");
        SaveButton(failed).Should().NotContain("disabled");
        AssertNoSecrets(failed, harness.Logger);
        await harness.InvokeAsync("SaveToSyncCallbackAsync");
        (await harness.HtmlAsync()).Should().Contain("Saved to Sync");
        SaveButton(await harness.HtmlAsync()).Should().Contain("disabled");
        harness.Handler.Requests.Should().HaveCount(3);
    }

    [Fact]
    public async Task SaveTimeoutReportsUnconfirmedOutcomeWithoutClaimingFailureOrSuccess()
    {
        await using var harness = await Harness.CreateAsync(attempt => attempt == 1
            ? Task.FromResult(Ok(ValidDxf))
            : Task.FromException<HttpResponseMessage>(new TaskCanceledException(Secret)));
        await harness.InvokeAsync("TryLoadSyncLaunchDocumentAsync");
        await harness.InvokeAsync("SaveToSyncCallbackAsync");
        var html = await harness.HtmlAsync();
        html.Should().Contain("Save confirmation timed out").And.Contain("Check Sync before retrying")
            .And.NotContain("Removed 0 exact duplicate").And.NotContain("Saved to Sync");
        SaveButton(html).Should().Contain("disabled");
        await harness.InvokeAsync("SaveToSyncCallbackAsync");
        harness.Handler.Requests.Should().HaveCount(2);
        AssertNoSecrets(html, harness.Logger);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(401)]
    [InlineData(0)]
    public async Task UncertainSaveResponseBlocksBlindRetry(int status)
    {
        await using var harness = await Harness.CreateAsync(attempt => attempt == 1
            ? Task.FromResult(Ok(ValidDxf))
            : status == 0 ? Task.FromException<HttpResponseMessage>(new HttpRequestException(Secret))
            : Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { ReasonPhrase = Secret }));
        await harness.InvokeAsync("TryLoadSyncLaunchDocumentAsync");
        await harness.InvokeAsync("SaveToSyncCallbackAsync");
        var html = await harness.HtmlAsync();
        html.Should().Contain("Save not confirmed").And.NotContain("Saved to Sync");
        SaveButton(html).Should().Contain("disabled");
        await harness.InvokeAsync("SaveToSyncCallbackAsync");
        harness.Handler.Requests.Should().HaveCount(2);
        AssertNoSecrets(html, harness.Logger);
    }

    [Theory]
    [InlineData("import")]
    [InlineData("closeLaunchedSyncTab")]
    public async Task PendingCloseInteropIsBoundedAndCannotUndoConfirmedSave(string pendingCall)
    {
        await using var harness = await Harness.CreateAsync(attempt => Task.FromResult(Ok(attempt == 1 ? ValidDxf : "{}")),
            js: new CloseRuntime(false, pendingCall));
        await harness.InvokeAsync("TryLoadSyncLaunchDocumentAsync");
        await harness.InvokeAsync("SaveToSyncCallbackAsync").WaitAsync(TimeSpan.FromSeconds(5));
        var html = await harness.HtmlAsync();
        html.Should().Contain("Saved to Sync").And.Contain("Close this tab");
        SaveButton(html).Should().Contain("disabled");
        harness.Handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task FailedFallbackWriteCannotHideSaveOutcome()
    {
        var existingFile = Path.GetTempFileName();
        try
        {
            await using var harness = await Harness.CreateAsync(attempt => Task.FromResult(attempt == 1
                ? Ok(ValidDxf) : new HttpResponseMessage(HttpStatusCode.BadRequest)), jobFolder: existingFile);
            await harness.InvokeAsync("TryLoadSyncLaunchDocumentAsync");
            await harness.InvokeAsync("SaveToSyncCallbackAsync");
            var html = await harness.HtmlAsync();
            html.Should().Contain("HTTP 400").And.Contain("fallback file could not be written")
                .And.NotContain(existingFile).And.NotContain("Removed 0 exact duplicate");
            SaveButton(html).Should().NotContain("disabled");
        }
        finally { File.Delete(existingFile); }
    }

    [Fact]
    public void CompactSyncStatusPreservesLayoutContract()
    {
        // Desktop keeps the top-right strip; mobile reserves a row above the palette.
        // Align/wrap untruncated status within the viewport; keep stable, accessible actions.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DXFER.slnx")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull();
        var component = Path.Combine(directory!.FullName, "src", "DXFER.Blazor", "Components", "DrawingWorkbench.razor");
        var markup = File.ReadAllText(component);
        var css = File.ReadAllText(component + ".css");
        markup.Should().Contain("aria-live=\"polite\"").And.Contain("CadIconButton")
            .And.Contain("Retry Sync download").And.Contain("dxfer-sync-actions");
        css.Should().Contain("max-width: calc(100% - 1.1rem)")
            .And.Contain("flex-wrap: wrap").And.Contain("overflow-wrap: anywhere")
            .And.Contain(".dxfer-sync-state-error")
            .And.Contain("grid-template-rows: auto minmax(0, 1fr) auto !important;");
    }

    private static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private static string SaveButton(string html)
    {
        var button = Regex.Match(html, "<button[^>]*class=\"dxfer-sync-send-button\"[^>]*>").Value;
        button.Should().NotBeEmpty();
        return button;
    }

    private static void AssertNoSecrets(string html, CapturingLogger logger)
    {
        html.Should().NotContain(Secret).And.NotContain("sync.invalid");
        string.Join("\n", logger.Messages).Should().NotContain(Secret).And.NotContain("sync.invalid");
        logger.Exceptions.Should().OnlyContain(exception => exception == null);
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly ServiceProvider _services;
        private readonly HtmlRenderer _renderer;
        private readonly CapturingActivator _activator;
        private HtmlRootComponent _root;

        private Harness(ServiceProvider services, CapturingActivator activator, Handler handler, CapturingLogger logger)
        {
            _services = services;
            _activator = activator;
            Handler = handler;
            Logger = logger;
            _renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        }

        public Handler Handler { get; }
        public CapturingLogger Logger { get; }

        public static async Task<Harness> CreateAsync(Func<int, Task<HttpResponseMessage>> respond, bool hasInput = true, IJSRuntime? js = null, string? jobFolder = null)
        {
            var activator = new CapturingActivator();
            var handler = new Handler(respond);
            var logger = new CapturingLogger();
            var services = new ServiceCollection();
            services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
            services.AddSingleton<ILogger<DrawingWorkbench>>(logger);
            services.AddSingleton<IComponentActivator>(activator);
            services.AddSingleton<NavigationManager>(new TestNavigation(hasInput, jobFolder));
            services.AddSingleton<IJSRuntime>(js ?? new NoJsRuntime());
            services.AddSingleton<WorkbenchMenuCommandService>();
            services.AddSingleton<ToolHotkeyService>();
            services.AddSingleton(new HttpClient(handler));
            services.AddSingleton<SyncCallbackClient>();
            var harness = new Harness(services.BuildServiceProvider(), activator, handler, logger);
            harness._root = await harness._renderer.Dispatcher.InvokeAsync(() =>
                harness._renderer.RenderComponentAsync<DrawingWorkbench>());
            return harness;
        }

        public Task<string> HtmlAsync() => _renderer.Dispatcher.InvokeAsync(() => _root.ToHtmlString());

        public Task InvokeAsync(string name) => _renderer.Dispatcher.InvokeAsync(() => Invoke(name));

        public Task<Task> StartLoadAsync() => StartAsync("TryLoadSyncLaunchDocumentAsync");

        public async Task<Task> StartAsync(string name)
        {
            Task load = Task.CompletedTask;
            await _renderer.Dispatcher.InvokeAsync(() => { load = Invoke(name); });
            return load;
        }

        private Task Invoke(string name) => (Task)typeof(DrawingWorkbench)
            .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(_activator.Workbench, null)!;

        public async ValueTask DisposeAsync()
        {
            await _renderer.DisposeAsync();
            await _services.DisposeAsync();
        }
    }

    private sealed class CapturingActivator : IComponentActivator
    {
        public DrawingWorkbench? Workbench { get; private set; }

        public IComponent CreateInstance(Type componentType)
        {
            var component = (IComponent)Activator.CreateInstance(componentType)!;
            if (component is DrawingWorkbench workbench)
            {
                Workbench = workbench;
            }

            return component;
        }
    }

    private sealed class TestNavigation : NavigationManager
    {
        public TestNavigation(bool hasInput, string? jobFolder) => Initialize("https://dxfer.invalid/",
            "https://dxfer.invalid/?syncBaseUrl=https%3A%2F%2Fsync.invalid&artifactId=1&jobId=test&editToken=" + Secret
            + (jobFolder is null ? "" : "&jobFolder=" + System.Uri.EscapeDataString(jobFolder))
            + (hasInput ? "&downloadUrl=" + System.Uri.EscapeDataString(DownloadUrl) : ""));
    }

    private sealed class NoJsRuntime : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) => throw new NotSupportedException();
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            throw new NotSupportedException();
    }

    private sealed class CloseRuntime(bool throws, string? pendingCall = null) : IJSRuntime, IJSObjectReference
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            if (identifier == "import") return ValueTask.FromResult((TValue)(object)this);
            if (identifier != "closeLaunchedSyncTab") throw new NotSupportedException(identifier);
            if (throws) throw new JSException(Secret);
            return ValueTask.FromResult((TValue)(object)false);
        }
        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            if (identifier == pendingCall) await Task.Delay(Timeout.Infinite, cancellationToken);
            return await InvokeAsync<TValue>(identifier, args);
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Handler(Func<int, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return respond(Requests.Count);
        }
    }

    private sealed class CapturingLogger : ILogger<DrawingWorkbench>
    {
        public List<string> Messages { get; } = new();
        public List<Exception?> Exceptions { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
            Exceptions.Add(exception);
        }
    }
}

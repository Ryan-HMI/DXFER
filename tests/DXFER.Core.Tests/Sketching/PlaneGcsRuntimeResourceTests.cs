using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

// Capacity assertions must not compete with the real-engine integration tests.
[CollectionDefinition("PlaneGCS resource budget", DisableParallelization = true)]
public sealed class PlaneGcsRuntimeResourceCollection;

[Collection("PlaneGCS resource budget")]
public sealed class PlaneGcsRuntimeResourceTests
{
    private const int MessageLimit = 8 * 1024 * 1024;
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CleanupTimerCreationPreservesCallerExecutionContextFlow(bool suppressFlow)
    {
        var original = ExecutionContext.IsFlowSuppressed();
        AsyncFlowControl? suppression = suppressFlow && !original ? ExecutionContext.SuppressFlow() : null;
        try
        {
            var expected = ExecutionContext.IsFlowSuppressed();
            var factory = typeof(PlaneGcsNodeRuntime).GetMethod("CreateCleanupTimer",
                BindingFlags.Static | BindingFlags.NonPublic)!;
            // Exercise the factory directly even when another test initialized the static timer.
            using var timer = (Timer)factory.Invoke(null, null)!;
            ExecutionContext.IsFlowSuppressed().Should().Be(expected);
        }
        finally { suppression?.Undo(); }
        ExecutionContext.IsFlowSuppressed().Should().Be(original);
    }

    [Fact]
    public void OversizedRequestIsRejectedBeforeStartingAProcess()
    {
        using var fixture = new HostFixture("valid");
        using var runtime = fixture.Runtime();

        var solve = () => runtime.Solve(Request(new string('x', MessageLimit + 1)));

        solve.Should().Throw<InvalidOperationException>().WithMessage("*message limit*");
        File.Exists(fixture.RunsPath).Should().BeFalse();
    }

    [Theory]
    [InlineData("stdout-flood")]
    [InlineData("stderr-flood")]
    [InlineData("partial-frame")]
    [InlineData("extra-frame")]
    public void BrokenOrUnboundedOutputStopsTheChildAndNextRequestRecovers(string mode)
    {
        using var fixture = new HostFixture(mode);
        using var runtime = fixture.Runtime(timeout: 30000);
        var watch = Stopwatch.StartNew();

        var solve = () => runtime.Solve(Request());

        solve.Should().Throw<Exception>();
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(15),
            "framing and stderr limits should fail before the 30-second solve timeout");
        AssertExited(fixture.ProcessIds().Should().ContainSingle().Which);
        runtime.Solve(Request()).Status.Should().Be("solved");
        fixture.ProcessIds().Should().HaveCount(2);
    }

    [Fact]
    public void NonNewlineStderrIsDrainedWithoutContaminatingTheResponse()
    {
        using var fixture = new HostFixture("stderr-small");
        using var runtime = fixture.Runtime();

        var result = runtime.Solve(Request("separate-response"));

        result.Status.Should().Be("solved");
        result.Diagnostics.Should().Contain("separate-response");
        runtime.Solve(Request()).Status.Should().Be("solved");
        fixture.ProcessIds().Should().ContainSingle();
    }

    [Fact]
    public void EnvironmentIsClearedAndModuleModeIsExplicit()
    {
        using var fixture = new HostFixture("environment");
        using var runtime = fixture.Runtime();
        const string key = "DXFER_RUNTIME_TEST_SECRET";
        var previous = Environment.GetEnvironmentVariable(key);
        try
        {
            Environment.SetEnvironmentVariable(key, "must-not-reach-child");
            var result = runtime.Solve(Request());
            result.Diagnostics.Should().Contain("secret-absent");
            result.Diagnostics.Should().Contain("--experimental-default-type=module");
            result.Diagnostics.Should().Contain("--wasm-max-mem-pages=4096");
            result.Diagnostics.Should().Contain("env:LANG");
        }
        finally { Environment.SetEnvironmentVariable(key, previous); }
    }

    [Fact]
    public void WasmMemoryCannotInitializeOrGrowBeyond256MiBEvenWhenModuleMaximumIsLarger()
    {
        using var fixture = new HostFixture("wasm-cap");
        using var runtime = fixture.Runtime();

        var result = runtime.Solve(Request());

        result.Status.Should().Be("solved");
        result.Diagnostics.Should().Contain("wasm-cap-enforced:268435456");
    }

    [Fact]
    public void ChildWasmAllocationFailurePreservesGeometryAndNextSolveStartsFresh()
    {
        using var fixture = new HostFixture("wasm-oom");
        using var runtime = fixture.Runtime();
        var solver = new PlaneGcsSketchSolverAdapter(runtime);
        var document = new DrawingDocument([new LineEntity(EntityId.Create("edge"), new Point2(0, 0), new Point2(4, 0))]);
        var request = SketchSolveRequest.FromDocument(document);

        var failed = solver.Solve(request);

        failed.Status.Should().Be(SketchSolveStatus.Unavailable);
        failed.Document.Should().BeSameAs(document);
        AssertExited(fixture.ProcessIds().Should().ContainSingle().Which);
        var recovered = solver.Solve(request);
        recovered.Status.Should().Be(SketchSolveStatus.Solved);
        recovered.Document.Entities.Should().Equal(document.Entities);
        fixture.ProcessIds().Should().HaveCount(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RealHostRetiresNativeAbortWhetherThrownOrConvertedToFailedResult(bool returnedFailure)
    {
        using var fixture = new HostFixture("valid");
        fixture.UseAbortingModule(returnedFailure);
        using var runtime = new PlaneGcsNodeRuntime(NodeExecutable(), RepositoryHost(), fixture.ModulePath);
        var solver = new PlaneGcsSketchSolverAdapter(runtime);
        var document = new DrawingDocument([new LineEntity(EntityId.Create("edge"), new Point2(0, 0), new Point2(4, 0))]);
        var request = SketchSolveRequest.FromDocument(document);

        var failed = solver.Solve(request);

        failed.Status.Should().Be(SketchSolveStatus.Unavailable);
        failed.Document.Should().BeSameAs(document);
        AssertExited(fixture.ProcessIds().Should().ContainSingle().Which);
        var recovered = solver.Solve(request);
        recovered.Status.Should().Be(SketchSolveStatus.Solved);
        recovered.Document.Entities.Should().Equal(document.Entities);
        fixture.ProcessIds().Should().HaveCount(2);
    }

    [Fact]
    public void ProcessCapacityIsBoundedAndDisposalReleasesOneSlot()
    {
        var fixtures = new List<HostFixture>();
        var runtimes = new List<PlaneGcsNodeRuntime>();
        try
        {
            for (var i = 0; i < 9; i++)
            {
                var fixture = new HostFixture("valid");
                fixtures.Add(fixture);
                runtimes.Add(fixture.Runtime(idleTimeout: 3600000));
            }
            for (var i = 0; i < 8; i++)
                runtimes[i].Solve(Request()).Status.Should().Be("solved");

            var ninthSolve = () => runtimes[8].Solve(Request());
            ninthSolve.Should().Throw<InvalidOperationException>().WithMessage("*capacity*");
            File.Exists(fixtures[8].RunsPath).Should().BeFalse();

            runtimes[0].Dispose();
            AssertExited(fixtures[0].ProcessIds().Single());
            runtimes[8].Solve(Request()).Status.Should().Be("solved");
        }
        finally
        {
            foreach (var runtime in runtimes) runtime.Dispose();
            foreach (var fixture in fixtures) fixture.Dispose();
        }
    }

    [Fact]
    public void FailedStartsDoNotConsumeProcessSlots()
    {
        using var fixture = new HostFixture("valid");
        for (var i = 0; i < 12; i++)
        {
            using var runtime = new PlaneGcsNodeRuntime(Path.Combine(fixture.DirectoryPath, "absent-node"),
                fixture.HostPath, fixture.ModulePath);
            var solve = () => runtime.Solve(Request());
            solve.Should().Throw<System.ComponentModel.Win32Exception>();
        }

        using var working = fixture.Runtime();
        working.Solve(Request()).Status.Should().Be("solved");
    }

    [Fact]
    public void IdleChildIsReapedAndTheSameRuntimeCanStartFresh()
    {
        using var fixture = new HostFixture("valid");
        using var runtime = fixture.Runtime(idleTimeout: 100);
        runtime.Solve(Request()).Status.Should().Be("solved");
        var first = fixture.ProcessIds().Single();

        SpinWait.SpinUntil(() => HasExited(first), TimeSpan.FromSeconds(10)).Should().BeTrue();
        runtime.Solve(Request()).Status.Should().Be("solved");
        fixture.ProcessIds().Should().HaveCount(2);
    }

    [Fact]
    public async Task SeparateRuntimesNeverShareProcessStateOrDisposal()
    {
        using var firstFixture = new HostFixture("valid");
        using var secondFixture = new HostFixture("valid");
        using var first = firstFixture.Runtime();
        using var second = secondFixture.Runtime();
        var responses = await Task.WhenAll(
            Task.Run(() => first.Solve(Request("first-circuit"))),
            Task.Run(() => second.Solve(Request("second-circuit"))));

        responses[0].Diagnostics.Should().Contain("first-circuit").And.NotContain("second-circuit");
        responses[1].Diagnostics.Should().Contain("second-circuit").And.NotContain("first-circuit");
        var firstPid = firstFixture.ProcessIds().Single();
        var secondPid = secondFixture.ProcessIds().Single();
        firstPid.Should().NotBe(secondPid);
        first.Dispose();
        AssertExited(firstPid);
        HasExited(secondPid).Should().BeFalse();
        second.Solve(Request("still-second")).Diagnostics.Should().Contain("still-second");
        secondFixture.ProcessIds().Should().ContainSingle();
    }

    [Fact]
    public void CleanupFailureRetainsOwnershipAndBudgetUntilRetryAfterDisposal()
    {
        using var fixture = new HostFixture("valid");
        using var runtime = fixture.Runtime();
        runtime.Solve(Request());
        var pid = fixture.ProcessIds().Single();
        var type = typeof(PlaneGcsNodeRuntime);
        var processField = type.GetField("_process", PrivateInstance)!;
        var gate = type.GetField("_gate", PrivateInstance)!.GetValue(runtime)!;
        var owners = (ConcurrentDictionary<PlaneGcsNodeRuntime, byte>)type
            .GetField("ProcessOwners", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var slots = (SemaphoreSlim)type
            .GetField("ProcessSlots", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var available = slots.CurrentCount;

        lock (gate)
        {
            var actualProcess = processField.GetValue(runtime);
            using var unavailableHandle = new Process();
            try
            {
                // Simulate an OS/process-handle failure without killing an unrelated process.
                processField.SetValue(runtime, unavailableHandle);
                runtime.Dispose();
                processField.GetValue(runtime).Should().BeSameAs(unavailableHandle);
                owners.ContainsKey(runtime).Should().BeTrue();
                slots.CurrentCount.Should().Be(available);
                HasExited(pid).Should().BeFalse();
            }
            finally { processField.SetValue(runtime, actualProcess); }
        }

        SpinWait.SpinUntil(() => !owners.ContainsKey(runtime), TimeSpan.FromSeconds(10)).Should().BeTrue();
        AssertExited(pid);
        slots.CurrentCount.Should().Be(available + 1);
    }

    [Fact]
    public void RealHostLoadsJsModulesWithRelativeImportsFromAPathContainingSpaces()
    {
        using var fixture = new HostFixture("valid");
        using var runtime = new PlaneGcsNodeRuntime(NodeExecutable(), RepositoryHost(), fixture.ModulePath);

        var result = runtime.Solve(Request());

        result.Status.Should().Be("solved");
        result.Diagnostics.Should().Contain("relative-module-loaded");
    }

    [Fact]
    public void RealHostRejectsOversizedResponseBeforeSerializationAndRemainsUsable()
    {
        using var fixture = new HostFixture("valid");
        using var runtime = new PlaneGcsNodeRuntime(NodeExecutable(), RepositoryHost(), fixture.ModulePath);

        var oversized = runtime.Solve(Request("oversized-response"));
        oversized.Status.Should().Be("failed");
        oversized.Diagnostics.Should().Contain(message => message.Contains("message limit"));
        runtime.Solve(Request()).Status.Should().Be("solved");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealHostRejectsOversizedOrIncompleteInputWithoutWaitingForNewline(bool oversized)
    {
        using var fixture = new HostFixture("valid");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var process = new Process
        {
            StartInfo = StartInfo(RepositoryHost(), fixture.ModulePath)
        };
        process.Start().Should().BeTrue();
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            var data = Encoding.UTF8.GetBytes(oversized ? new string('x', MessageLimit + 1) : "{\"id\":1");
            await process.StandardInput.BaseStream.WriteAsync(data, timeout.Token);
            await process.StandardInput.BaseStream.FlushAsync(timeout.Token);
            if (!oversized) process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
            process.ExitCode.Should().NotBe(0);
            (await stdout).Should().BeEmpty();
            (await stderr).Should().Contain(oversized ? "message limit" : "Incomplete solver request");
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
        }
    }

    private static PlaneGcsRequest Request(string label = "resource-test") =>
        new(new([], [], [], JsonSerializer.SerializeToElement(new { }), false), [], [], [label], []);

    private static bool HasExited(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.HasExited;
        }
        catch (ArgumentException) { return true; }
    }

    private static void AssertExited(int pid) => HasExited(pid).Should().BeTrue($"child {pid} must have exited");

    private static string RepositoryHost()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, "src/DXFER.Web/Solver/planeGcsHost.mjs");
            if (File.Exists(path)) return path;
        }
        throw new InvalidOperationException("Resource tests require the repository Node host.");
    }

    private static string NodeExecutable()
    {
        var name = OperatingSystem.IsWindows() ? "node.exe" : "node";
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory)) continue;
            var path = Path.Combine(directory, name);
            if (File.Exists(path)) return Path.GetFullPath(path);
        }
        throw new InvalidOperationException("Node is required for PlaneGCS runtime resource tests.");
    }

    private static ProcessStartInfo StartInfo(string host, string module)
    {
        var start = new ProcessStartInfo(NodeExecutable())
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("--experimental-default-type=module");
        start.ArgumentList.Add(host);
        start.ArgumentList.Add(module);
        start.Environment.Clear();
        start.Environment["LANG"] = "C.UTF-8";
        return start;
    }

    private sealed class HostFixture : IDisposable
    {
        public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "dxfer runtime resources " + Guid.NewGuid().ToString("N"));
        public string HostPath => Path.Combine(DirectoryPath, "host.mjs");
        public string ModulePath => Path.Combine(DirectoryPath, "module.js");
        public string RunsPath => Path.Combine(DirectoryPath, "runs.json");

        public HostFixture(string mode)
        {
            Directory.CreateDirectory(DirectoryPath);
            File.WriteAllText(Path.Combine(DirectoryPath, "relative.js"), "export const marker = 'relative-module-loaded';\n");
            File.WriteAllText(ModulePath, """
                import { marker } from './relative.js';
                export async function createPlaneGcsSolver() {
                    return {
                        solve(request) {
                            return { status: 'solved', engine: 'PlaneGCS', entities: [],
                                diagnostics: [request.fixedReferenceKeys[0] === 'oversized-response'
                                    ? 'x'.repeat(9 * 1024 * 1024) : marker] };
                        },
                        dispose() {}
                    };
                }
                """);
            File.WriteAllText(HostPath, $$"""
                import { createInterface } from 'node:readline';
                import { existsSync, readFileSync, writeFileSync } from 'node:fs';
                const mode = {{JsonSerializer.Serialize(mode)}};
                const path = new URL('./runs.json', import.meta.url);
                const runs = existsSync(path) ? JSON.parse(readFileSync(path, 'utf8')) : [];
                runs.push(process.pid);
                writeFileSync(path, JSON.stringify(runs));
                const first = runs.length === 1;
                const write = (stream, data) => new Promise((resolve, reject) =>
                    stream.write(data, error => error ? reject(error) : resolve()));
                for await (const line of createInterface({ input: process.stdin, crlfDelay: Infinity })) {
                    const message = JSON.parse(line);
                    if (first && mode === 'wasm-oom') {
                        // Deliberately exceed the engine cap in the disposable child.
                        // This is allocation-failure isolation, not a real PlaneGCS OOM fixture.
                        new WebAssembly.Memory({ initial: 4097, maximum: 32768 });
                        throw new Error('WASM memory cap was not enforced.');
                    }
                    if (first && (mode === 'stdout-flood' || mode === 'stderr-flood')) {
                        const stream = mode === 'stdout-flood' ? process.stdout : process.stderr;
                        for (let i = 0; i < 145; i++) await write(stream, Buffer.alloc(65536, 120));
                        continue;
                    }
                    if (first && mode === 'partial-frame') {
                        await write(process.stdout, '{"id":');
                        process.exit(0);
                    }
                    if (mode === 'stderr-small') await write(process.stderr, Buffer.alloc(65536, 120));
                    const diagnostics = mode === 'environment'
                        ? [process.env.DXFER_RUNTIME_TEST_SECRET ?? 'secret-absent',
                           ...process.execArgv, 'env:' + Object.keys(process.env).sort().join(',')]
                        : [message.request.fixedReferenceKeys[0] ?? 'resource-test'];
                    if (mode === 'wasm-cap') {
                        const memory = new WebAssembly.Memory({ initial: 1, maximum: 32768 });
                        memory.grow(4095);
                        if (memory.buffer.byteLength !== 256 * 1024 * 1024) throw new Error('Unexpected memory size.');
                        let refusedGrowth = false, refusedInitial = false;
                        try { memory.grow(1); } catch (error) { refusedGrowth = error instanceof RangeError; }
                        try { new WebAssembly.Memory({ initial: 4097, maximum: 32768 }); }
                        catch (error) { refusedInitial = error instanceof RangeError; }
                        if (!refusedGrowth || !refusedInitial) throw new Error('WASM memory cap was not enforced.');
                        diagnostics.push('wasm-cap-enforced:' + memory.buffer.byteLength);
                    }
                    const response = JSON.stringify({ id: message.id,
                        result: { status: 'solved', engine: 'PlaneGCS', entities: message.request.document.entities, diagnostics } });
                    await write(process.stdout, response + '\n' + (first && mode === 'extra-frame' ? '{}\n' : ''));
                }
                """);
        }

        public PlaneGcsNodeRuntime Runtime(int timeout = 5000, int idleTimeout = 60000) =>
            new(NodeExecutable(), HostPath, ModulePath, timeout, idleTimeout);

        public void UseAbortingModule(bool returnedFailure)
        {
            File.WriteAllText(ModulePath, $$"""
                import { existsSync, readFileSync, writeFileSync } from 'node:fs';
                export async function createPlaneGcsSolver() {
                    const path = new URL('./runs.json', import.meta.url);
                    const runs = existsSync(path) ? JSON.parse(readFileSync(path, 'utf8')) : [];
                    runs.push(process.pid);
                    writeFileSync(path, JSON.stringify(runs));
                    return {
                        solve(request) {
                            // Synthetic abort signalling exercises the real host's retirement path.
                            if (runs.length === 1) {
                                if ({{JsonSerializer.Serialize(returnedFailure)}})
                                    return { status: 'failed', engine: 'PlaneGCS',
                                        entities: request.document.entities, diagnostics: ['Aborted(OOM). Synthetic fixture.'] };
                                throw new WebAssembly.RuntimeError('Synthetic native trap.');
                            }
                            return { status: 'solved', engine: 'PlaneGCS',
                                entities: request.document.entities, diagnostics: ['Fresh module after native abort.'] };
                        },
                        dispose() {}
                    };
                }
                """);
        }

        public int[] ProcessIds() => JsonSerializer.Deserialize<int[]>(File.ReadAllText(RunsPath))!;

        public void Dispose() => Directory.Delete(DirectoryPath, recursive: true);
    }
}

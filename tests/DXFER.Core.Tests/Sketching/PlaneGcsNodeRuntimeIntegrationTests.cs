using System.Diagnostics;
using System.Text.Json;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class PlaneGcsNodeRuntimeIntegrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RealVendoredEngineSolvesThroughNodeTransportAndAdapter(bool vertical)
    {
        var root = RepositoryRoot();
        using var runtime = new PlaneGcsNodeRuntime(NodeExecutable(),
            Path.Combine(root, "src/DXFER.Web/Solver/planeGcsHost.mjs"),
            Path.Combine(root, "src/DXFER.Blazor/wwwroot/planeGcsSolver.js"));
        var observed = new ObservedRuntime(runtime);
        var solver = new PlaneGcsSketchSolverAdapter(observed);
        var original = new DrawingDocument([new LineEntity(EntityId.Create("edge"), new(0, 0), new(3, 4))], [],
            [new SketchConstraint("fixed-start", SketchConstraintKind.Fix, ["edge:start"], SketchConstraintState.Satisfied)]);
        var axis = new SketchConstraint("axis", vertical ? SketchConstraintKind.Vertical : SketchConstraintKind.Horizontal, ["edge"]);
        var size = new SketchDimension("size", SketchDimensionKind.LinearDistance, ["edge:start", "edge:end"], 10, null, true);

        var result = solver.Solve(new SketchSolveRequest(original, [axis], [size]));

        result.Status.Should().Be(SketchSolveStatus.Solved, string.Join(" ", result.Diagnostics));
        observed.LastResponse.Should().NotBeNull();
        observed.LastResponse!.Engine.Should().Be("PlaneGCS");
        observed.LastResponse.Status.Should().Be("solved");
        var line = result.Document.Entities.Should().ContainSingle().Which.Should().BeOfType<LineEntity>().Which;
        line.Start.Should().Be(new Point2(0, 0));
        line.End.X.Should().BeApproximately(vertical ? 0 : 10, 1e-6);
        line.End.Y.Should().BeApproximately(vertical ? 10 : 0, 1e-6);
        result.Document.Constraints.Should().OnlyContain(c => SketchConstraintService.ValidateConstraint(result.Document, c).State == SketchConstraintState.Satisfied);
        SketchDimensionSolverService.IsDimensionSatisfied(result.Document, result.Document.Dimensions.Single()).Should().BeTrue();
        ((LineEntity)original.Entities.Single()).End.Should().Be(new Point2(3, 4));
        original.Dimensions.Should().BeEmpty();
    }

    [Fact]
    public void MissingModuleReturnsUnavailableWithoutFallbackOrGeometryChange()
    {
        using var fixture = new TransportFixture("valid");
        using var runtime = new PlaneGcsNodeRuntime(NodeExecutable(), fixture.HostPath, Path.Combine(fixture.DirectoryPath, "absent.mjs"));
        var document = StableDocument();
        var result = new PlaneGcsSketchSolverAdapter(runtime).Solve(SketchSolveRequest.FromDocument(document));
        AssertUnavailableUnchanged(document, result);
        result.Diagnostics.Should().Contain(message => message.Contains("assets", StringComparison.OrdinalIgnoreCase));
        File.Exists(fixture.RunsPath).Should().BeFalse("missing assets must not start a child process");
    }

    [Theory]
    [InlineData("malformed-json")]
    [InlineData("wrong-id")]
    [InlineData("empty-result")]
    public void TransportRejectsBrokenFrameAndRestartsCleanly(string mode)
    {
        using var fixture = new TransportFixture(mode);
        using var runtime = new PlaneGcsNodeRuntime(NodeExecutable(), fixture.HostPath, fixture.ModulePath);
        var solver = new PlaneGcsSketchSolverAdapter(runtime);
        var document = StableDocument();

        AssertUnavailableUnchanged(document, solver.Solve(SketchSolveRequest.FromDocument(document)));
        var firstPid = fixture.ProcessIds().Should().ContainSingle().Which;
        AssertExited(firstPid);

        var recovered = solver.Solve(SketchSolveRequest.FromDocument(document));
        recovered.Status.Should().Be(SketchSolveStatus.Solved, string.Join(" ", recovered.Diagnostics));
        recovered.Document.Entities.Should().Equal(document.Entities);
        fixture.ProcessIds().Should().HaveCount(2);
        runtime.Dispose();
        foreach (var pid in fixture.ProcessIds()) AssertExited(pid);
    }

    [Fact]
    public void TransportTimeoutKillsOwnedHostAndNextRequestStartsFresh()
    {
        using var fixture = new TransportFixture("timeout");
        using var runtime = new PlaneGcsNodeRuntime(NodeExecutable(), fixture.HostPath, fixture.ModulePath, timeoutMilliseconds: 1500);
        var solver = new PlaneGcsSketchSolverAdapter(runtime);
        var document = StableDocument();
        var watch = Stopwatch.StartNew();
        var failed = solver.Solve(SketchSolveRequest.FromDocument(document));
        watch.Stop();
        AssertUnavailableUnchanged(document, failed);
        failed.Diagnostics.Should().Contain(message => message.Contains("timed out", StringComparison.OrdinalIgnoreCase));
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
        AssertExited(fixture.ProcessIds().Should().ContainSingle().Which);

        var recovered = solver.Solve(SketchSolveRequest.FromDocument(document));
        recovered.Status.Should().Be(SketchSolveStatus.Solved, string.Join(" ", recovered.Diagnostics));
        recovered.Document.Entities.Should().Equal(document.Entities);
        fixture.ProcessIds().Should().HaveCount(2);
        runtime.Dispose();
        foreach (var pid in fixture.ProcessIds()) AssertExited(pid);
    }

    private static DrawingDocument StableDocument() => new([new LineEntity(EntityId.Create("edge"), new(0, 0), new(4, 0))]);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SynchronousSolveNeverPostsTransportContinuationToCallingSynchronizationContext(bool timeout)
    {
        using var fixture = new TransportFixture(timeout ? "timeout" : "delayed");
        using var runtime = new PlaneGcsNodeRuntime(NodeExecutable(), fixture.HostPath, fixture.ModulePath, timeoutMilliseconds: 1500);
        var document = StableDocument();
        var solver = new PlaneGcsSketchSolverAdapter(runtime);
        var context = new CaptureDetectingSynchronizationContext();
        var previous = SynchronizationContext.Current;
        SketchSolveResult result;
        var watch = Stopwatch.StartNew();
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            result = solver.Solve(SketchSolveRequest.FromDocument(document));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
        watch.Stop();

        context.PostCount.Should().Be(0, "synchronous Blazor callers cannot pump transport continuations while waiting for Solve");
        watch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
        if (timeout)
        {
            AssertUnavailableUnchanged(document, result);
            result.Diagnostics.Should().Contain(message => message.Contains("timed out", StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            result.Status.Should().Be(SketchSolveStatus.Solved, string.Join(" ", result.Diagnostics));
            result.Document.Entities.Should().Equal(document.Entities);
        }
        runtime.Dispose();
        foreach (var pid in fixture.ProcessIds()) AssertExited(pid);
    }

    private sealed class CaptureDetectingSynchronizationContext : SynchronizationContext
    {
        private int _posts;
        public int PostCount => Volatile.Read(ref _posts);

        public override void Post(SendOrPostCallback callback, object? state)
        {
            Interlocked.Increment(ref _posts);
            // Dispatch unexpected captures so a regression fails PostCount instead
            // of deadlocking the test runner like a blocked Blazor circuit would.
            ThreadPool.QueueUserWorkItem(_ => callback(state));
        }
    }

    private static void AssertUnavailableUnchanged(DrawingDocument original, SketchSolveResult result)
    {
        result.Status.Should().Be(SketchSolveStatus.Unavailable);
        result.Document.Should().BeSameAs(original);
        result.Diagnostics.Should().NotBeEmpty();
    }

    private static void AssertExited(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            process.HasExited.Should().BeTrue($"owned transport host {pid} must be stopped");
        }
        catch (ArgumentException) { }
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "src/DXFER.Web/Solver/planeGcsHost.mjs")))
                return directory.FullName;
        throw new InvalidOperationException("PlaneGCS integration tests require the repository host and vendored runtime assets.");
    }

    private static string NodeExecutable()
    {
        var name = OperatingSystem.IsWindows() ? "node.exe" : "node";
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory)) continue;
            var candidate = Path.Combine(directory, name);
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }
        throw new InvalidOperationException("Node is required for PlaneGCS transport integration; no mock or legacy fallback is permitted.");
    }

    private sealed class ObservedRuntime(IPlaneGcsRuntime inner) : IPlaneGcsRuntime
    {
        public PlaneGcsResponse? LastResponse { get; private set; }
        public PlaneGcsResponse Solve(PlaneGcsRequest request) => LastResponse = inner.Solve(request);
    }

    // Deliberately synthetic framing hosts. These exercise transport recovery only,
    // never serve as evidence that a geometric problem was solved by PlaneGCS.
    private sealed class TransportFixture : IDisposable
    {
        public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "dxfer-planegcs-transport-" + Guid.NewGuid().ToString("N"));
        public string HostPath => Path.Combine(DirectoryPath, "host.mjs");
        public string ModulePath => Path.Combine(DirectoryPath, "unused-module.mjs");
        public string RunsPath => Path.Combine(DirectoryPath, "runs.json");

        public TransportFixture(string mode)
        {
            Directory.CreateDirectory(DirectoryPath);
            File.WriteAllText(ModulePath, "export {};\n");
            File.WriteAllText(HostPath, $$"""
                import { createInterface } from 'node:readline';
                import { existsSync, readFileSync, writeFileSync } from 'node:fs';
                const mode = {{JsonSerializer.Serialize(mode)}};
                const path = new URL('./runs.json', import.meta.url);
                const runs = existsSync(path) ? JSON.parse(readFileSync(path, 'utf8')) : [];
                runs.push(process.pid);
                writeFileSync(path, JSON.stringify(runs));
                const first = runs.length === 1;
                for await (const line of createInterface({ input: process.stdin, crlfDelay: Infinity })) {
                    const message = JSON.parse(line);
                    if (first && mode === 'timeout') continue;
                    if (mode === 'delayed') await new Promise(resolve => setTimeout(resolve, 100));
                    if (first && mode === 'malformed-json') { process.stdout.write('not-json\n'); continue; }
                    const result = first && mode === 'empty-result' ? null : {
                        status: 'solved', engine: 'PlaneGCS', entities: message.request.document.entities,
                        diagnostics: ['Synthetic transport framing fixture; not engine acceptance.']
                    };
                    const id = first && mode === 'wrong-id' ? message.id + 100 : message.id;
                    process.stdout.write(JSON.stringify({ id, result }) + '\n');
                }
                """);
        }

        public int[] ProcessIds() => JsonSerializer.Deserialize<int[]>(File.ReadAllText(RunsPath))!;
        public void Dispose() => Directory.Delete(DirectoryPath, recursive: true);
    }
}

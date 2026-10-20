using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace DXFER.Core.Sketching;

public sealed class PlaneGcsNodeRuntime : IPlaneGcsRuntime, IDisposable
{
    private const int MessageLimit = 8 * 1024 * 1024;
    private const int StderrLimit = 1024 * 1024;
    private static readonly SemaphoreSlim ProcessSlots = new(8, 8);
    // Keep failed terminations owned, even after the circuit has disposed its runtime.
    private static readonly ConcurrentDictionary<PlaneGcsNodeRuntime, byte> ProcessOwners = new();
    private static readonly Timer CleanupTimer = CreateCleanupTimer();
    private static int _cleanupRunning;
    private readonly object _gate = new();
    private readonly string _nodeExecutable;
    private readonly string _hostPath;
    private readonly string _modulePath;
    private readonly int _timeoutMilliseconds;
    private readonly int _idleTimeoutMilliseconds;
    private Process? _process;
    private CancellationTokenSource? _processCancellation;
    private Task? _stderrDrain;
    private bool _stopRequested;
    private long _lastActivity;
    private long _sequence;
    private bool _disposed;

    public PlaneGcsNodeRuntime(string nodeExecutable, string hostPath, string modulePath,
        int timeoutMilliseconds = 5000, int idleTimeoutMilliseconds = 60000)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeExecutable);
        ArgumentException.ThrowIfNullOrWhiteSpace(hostPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(modulePath);
        if (timeoutMilliseconds is < 100 or > 30000)
            throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
        if (idleTimeoutMilliseconds is < 100 or > 3600000)
            throw new ArgumentOutOfRangeException(nameof(idleTimeoutMilliseconds));
        _nodeExecutable = Path.GetFullPath(nodeExecutable);
        _hostPath = Path.GetFullPath(hostPath);
        _modulePath = Path.GetFullPath(modulePath);
        _timeoutMilliseconds = timeoutMilliseconds;
        _idleTimeoutMilliseconds = idleTimeoutMilliseconds;
    }

    public PlaneGcsResponse Solve(PlaneGcsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Monitor.TryEnter(_gate, _timeoutMilliseconds))
            throw new TimeoutException("PlaneGCS is busy. Geometry unchanged.");
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var id = ++_sequence;
            var message = new FrameBuffer();
            using (var writer = new Utf8JsonWriter(message))
                JsonSerializer.Serialize(writer, new { id, request });
            EnsureStarted();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_processCancellation!.Token);
            timeout.CancelAfter(_timeoutMilliseconds);
            return ExchangeAsync(id, message.WrittenMemory, timeout.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            StopProcess();
            throw new TimeoutException("PlaneGCS timed out or exceeded its output budget. Geometry unchanged.");
        }
        catch
        {
            // A broken frame or crashed module must never contaminate the next solve.
            StopProcess();
            throw;
        }
        finally
        {
            _lastActivity = Environment.TickCount64;
            Monitor.Exit(_gate);
        }
    }

    private void EnsureStarted()
    {
        if (_process is { HasExited: false } && !_stopRequested && !_processCancellation!.IsCancellationRequested) return;
        if (!StopProcess())
            throw new InvalidOperationException("PlaneGCS is still stopping. Geometry unchanged.");
        if (!File.Exists(_hostPath) || !File.Exists(_modulePath))
            throw new FileNotFoundException("PlaneGCS runtime assets are unavailable.");
        if (!ProcessSlots.Wait(0))
            throw new InvalidOperationException("PlaneGCS process capacity is busy. Geometry unchanged.");
        ProcessOwners.TryAdd(this, 0);
        GC.KeepAlive(CleanupTimer);
        var start = new ProcessStartInfo
        {
            FileName = _nodeExecutable,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(_modulePath)!
        };
        start.ArgumentList.Add("--max-old-space-size=128");
        // 4096 x 64 KiB = 256 MiB per WASM memory, separate from V8's JS heap.
        start.ArgumentList.Add("--wasm-max-mem-pages=4096");
        // Explicit ESM for the shared .js mapper; supported by Node 20.10+ / 22.
        start.ArgumentList.Add("--experimental-default-type=module");
        start.ArgumentList.Add(_hostPath);
        start.ArgumentList.Add(_modulePath);
        // Solver code needs no OAuth, database, callback or application credentials.
        start.Environment.Clear();
        start.Environment["LANG"] = "C.UTF-8";
        var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start()) throw new InvalidOperationException("PlaneGCS runtime could not start.");
        }
        catch
        {
            process.Dispose();
            ReleaseSlot();
            throw;
        }
        _process = process;
        _stopRequested = false;
        _processCancellation = new CancellationTokenSource();
        _stderrDrain = DrainStderrAsync(process.StandardError.BaseStream, _processCancellation);
    }

    private async Task<PlaneGcsResponse> ExchangeAsync(long id, ReadOnlyMemory<byte> message, CancellationToken token)
    {
        var process = _process!;
        // Solve is synchronous; never capture a caller's Blazor synchronization context.
        await process.StandardInput.BaseStream.WriteAsync(message, token).ConfigureAwait(false);
        await process.StandardInput.BaseStream.WriteAsync(new byte[] { (byte)'\n' }, token).ConfigureAwait(false);
        await process.StandardInput.BaseStream.FlushAsync(token).ConfigureAwait(false);
        var response = await ReadFrameAsync(process.StandardOutput.BaseStream, token).ConfigureAwait(false);
        using var parsed = JsonDocument.Parse(response);
        if (!parsed.RootElement.TryGetProperty("id", out var responseId)
            || !responseId.TryGetInt64(out var actualId) || actualId != id
            || !parsed.RootElement.TryGetProperty("result", out var result))
            throw new InvalidOperationException("PlaneGCS response does not match this operation.");
        return result.Deserialize<PlaneGcsResponse>()
            ?? throw new InvalidOperationException("PlaneGCS returned an empty result.");
    }

    private static async Task<ReadOnlyMemory<byte>> ReadFrameAsync(Stream stream, CancellationToken token)
    {
        var frame = new FrameBuffer();
        var chunk = new byte[4096];
        while (true)
        {
            var count = await stream.ReadAsync(chunk.AsMemory(), token).ConfigureAwait(false);
            if (count == 0) throw new InvalidOperationException("PlaneGCS returned an incomplete response.");
            var newline = Array.IndexOf(chunk, (byte)'\n', 0, count);
            var length = newline < 0 ? count : newline;
            frame.Append(chunk.AsSpan(0, length));
            if (newline < 0) continue;
            if (newline != count - 1)
                throw new InvalidOperationException("PlaneGCS returned unsolicited output.");
            return frame.WrittenMemory;
        }
    }

    private static async Task DrainStderrAsync(Stream stream, CancellationTokenSource lifetime)
    {
        var chunk = new byte[4096];
        var remaining = StderrLimit;
        try
        {
            while (true)
            {
                var count = await stream.ReadAsync(chunk.AsMemory(), lifetime.Token).ConfigureAwait(false);
                if (count == 0) return;
                remaining -= count;
                if (remaining >= 0) continue;
                lifetime.Cancel();
                return;
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { lifetime.Cancel(); }
        catch (ObjectDisposedException) { lifetime.Cancel(); }
    }

    private bool StopProcess()
    {
        var process = _process;
        if (process is null) return true;
        _stopRequested = true;
        _processCancellation!.Cancel();
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            if (!process.WaitForExit(1000)) return false;
        }
        catch (InvalidOperationException) { return false; }
        catch (System.ComponentModel.Win32Exception) { return false; }
        process.Dispose();
        // The drain uses no caller context and must finish before its token source is disposed.
        _stderrDrain!.GetAwaiter().GetResult();
        _processCancellation.Dispose();
        _stderrDrain = null;
        _processCancellation = null;
        _process = null;
        ReleaseSlot();
        return true;
    }

    private void ReleaseSlot()
    {
        if (ProcessOwners.TryRemove(this, out _)) ProcessSlots.Release();
    }

    private static Timer CreateCleanupTimer()
    {
        // A global reaper must not retain the first circuit's execution context.
        if (ExecutionContext.IsFlowSuppressed())
            return new Timer(_ => CleanupIdleProcesses(), null, 1000, 1000);
        using (ExecutionContext.SuppressFlow())
            return new Timer(_ => CleanupIdleProcesses(), null, 1000, 1000);
    }

    private static void CleanupIdleProcesses()
    {
        if (Interlocked.Exchange(ref _cleanupRunning, 1) != 0) return;
        try
        {
            foreach (var owner in ProcessOwners.Keys)
            {
                if (!Monitor.TryEnter(owner._gate)) continue;
                try
                {
                    if (owner._disposed || owner._stopRequested
                        || owner._processCancellation?.IsCancellationRequested == true
                        || owner._process is { HasExited: true }
                        || Environment.TickCount64 - owner._lastActivity >= owner._idleTimeoutMilliseconds)
                        owner.StopProcess();
                }
                finally { Monitor.Exit(owner._gate); }
            }
        }
        finally { Volatile.Write(ref _cleanupRunning, 0); }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            StopProcess();
        }
    }

    private sealed class FrameBuffer : IBufferWriter<byte>
    {
        private byte[] _buffer = new byte[4096];
        private int _length;
        public ReadOnlyMemory<byte> WrittenMemory => _buffer.AsMemory(0, _length);

        public void Advance(int count)
        {
            if (count < 0 || count > _buffer.Length - _length)
                throw new ArgumentOutOfRangeException(nameof(count));
            _length += count;
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            EnsureCapacity(sizeHint);
            return _buffer.AsMemory(_length);
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            EnsureCapacity(sizeHint);
            return _buffer.AsSpan(_length);
        }

        public void Append(ReadOnlySpan<byte> bytes)
        {
            if (bytes.IsEmpty) return;
            bytes.CopyTo(GetSpan(bytes.Length));
            Advance(bytes.Length);
        }

        private void EnsureCapacity(int sizeHint)
        {
            sizeHint = Math.Max(1, sizeHint);
            if (sizeHint > MessageLimit - _length)
                throw new InvalidOperationException("PlaneGCS frame exceeds the message limit.");
            if (sizeHint > _buffer.Length - _length)
                Array.Resize(ref _buffer, Math.Min(MessageLimit, Math.Max(_length + sizeHint, _buffer.Length * 2)));
        }
    }
}

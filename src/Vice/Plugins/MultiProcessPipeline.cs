using System.Diagnostics;
using System.Threading.Channels;
using Vice.Commands;
using Vice.Execution;
using Vice.Logging;

namespace Vice.Plugins;

internal static class MultiProcessPipeline
{
    private const int FAN_QUEUE_CAPACITY = 16;

    public static async Task<int> RunAsync(
        string appName,
        IReadOnlyList<RawArgsSplitter.Segment> segments,
        ICommandRegistry registry,
        CancellationToken ct)
    {
        if (segments.Count < 2)
        {
            throw new ArgumentException("MultiProcessPipeline requires at least 2 segments");
        }

        var resolution = ResolveSegments(appName, segments, registry);
        if (resolution.Error is int resolveError)
        {
            return resolveError;
        }

        var resolved = resolution.Resolved!;
        var topology = BuildTopology(segments);

        var procs = new Process[segments.Count];
        int started = 0;
        try
        {
            var startError = StartProcesses(segments.Count, resolved, topology, procs, ref started);
            if (startError is int startCode)
            {
                return startCode;
            }

            var pumps = WirePumps(segments.Count, topology, procs, ct);

            using var registration = ct.Register(() => KillAll(procs));

            await Task.WhenAll(pumps).ConfigureAwait(false);

            var waits = new Task[segments.Count];
            for (int i = 0; i < segments.Count; i++)
            {
                waits[i] = procs[i].WaitForExitAsync(ct);
            }

            await Task.WhenAll(waits).ConfigureAwait(false);

            return FirstNonZeroExitCode(procs);
        }
        catch (OperationCanceledException)
        {
            return ViceExitCode.INTERRUPTED;
        }
        finally
        {
            DisposeProcesses(procs, started);
        }
    }

    private static SegmentResolution ResolveSegments(
        string appName,
        IReadOnlyList<RawArgsSplitter.Segment> segments,
        ICommandRegistry registry)
    {
        var resolved = new ResolvedSegment[segments.Count];
        for (int i = 0; i < segments.Count; i++)
        {
            var seg = segments[i];
            if (seg.Args.Length == 0)
            {
                Console.Error.WriteLine($"Pipeline segment {i + 1}: empty");
                return SegmentResolution.Failed(2);
            }

            var verb = seg.Args[0];
            string filePath;
            string[] procArgs;
            if (registry.FindByVerb(verb) is not null)
            {
                filePath = SelfExePath();
                procArgs = seg.Args;
            }
            else if (PluginDispatcher.TryFindOnPath($"{appName}-{verb}", out var pluginPath))
            {
                filePath = pluginPath;
                procArgs = seg.Args.AsSpan(1).ToArray();
            }
            else
            {
                Console.Error.WriteLine($"Pipeline segment {i + 1}: unknown verb '{verb}' (not in registry, not on PATH as '{appName}-{verb}')");
                return SegmentResolution.Failed(127);
            }

            resolved[i] = new ResolvedSegment(filePath, procArgs);
        }

        return SegmentResolution.Succeeded(resolved);
    }

    private static PipelineTopology BuildTopology(IReadOnlyList<RawArgsSplitter.Segment> segments)
    {
        var upstreamIdx = new int[segments.Count];
        upstreamIdx[0] = -1;
        for (int i = 1; i < segments.Count; i++)
        {
            upstreamIdx[i] = segments[i].OperatorWord is "fan"
                ? upstreamIdx[i - 1]
                : i - 1;
        }

        var downstreams = new List<int>[segments.Count];
        for (int i = 0; i < segments.Count; i++)
        {
            downstreams[i] = new List<int>();
        }

        for (int i = 0; i < segments.Count; i++)
        {
            if (upstreamIdx[i] >= 0)
            {
                downstreams[upstreamIdx[i]].Add(i);
            }
        }

        return new PipelineTopology(upstreamIdx, downstreams);
    }

    private static int? StartProcesses(
        int count,
        ResolvedSegment[] resolved,
        PipelineTopology topology,
        Process[] procs,
        ref int started)
    {
        for (int i = 0; i < count; i++)
        {
            var psi = new ProcessStartInfo
            {
                FileName = resolved[i].FileName,
                UseShellExecute = false,
                RedirectStandardInput = topology.UpstreamIdx[i] >= 0,
                RedirectStandardOutput = topology.Downstreams[i].Count > 0,
            };
            foreach (var a in resolved[i].Args)
            {
                psi.ArgumentList.Add(a);
            }

            var p = new Process { StartInfo = psi };
            if (!p.Start())
            {
                Console.Error.WriteLine($"Pipeline segment {i + 1}: failed to start '{resolved[i].FileName}'");
                return 127;
            }

            procs[i] = p;
            started = i + 1;
        }

        return null;
    }

    private static List<Task> WirePumps(
        int count,
        PipelineTopology topology,
        Process[] procs,
        CancellationToken ct)
    {
        var pumps = new List<Task>(count);
        for (int i = 0; i < count; i++)
        {
            if (topology.Downstreams[i].Count == 0)
            {
                continue;
            }

            var src = procs[i].StandardOutput.BaseStream;
            var dsts = topology.Downstreams[i].Select(j => procs[j].StandardInput.BaseStream).ToArray();
            pumps.Add(dsts.Length == 1
                ? PumpOneAsync(src, dsts[0], ct)
                : PumpFanAsync(src, dsts, ct));
        }

        return pumps;
    }

    private static void KillAll(Process[] procs)
    {
        foreach (var p in procs)
        {
            try
            {
                if (p is not null && !p.HasExited)
                {
                    p.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
            {
                Debug.WriteLine(ex);
            }
        }
    }

    private static int FirstNonZeroExitCode(Process[] procs)
    {
        for (int i = procs.Length - 1; i >= 0; i--)
        {
            if (procs[i].ExitCode != 0)
            {
                return procs[i].ExitCode;
            }
        }

        return 0;
    }

    private static void DisposeProcesses(Process[] procs, int started)
    {
        for (int i = 0; i < started; i++)
        {
            try
            {
                procs[i]?.Dispose();
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
            {
                Vice.Log.Emit(ViceLogLevel.Warn, $"pipeline segment {i + 1} process dispose failed", ex);
            }
        }
    }

    private static async Task PumpOneAsync(Stream src, Stream dst, CancellationToken ct)
    {
        var buf = new byte[65536];
        try
        {
            int read;
            while ((read = await src.ReadAsync(buf.AsMemory(0, buf.Length), ct).ConfigureAwait(false)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, read), ct).ConfigureAwait(false);
                await dst.FlushAsync(ct).ConfigureAwait(false);
            }
        }
        catch (IOException ex)
        {
            Vice.Log.Emit(ViceLogLevel.Warn, "pipeline stream pump failed", ex);
        }
        catch (OperationCanceledException ex)
        {
            Debug.WriteLine(ex);
        }
        finally
        {
            await CloseDownstreamAsync(dst).ConfigureAwait(false);
        }
    }

    private static async Task CloseDownstreamAsync(Stream dst)
    {
        try
        {
            await dst.FlushAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            Debug.WriteLine(ex);
        }

        try
        {
            dst.Close();
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            Debug.WriteLine(ex);
        }
    }

    private static async Task PumpFanAsync(Stream src, Stream[] dsts, CancellationToken ct)
    {
        var queues = new Channel<byte[]?>[dsts.Length];
        for (int i = 0; i < dsts.Length; i++)
        {
            queues[i] = Channel.CreateBounded<byte[]?>(new BoundedChannelOptions(FAN_QUEUE_CAPACITY)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait,
            });
        }

        var writers = new Task[dsts.Length];
        for (int i = 0; i < dsts.Length; i++)
        {
            var qi = queues[i];
            var di = dsts[i];
            writers[i] = Task.Run(async () =>
            {
                try
                {
                    await foreach (var chunk in qi.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                    {
                        if (chunk is null)
                        {
                            break;
                        }

                        await di.WriteAsync(chunk, ct).ConfigureAwait(false);
                        await di.FlushAsync(ct).ConfigureAwait(false);
                    }
                }
                catch (IOException ex)
                {
                    Vice.Log.Emit(ViceLogLevel.Warn, "pipeline fan-out downstream write failed", ex);
                }
                catch (OperationCanceledException ex)
                {
                    Debug.WriteLine(ex);
                }
                finally
                {
                    await CloseDownstreamAsync(di).ConfigureAwait(false);
                }
            }, ct);
        }

        var buf = new byte[65536];
        try
        {
            int read;
            while ((read = await src.ReadAsync(buf.AsMemory(0, buf.Length), ct).ConfigureAwait(false)) > 0)
            {
                var chunk = new byte[read];
                Buffer.BlockCopy(buf, 0, chunk, 0, read);
                var dispatch = new Task[queues.Length];
                for (int i = 0; i < queues.Length; i++)
                {
                    dispatch[i] = queues[i].Writer.WriteAsync(chunk, ct).AsTask();
                }

                await Task.WhenAll(dispatch).ConfigureAwait(false);
            }
        }
        catch (IOException ex)
        {
            Vice.Log.Emit(ViceLogLevel.Warn, "pipeline fan-out source read failed", ex);
        }
        catch (OperationCanceledException ex)
        {
            Debug.WriteLine(ex);
        }
        finally
        {
            for (int i = 0; i < queues.Length; i++)
            {
                queues[i].Writer.TryComplete();
            }
        }

        await Task.WhenAll(writers).ConfigureAwait(false);
    }

    private static string SelfExePath()
        => Environment.ProcessPath ?? throw new InvalidOperationException("Environment.ProcessPath is null");

    private sealed record ResolvedSegment(string FileName, string[] Args);

    private readonly record struct PipelineTopology(int[] UpstreamIdx, List<int>[] Downstreams);

    private readonly record struct SegmentResolution(ResolvedSegment[]? Resolved, int? Error)
    {
        public static SegmentResolution Succeeded(ResolvedSegment[] resolved) => new(resolved, null);

        public static SegmentResolution Failed(int error) => new(null, error);
    }
}

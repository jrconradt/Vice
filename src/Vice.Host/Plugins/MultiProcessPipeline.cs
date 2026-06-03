using System.Buffers;
using System.Diagnostics;
using System.Threading.Channels;
using Vice.Commands;
using Vice.Contracts;
using Vice.Foundation.Execution;
using Vice.Logging;
using Vice.Options;

namespace Vice.Plugins;

internal static class MultiProcessPipeline
{
    private const int FAN_QUEUE_CAPACITY = 16;

    public static async Task<int> RunAsync(
        string appName,
        IReadOnlyList<RawArgsSplitter.Segment> segments,
        ICommandRegistry registry,
        IViceLogger logger,
        CancellationToken ct)
    {
        if (segments.Count < 2)
        {
            throw new ArgumentException("MultiProcessPipeline requires at least 2 segments");
        }

        var locale = ExtractLocale(segments);
        var hoisted = locale is null
            ? segments
            : StripLocale(segments);

        var resolution = ResolveSegments(appName, hoisted, registry, logger);
        if (resolution.Error is int resolveError)
        {
            return resolveError;
        }

        var resolved = resolution.Resolved!;
        var topology = BuildTopology(hoisted);

        var procs = new Process[hoisted.Count];
        int started = 0;
        try
        {
            var startError = StartProcesses(hoisted.Count, resolved, topology, locale, procs, ref started);
            if (startError is int startCode)
            {
                KillAll(procs, logger);
                return startCode;
            }

            var pumps = WirePumps(hoisted.Count, topology, procs, logger, ct);

            using var registration = ct.Register(() => KillAll(procs, logger));

            await Task.WhenAll(pumps).ConfigureAwait(false);

            var waits = new Task[hoisted.Count];
            for (int i = 0; i < hoisted.Count; i++)
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
            DisposeProcesses(procs, started, logger);
        }
    }

    private static SegmentResolution ResolveSegments(
        string appName,
        IReadOnlyList<RawArgsSplitter.Segment> segments,
        ICommandRegistry registry,
        IViceLogger logger)
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
            else if (PluginDispatcher.TryFindOnPath($"{appName}-{verb}", logger, out var pluginPath))
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
            upstreamIdx[i] = string.Equals(segments[i].OperatorWord, "fan", StringComparison.OrdinalIgnoreCase)
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

    private static string? ExtractLocale(IReadOnlyList<RawArgsSplitter.Segment> segments)
    {
        var name = new LocaleOption().Name;
        var inline = $"--{name}=";
        var flag = $"--{name}";
        foreach (var seg in segments)
        {
            var args = seg.Args;
            for (int i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                if (arg.StartsWith(inline, StringComparison.OrdinalIgnoreCase))
                {
                    return arg[inline.Length..];
                }

                if (string.Equals(arg, flag, StringComparison.OrdinalIgnoreCase)
                    && i + 1 < args.Length)
                {
                    return args[i + 1];
                }
            }
        }

        return null;
    }

    private static IReadOnlyList<RawArgsSplitter.Segment> StripLocale(IReadOnlyList<RawArgsSplitter.Segment> segments)
    {
        var name = new LocaleOption().Name;
        var inline = $"--{name}=";
        var flag = $"--{name}";
        var result = new RawArgsSplitter.Segment[segments.Count];
        for (int s = 0; s < segments.Count; s++)
        {
            var seg = segments[s];
            var kept = new List<string>(seg.Args.Length);
            for (int i = 0; i < seg.Args.Length; i++)
            {
                var arg = seg.Args[i];
                if (arg.StartsWith(inline, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (string.Equals(arg, flag, StringComparison.OrdinalIgnoreCase)
                    && i + 1 < seg.Args.Length)
                {
                    i++;
                    continue;
                }

                kept.Add(arg);
            }

            result[s] = new RawArgsSplitter.Segment(kept.ToArray(), seg.OperatorWord);
        }

        return result;
    }

    private static int? StartProcesses(
        int count,
        ResolvedSegment[] resolved,
        PipelineTopology topology,
        string? locale,
        Process[] procs,
        ref int started)
    {
        var localeName = new LocaleOption().Name;
        for (int i = 0; i < count; i++)
        {
            var psi = new ProcessStartInfo
            {
                FileName = resolved[i].FileName,
                UseShellExecute = false,
                RedirectStandardInput = topology.UpstreamIdx[i] >= 0,
                RedirectStandardOutput = topology.Downstreams[i].Count > 0,
            };
            if (locale is not null)
            {
                psi.ArgumentList.Add($"--{localeName}");
                psi.ArgumentList.Add(locale);
            }

            foreach (var a in resolved[i].Args)
            {
                psi.ArgumentList.Add(a);
            }

            var process = new Process { StartInfo = psi };
            if (!process.Start())
            {
                Console.Error.WriteLine($"Pipeline segment {i + 1}: failed to start '{resolved[i].FileName}'");
                return 127;
            }

            procs[i] = process;
            started = i + 1;
        }

        return null;
    }

    private static List<Task> WirePumps(
        int count,
        PipelineTopology topology,
        Process[] procs,
        IViceLogger logger,
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
                ? PumpOneAsync(src, dsts[0], logger, ct)
                : PumpFanAsync(src, dsts, logger, ct));
        }

        return pumps;
    }

    private static void KillAll(Process[] procs, IViceLogger logger)
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
                Vice.Quietly.Swallow(ex, logger);
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

    private static void DisposeProcesses(Process[] procs, int started, IViceLogger logger)
    {
        for (int i = 0; i < started; i++)
        {
            try
            {
                procs[i]?.Dispose();
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
            {
                logger.Log(ViceLogLevel.Warn, $"pipeline segment {i + 1} process dispose failed", ex);
            }
        }
    }

    private static async Task PumpOneAsync(Stream src, Stream dst, IViceLogger logger, CancellationToken ct)
    {
        var buf = new byte[65536];
        try
        {
            int read;
            while ((read = await src.ReadAsync(buf.AsMemory(0, buf.Length), ct).ConfigureAwait(false)) > 0)
            {
                await dst.WriteAsync(buf.AsMemory(0, read), ct).ConfigureAwait(false);
            }
        }
        catch (IOException ex)
        {
            logger.Log(ViceLogLevel.Warn, "pipeline stream pump failed", ex);
        }
        catch (OperationCanceledException ex)
        {
            Vice.Quietly.Swallow(ex, logger);
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
            Vice.Quietly.Swallow(ex);
        }

        try
        {
            dst.Close();
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            Vice.Quietly.Swallow(ex);
        }
    }

    private static async Task PumpFanAsync(Stream src, Stream[] dsts, IViceLogger logger, CancellationToken ct)
    {
        var pool = ArrayPool<byte>.Shared;
        var queues = new Channel<FanChunk?>[dsts.Length];
        for (int i = 0; i < dsts.Length; i++)
        {
            queues[i] = Channel.CreateBounded<FanChunk?>(new BoundedChannelOptions(FAN_QUEUE_CAPACITY)
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

                        try
                        {
                            await di.WriteAsync(chunk.Memory, ct).ConfigureAwait(false);
                        }
                        finally
                        {
                            pool.Return(chunk.Buffer);
                        }
                    }
                }
                catch (IOException ex)
                {
                    logger.Log(ViceLogLevel.Warn, "pipeline fan-out downstream write failed", ex);
                }
                catch (OperationCanceledException ex)
                {
                    Vice.Quietly.Swallow(ex, logger);
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
                var dispatch = new Task[queues.Length];
                for (int i = 0; i < queues.Length; i++)
                {
                    var rented = pool.Rent(read);
                    Buffer.BlockCopy(buf,
                                     0,
                                     rented,
                                     0,
                                     read);
                    dispatch[i] = queues[i].Writer.WriteAsync(new FanChunk(rented, read), ct).AsTask();
                }

                await Task.WhenAll(dispatch).ConfigureAwait(false);
            }
        }
        catch (IOException ex)
        {
            logger.Log(ViceLogLevel.Warn, "pipeline fan-out source read failed", ex);
        }
        catch (OperationCanceledException ex)
        {
            Vice.Quietly.Swallow(ex, logger);
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

    private sealed record FanChunk(byte[] Buffer, int Length)
    {
        public ReadOnlyMemory<byte> Memory => new(Buffer,
                                                   0,
                                                   Length);
    }

    private sealed record ResolvedSegment(string FileName, string[] Args);

    private readonly record struct PipelineTopology(int[] UpstreamIdx, List<int>[] Downstreams);

    private readonly record struct SegmentResolution(ResolvedSegment[]? Resolved, int? Error)
    {
        public static SegmentResolution Succeeded(ResolvedSegment[] resolved) => new(resolved, null);

        public static SegmentResolution Failed(int error) => new(null, error);
    }
}

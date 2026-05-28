using System.Diagnostics;
using System.Threading.Channels;
using Vice.Commands;
using Vice.Execution;

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

        var resolved = new ResolvedSegment[segments.Count];
        for (int i = 0; i < segments.Count; i++)
        {
            var seg = segments[i];
            if (seg.Args.Length == 0)
            {
                Console.Error.WriteLine($"Pipeline segment {i + 1}: empty");
                return 2;
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
                return 127;
            }
            resolved[i] = new ResolvedSegment(filePath, procArgs);
        }

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

        var procs = new Process[segments.Count];
        int started = 0;
        try
        {
            for (int i = 0; i < segments.Count; i++)
            {
                var psi = new ProcessStartInfo
                {
                    FileName = resolved[i].FileName,
                    UseShellExecute = false,
                    RedirectStandardInput = upstreamIdx[i] >= 0,
                    RedirectStandardOutput = downstreams[i].Count > 0,
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

            var pumps = new List<Task>(segments.Count);
            for (int i = 0; i < segments.Count; i++)
            {
                if (downstreams[i].Count == 0)
                {
                    continue;
                }

                var src = procs[i].StandardOutput.BaseStream;
                var dsts = downstreams[i].Select(j => procs[j].StandardInput.BaseStream).ToArray();
                pumps.Add(dsts.Length == 1
                    ? PumpOneAsync(src, dsts[0], ct)
                    : PumpFanAsync(src, dsts, ct));
            }

            using var registration = ct.Register(() =>
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
            });

            await Task.WhenAll(pumps).ConfigureAwait(false);

            var waits = new Task[segments.Count];
            for (int i = 0; i < segments.Count; i++)
            {
                waits[i] = procs[i].WaitForExitAsync(ct);
            }

            await Task.WhenAll(waits).ConfigureAwait(false);

            for (int i = segments.Count - 1; i >= 0; i--)
            {
                if (procs[i].ExitCode != 0)
                {
                    return procs[i].ExitCode;
                }
            }

            return 0;
        }
        catch (OperationCanceledException)
        {
            return ViceExitCode.INTERRUPTED;
        }
        finally
        {
            for (int i = 0; i < started; i++)
            {
                try
                {
                    procs[i]?.Dispose();
                }
                catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
                {
                    Debug.WriteLine(ex);
                }
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
            Debug.WriteLine(ex);
        }
        catch (OperationCanceledException ex)
        {
            Debug.WriteLine(ex);
        }
        finally
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
                    Debug.WriteLine(ex);
                }
                catch (OperationCanceledException ex)
                {
                    Debug.WriteLine(ex);
                }
                finally
                {
                    try
                    {
                        await di.FlushAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is IOException or ObjectDisposedException)
                    {
                        Debug.WriteLine(ex);
                    }

                    try
                    {
                        di.Close();
                    }
                    catch (Exception ex) when (ex is IOException or ObjectDisposedException)
                    {
                        Debug.WriteLine(ex);
                    }
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
                for (int i = 0; i < queues.Length; i++)
                {
                    await queues[i].Writer.WriteAsync(chunk, ct).ConfigureAwait(false);
                }
            }
        }
        catch (IOException ex)
        {
            Debug.WriteLine(ex);
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
}

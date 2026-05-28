using System.Buffers;
using System.Collections;
using Vice.Execution;
using Vice.Mux.Sinks;
using Vice.Mux.Strategies;

namespace Vice.Mux.Commands;

internal static class MuxRunner
{
    public static async Task<int> RunAsync(
        CommandContext ctx, CancellationToken ct,
        StrategyRegistry strategies, bool requireN)
    {
        var strategyName = ctx.GetTarget("strategy") ?? throw new ArgumentException("missing {strategy}");
        if (!strategies.TryGet(strategyName, out var entry))
        {
            throw new ArgumentException($"unknown strategy '{strategyName}'; try `vice mux strategies`");
        }

        var specs = SinkSpec.Collect(ctx, "sinks");
        var nTarget = ctx.GetTarget("n");
        int sinkCount;
        if (requireN)
        {
            if (!int.TryParse(nTarget, out sinkCount) || sinkCount <= 0)
            {
                throw new ArgumentException("split: {n} must be a positive integer");
            }

            if (specs.Count != 0 && specs.Count != sinkCount)
            {
                throw new ArgumentException($"split: {{n}}={sinkCount} but {specs.Count} sinks provided");
            }

            if (specs.Count == 0)
            {
                specs = new List<string>(sinkCount);
                for (int i = 0; i < sinkCount; i++)
                {
                    specs.Add($"file:./mux-{i}.out");
                }
            }
        }
        else
        {
            sinkCount = specs.Count;
            if (sinkCount == 0)
            {
                throw new ArgumentException("route: at least one sink is required");
            }
        }

        var state = new RouteState
        {
            Seed = ParseSeed(ctx.GetGlobalOption("seed")),
            KeyOffset = ctx.GetGlobalOption("key-offset").AsPositiveInt() ?? 0,
            KeyLength = ctx.GetGlobalOption("key-length").AsPositiveInt() ?? 4,
        };
        entry.Configure?.Invoke(state, ctx.GetGlobalOption("strategy-arg"));

        var sinks = new ISink[sinkCount];
        for (int i = 0; i < sinkCount; i++)
        {
            sinks[i] = SinkFactory.Open(specs[i]);
        }

        var bufferSize = ctx.GetGlobalOption("chunk-size").AsPositiveInt() ?? 65536;
        var pool = ArrayPool<byte>.Shared;
        var buffer = pool.Rent(bufferSize);
        try
        {
            var stdin = Console.OpenStandardInput();
            int read;
            while ((read = await stdin.ReadAsync(buffer.AsMemory(0, bufferSize), ct)) > 0)
            {
                state.ChunkIndex++;
                state.ByteCount += read;
                var span = buffer.AsSpan(0, read);

                if (entry.Kind == StrategyKind.Broadcast)
                {
                    var mask = new BitArray(sinkCount);
                    entry.Broadcast!(span, sinkCount, state, mask);
                    var copy = pool.Rent(read);
                    try
                    {
                        Buffer.BlockCopy(buffer, 0, copy, 0, read);
                        var mem = new ReadOnlyMemory<byte>(copy, 0, read);
                        var writes = new List<Task>(sinkCount);
                        for (int i = 0; i < sinkCount; i++)
                        {
                            if (mask[i])
                            {
                                writes.Add(sinks[i].WriteAsync(mem, ct).AsTask());
                            }
                        }

                        await Task.WhenAll(writes);
                    }
                    finally
                    {
                        pool.Return(copy);
                    }
                }
                else
                {
                    var idx = entry.Route!(span, sinkCount, state);
                    if ((uint)idx >= (uint)sinkCount)
                    {
                        throw new InvalidOperationException($"strategy '{entry.Name}' returned out-of-range index {idx} for {sinkCount} sinks");
                    }

                    await sinks[idx].WriteAsync(buffer.AsMemory(0, read), ct);
                }
            }
            foreach (var s in sinks)
            {
                await s.FlushAsync(ct);
            }
        }
        finally
        {
            pool.Return(buffer);
            foreach (var s in sinks)
            {
                await s.DisposeAsync();
            }
        }
        return 0;
    }

    private static ulong ParseSeed(string? v)
        => (v is not null && ulong.TryParse(v, out var n)) ? n : 0;
}

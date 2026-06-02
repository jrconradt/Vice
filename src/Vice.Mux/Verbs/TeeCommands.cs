using System.Buffers;
using Vice.Composition;
using Vice.Contracts;
using Vice.Execution;
using Vice.Lexicon;
using Vice.Mux;
using Vice.Mux.Sinks;

namespace Vice.Mux.Commands;

[ViceCommandPack]
public static class TeeCommands
{
    public static void Register(IViceApp app)
    {
        app.Register(
            Verbs.Tee() > Connectors.To() * Targets.Sinks,
            "Read stdin, broadcast every chunk to every sink and to stdout",
            HandleAsync);
    }

    private static async Task<int> HandleAsync(CommandContext ctx, CancellationToken ct)
    {
        var specs = SinkSpec.Collect(ctx, "sinks");
        if (specs.Count == 0)
        {
            throw new ArgumentException("tee: at least one sink is required");
        }

        var sinks = new List<ISink>(specs.Count + 1);
        try
        {
            var opens = new Task<ISink>[specs.Count];
            for (int i = 0; i < specs.Count; i++)
            {
                opens[i] = SinkFactory.OpenAsync(specs[i], ct, ctx.Logger).AsTask();
            }

            sinks.AddRange(await Task.WhenAll(opens));
            sinks.Add(new StreamSink(Console.OpenStandardOutput(), "stdout", ctx.Logger));
        }
        catch
        {
            foreach (var s in sinks)
            {
                await s.DisposeAsync();
            }

            throw;
        }

        var bufferSize = ctx.GetGlobalOption("chunk-size").AsPositiveInt() ?? MuxDefaults.DefaultChunkSize;
        var pool = ArrayPool<byte>.Shared;
        var buffer = pool.Rent(bufferSize);
        try
        {
            var stdin = Console.OpenStandardInput();
            int read;
            while ((read = await stdin.ReadAsync(buffer.AsMemory(0, bufferSize), ct)) > 0)
            {
                var copy = pool.Rent(read);
                try
                {
                    Buffer.BlockCopy(buffer, 0, copy, 0, read);
                    var mem = new ReadOnlyMemory<byte>(copy, 0, read);
                    var writes = new Task[sinks.Count];
                    for (int i = 0; i < sinks.Count; i++)
                    {
                        writes[i] = sinks[i].WriteAsync(mem, ct).AsTask();
                    }

                    await Task.WhenAll(writes);
                }
                finally
                {
                    pool.Return(copy);
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
}

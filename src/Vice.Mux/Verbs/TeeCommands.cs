using System.Buffers;
using Vice.Composition;
using Vice.Contracts;
using Vice.Core;
using Vice.Execution;
using Vice.Lexicon;
using Vice.Logging;
using Vice.Mux;
using Vice.Mux.Sinks;

namespace Vice.Mux.Commands;

[ViceCommandPack]
public static class TeeCommands
{
    public static void Register(IViceApp app, TcpSinkConnector connectTcp)
    {
        app.Register(
            Verbs.Tee() > Connectors.To() * Targets.Sinks,
            "Read stdin, broadcast every chunk to every sink and to stdout",
            (ctx, ct) => HandleAsync(ctx, ct, connectTcp));
    }

    private static async Task<int> HandleAsync(CommandContext ctx, CancellationToken ct, TcpSinkConnector connectTcp)
    {
        var specs = SinkSpec.Collect(ctx, "sinks");
        if (specs.Count == 0)
        {
            throw new ArgumentException("tee: at least one sink is required");
        }

        var sinks = new List<ISink>(specs.Count + 1);
        var opens = new Task<ISink>[specs.Count];
        for (int i = 0; i < specs.Count; i++)
        {
            opens[i] = SinkFactory.OpenAsync(specs[i], ct, ctx.Logger, connectTcp).AsTask();
        }

        try
        {
            await Task.WhenAll(opens);
            sinks.Add(new StreamSink(Console.OpenStandardOutput(), "stdout", ctx.Logger));
            sinks.AddRange(opens.Select(open => open.Result));
        }
        catch
        {
            foreach (var s in sinks)
            {
                try
                {
                    await s.DisposeAsync();
                }
                catch (Exception disposeEx)
                {
                    Vice.Logging.Quietly.Swallow(disposeEx, ctx.Logger);
                }
            }

            foreach (var open in opens)
            {
                if (open.Status == TaskStatus.RanToCompletion)
                {
                    try
                    {
                        await open.Result.DisposeAsync();
                    }
                    catch (Exception disposeEx)
                    {
                        Vice.Logging.Quietly.Swallow(disposeEx, ctx.Logger);
                    }
                }
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
                    for (int i = sinks.Count - 1; i >= 0; i--)
                    {
                        if (!await WriteOneAsync(sinks[i], mem, ct, ctx.Logger))
                        {
                            sinks.RemoveAt(i);
                        }
                    }
                }
                finally
                {
                    pool.Return(copy);
                }

                if (sinks.Count == 0)
                {
                    break;
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
                try
                {
                    await s.DisposeAsync();
                }
                catch (Exception disposeEx)
                {
                    Vice.Logging.Quietly.Swallow(disposeEx, ctx.Logger);
                }
            }
        }
        return 0;
    }

    private static async Task<bool> WriteOneAsync(
        ISink sink,
        ReadOnlyMemory<byte> chunk,
        CancellationToken ct,
        IViceLogger logger)
    {
        try
        {
            await sink.WriteAsync(chunk, ct);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.Log(ViceLogLevel.Warn,
                       $"Sink '{sink.Label}' failed during write; dropping it and continuing with remaining sinks.",
                       ex);
            await sink.DisposeAsync();
            return false;
        }
    }
}

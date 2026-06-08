using System.Buffers;
using Vice.Logging;
using Vice.Mux.Sinks;

namespace Vice.Mux.Routing;

public static class Router
{
    public static async Task<int> RouteAsync(
        int code,
        IReadOnlyList<RouteClause> clauses,
        Stream input,
        int chunkSize,
        CancellationToken ct,
        IViceLogger logger,
        TcpSinkConnector? connectTcp = null)
    {
        var matched = new List<RouteClause>(clauses.Count);
        foreach (var clause in clauses)
        {
            if (clause.Condition.Matches(code))
            {
                matched.Add(clause);
            }
        }

        if (matched.Count == 0)
        {
            return 0;
        }

        var opens = new Task<ISink>[matched.Count];
        for (int i = 0; i < matched.Count; i++)
        {
            opens[i] = SinkFactory.OpenAsync(matched[i].SinkSpec, ct, logger, connectTcp).AsTask();
        }

        List<ISink> live;
        try
        {
            live = new List<ISink>(await Task.WhenAll(opens));
        }
        catch
        {
            foreach (var open in opens)
            {
                if (open.Status == TaskStatus.RanToCompletion)
                {
                    await open.Result.DisposeAsync();
                }
            }

            throw;
        }

        var pool = ArrayPool<byte>.Shared;
        var buffer = pool.Rent(chunkSize);
        try
        {
            int read;
            while ((read = await input.ReadAsync(buffer.AsMemory(0, chunkSize), ct)) > 0)
            {
                if (live.Count == 1)
                {
                    if (!await WriteOneAsync(live[0], buffer.AsMemory(0, read), ct, logger))
                    {
                        live.RemoveAt(0);
                    }
                }
                else
                {
                    var copy = pool.Rent(read);
                    try
                    {
                        Buffer.BlockCopy(buffer, 0, copy, 0, read);
                        var shared = new ReadOnlyMemory<byte>(copy, 0, read);
                        for (int i = live.Count - 1; i >= 0; i--)
                        {
                            if (!await WriteOneAsync(live[i], shared, ct, logger))
                            {
                                live.RemoveAt(i);
                            }
                        }
                    }
                    finally
                    {
                        pool.Return(copy);
                    }
                }

                if (live.Count == 0)
                {
                    break;
                }
            }

            foreach (var sink in live)
            {
                await sink.FlushAsync(ct);
            }
        }
        finally
        {
            pool.Return(buffer);
            foreach (var sink in live)
            {
                await sink.DisposeAsync();
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

using System.Buffers;
using Vice.Mux.Sinks;

namespace Vice.Mux.Routing;

public static class Router
{
    public static async Task<int> RouteAsync(
        int code,
        IReadOnlyList<RouteClause> clauses,
        Stream input,
        int chunkSize,
        CancellationToken ct)
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
            opens[i] = SinkFactory.OpenAsync(matched[i].SinkSpec, ct).AsTask();
        }

        var sinks = await Task.WhenAll(opens);

        var pool = ArrayPool<byte>.Shared;
        var buffer = pool.Rent(chunkSize);
        try
        {
            int read;
            while ((read = await input.ReadAsync(buffer.AsMemory(0, chunkSize), ct)) > 0)
            {
                if (sinks.Length == 1)
                {
                    await sinks[0].WriteAsync(buffer.AsMemory(0, read), ct);
                }
                else
                {
                    var copy = pool.Rent(read);
                    try
                    {
                        Buffer.BlockCopy(buffer, 0, copy, 0, read);
                        var shared = new ReadOnlyMemory<byte>(copy, 0, read);
                        var writes = new Task[sinks.Length];
                        for (int i = 0; i < sinks.Length; i++)
                        {
                            writes[i] = sinks[i].WriteAsync(shared, ct).AsTask();
                        }

                        await Task.WhenAll(writes);
                    }
                    finally
                    {
                        pool.Return(copy);
                    }
                }
            }

            foreach (var sink in sinks)
            {
                await sink.FlushAsync(ct);
            }
        }
        finally
        {
            pool.Return(buffer);
            foreach (var sink in sinks)
            {
                await sink.DisposeAsync();
            }
        }

        return 0;
    }
}

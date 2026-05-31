namespace Vice.Mux.Strategies;

internal static class BuiltinStrategies
{
    internal static void RegisterAll(StrategyRegistry r)
    {
        r.Register("roundrobin", "Cycle sinks in order, one chunk per sink",
            StrategyKind.Unicast,
            route: (chunk, n, s) =>
            {
                var idx = (int)(s.Cursor % (uint)n);
                s.Cursor = unchecked(s.Cursor + 1);
                return idx;
            });

        r.Register("hash", "FNV-1a 64 of the chunk, modulo sink count",
            StrategyKind.Unicast,
            route: (chunk, n, s) =>
            {
                var h = Fnv1a64(chunk, s.Seed);
                return (int)(h % (uint)n);
            });

        r.Register("random", "Uniform random sink per chunk (xorshift seeded by RouteState.Seed)",
            StrategyKind.Unicast,
            route: (chunk, n, s) =>
            {
                var x = s.Seed == 0 ? 0x9E3779B97F4A7C15UL : s.Seed;
                x ^= x << 13; x ^= x >> 7; x ^= x << 17;
                s.Seed = x;
                return (int)(x % (uint)n);
            });

        r.Register("broadcast", "Write every chunk to every sink",
            StrategyKind.Broadcast,
            broadcast: (chunk, n, s, mask) => mask.SetAll(true));

        r.Register("sticky-key", "FNV-1a over a fixed byte slice (RouteState.KeyOffset/KeyLength) modulo n",
            StrategyKind.Unicast,
            route: (chunk, n, s) =>
            {
                if (chunk.Length == 0)
                {
                    return 0;
                }

                var off = Math.Clamp(s.KeyOffset, 0, chunk.Length - 1);
                var len = Math.Clamp(s.KeyLength, 0, chunk.Length - off);
                var slice = chunk.Slice(off, len);
                var h = Fnv1a64(slice, s.Seed);
                return (int)(h % (uint)n);
            });

        r.Register("weighted", "Deterministic interleave by integer weights (RouteState.Weights, colon-separated e.g. 3:1)",
            StrategyKind.Unicast,
            route: (chunk, n, s) =>
            {
                var w = s.Weights.Span;
                if (w.Length == 0)
                {
                    return (int)(s.Cursor++ % (uint)n);
                }

                long total = 0;
                for (int i = 0; i < w.Length; i++)
                {
                    total += w[i];
                }

                if (total <= 0)
                {
                    return (int)(s.Cursor++ % (uint)n);
                }

                var tick = (long)(s.Cursor % (uint)total);
                s.Cursor = unchecked(s.Cursor + 1);
                long acc = 0;
                for (int i = 0; i < w.Length && i < n; i++)
                {
                    acc += w[i];
                    if (tick < acc)
                    {
                        return i;
                    }
                }
                return Math.Min(w.Length, n) - 1;
            },
            configure: (state, arg) =>
            {
                if (string.IsNullOrWhiteSpace(arg))
                {
                    return;
                }

                var parts = arg.Split(new[] { ':', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var ws = new int[parts.Length];
                for (int i = 0; i < parts.Length; i++)
                {
                    if (!int.TryParse(parts[i], out var v) || v < 0)
                    {
                        throw new ArgumentException($"weighted: weight '{parts[i]}' must be a non-negative integer");
                    }

                    ws[i] = v;
                }
                state.Weights = ws;
            });
    }

    private static ulong Fnv1a64(ReadOnlySpan<byte> data, ulong seed)
    {
        const ulong OFFSET = 14695981039346656037UL;
        const ulong PRIME = 1099511628211UL;
        var h = OFFSET ^ seed;
        for (int i = 0; i < data.Length; i++)
        {
            h ^= data[i];
            h *= PRIME;
        }
        return h;
    }
}

using Vice.Execution;
using Vice.Logging;
using Vice.Nodes;
using Vice.Parser;
using Vice.Streaming;

namespace Vice.Commands;

internal sealed class CommandRegistry : ICommandRegistry
{
    private readonly List<CommandRegistration> _registrations = new();
    private IReadOnlyList<CommandRegistration>? _userRegistrationsCache;
    private IReadOnlyList<CommandRegistration>? _helpVisibleCache;
    private IReadOnlyList<IChainDescriptor>? _descriptorsCache;
    private int _collisionsReported;

    public IReadOnlyList<CommandRegistration> Registrations => _registrations;

    public IReadOnlyList<CommandRegistration> UserRegistrations
    {
        get
        {
            var cached = _userRegistrationsCache;
            if (cached is not null)
            {
                return cached;
            }

            var built = _registrations.Where(r => !r.IsBuiltin).ToList();
            Interlocked.CompareExchange(ref _userRegistrationsCache, built, null);
            return _userRegistrationsCache!;
        }
    }

    public IReadOnlyList<CommandRegistration> HelpVisibleRegistrations
    {
        get
        {
            var cached = _helpVisibleCache;
            if (cached is not null)
            {
                return cached;
            }

            var built = _registrations.Where(r => r.ShowInHelp).ToList();
            Interlocked.CompareExchange(ref _helpVisibleCache, built, null);
            return _helpVisibleCache!;
        }
    }

    private void InvalidateCaches()
    {
        _userRegistrationsCache = null;
        _helpVisibleCache = null;
        _descriptorsCache = null;
        Interlocked.Exchange(ref _collisionsReported, 0);
    }

    public void Register(
        ChainNode chain,
        string description,
        Func<CommandContext, CancellationToken, Task<int>> handler,
        bool isBuiltin = false,
        bool? showInHelp = null)
    {
        _registrations.Add(new CommandRegistration(chain, description, handler, isBuiltin, showInHelp));
        InvalidateCaches();
    }

    public void RegisterPipeline(
        ChainNode chain,
        string description,
        Func<CommandContext, CancellationToken, Task<int>> defaultHandler,
        Dictionary<int, Func<CommandContext, CancellationToken, Task<int>>> stageHandlers,
        bool isBuiltin = false,
        bool? showInHelp = null)
    {
        _registrations.Add(new CommandRegistration(chain, description, defaultHandler, stageHandlers, isBuiltin, showInHelp));
        InvalidateCaches();
    }

    public void RegisterStreaming<T>(
        ChainNode chain,
        string description,
        Func<IStreamingCommandContext<T>, CancellationToken, Task<int>> handler,
        BatchOptions? options = null,
        Func<CommandContext, CancellationToken, Task<int>>? classicFallback = null,
        bool isBuiltin = false,
        bool? showInHelp = null)
    {
        var capacity = options?.ChannelCapacity ?? 100;
        var launcher = new StreamingLauncher<T>(producer: handler, consumer: null, defaultChannelCapacity: capacity);

        Func<CommandContext, CancellationToken, Task<int>> fallback = classicFallback ?? (async (ctx, ct) =>
        {
            var channel = new StreamChannel<T>(capacity);
            await using (channel)
            {
                var streamCtx = new StreamingCommandContext<T>(ctx, channel);
                var producerTask = handler(streamCtx, ct);
                string output;
                int exitCode;
                try
                {
                    output = await StreamBridge.DrainToStringAsync<T>(channel, ct);
                    exitCode = await producerTask;
                }
                catch
                {
                    try
                    {
                        _ = await producerTask.ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Vice.Log.Emit(ViceLogLevel.Warn, "streaming producer faulted during drain failure", ex);
                    }
                    throw;
                }
                ctx.Console.Write(output);
                return exitCode;
            }
        });

        _registrations.Add(new CommandRegistration(
            chain, description, fallback,
            StageMode.StreamProducer, options, launcher,
            isBuiltin, showInHelp));
        InvalidateCaches();
    }

    public void RegisterStreamConsumer<T>(
        ChainNode chain,
        string description,
        Func<IConsumingCommandContext<T>, CancellationToken, Task<int>> handler,
        BatchOptions? options = null,
        bool isBuiltin = false,
        bool? showInHelp = null)
    {
        var capacity = options?.ChannelCapacity ?? 100;
        var launcher = new StreamingLauncher<T>(producer: null, consumer: handler, defaultChannelCapacity: capacity);

        Func<CommandContext, CancellationToken, Task<int>> classicFallback = async (ctx, ct) =>
        {
            if (typeof(T) == typeof(byte[]) && ctx.PipelineInput is null && System.Console.IsInputRedirected)
            {
                var byteChannel = new StreamChannel<byte[]>(capacity);
                await using (byteChannel)
                {
                    var pumpTask = StreamBridge.PumpStdinAsync(byteChannel, ct);
                    var consumeCtx = new ConsumingCommandContext<T>(ctx, (IStreamInput<T>)(object)byteChannel);
                    var consumerTask = handler(consumeCtx, ct);
                    await pumpTask;
                    return await consumerTask;
                }
            }

            var channel = new StreamChannel<string>(capacity);
            await using (channel)
            {
                var pushTask = StreamBridge.PushStringAsStreamAsync(ctx.PipelineInput, channel, ct);

                if (typeof(T) == typeof(string))
                {
                    var consumeCtx = new ConsumingCommandContext<T>(ctx, (IStreamInput<T>)(object)channel);
                    var consumerTask = handler(consumeCtx, ct);
                    await pushTask;
                    return await consumerTask;
                }

                if (typeof(T) == typeof(byte[]))
                {
                    var byteChannel = new StreamChannel<byte[]>(capacity);
                    await using (byteChannel)
                    {
                        await foreach (var item in channel.ReadAllAsync(ct))
                        {
                            await byteChannel.YieldAsync(System.Text.Encoding.UTF8.GetBytes(item + "\n"), ct);
                        }

                        byteChannel.Complete();
                        var consumeCtx = new ConsumingCommandContext<T>(ctx, (IStreamInput<T>)(object)byteChannel);
                        var consumerTask = handler(consumeCtx, ct);
                        await pushTask;
                        return await consumerTask;
                    }
                }

                await pushTask;
                throw new InvalidOperationException(
                    $"Cannot bridge classic string input to streaming consumer of type {typeof(T).Name}. " +
                    "Use a streaming producer upstream or register a string-typed consumer.");
            }
        };

        _registrations.Add(new CommandRegistration(
            chain, description, classicFallback,
            StageMode.StreamConsumer, options, launcher,
            isBuiltin, showInHelp));
        InvalidateCaches();
    }

    public void RegisterStreamingPipeline<T>(
        ChainNode chain,
        string description,
        Func<IStreamingCommandContext<T>, CancellationToken, Task<int>> producer,
        Func<IConsumingCommandContext<T>, CancellationToken, Task<int>> consumer,
        BatchOptions? options = null,
        bool isBuiltin = false,
        bool? showInHelp = null)
    {
        var capacity = options?.ChannelCapacity ?? 100;
        var launcher = new StreamingLauncher<T>(producer: producer, consumer: consumer, defaultChannelCapacity: capacity);

        Func<CommandContext, CancellationToken, Task<int>> defaultHandler = async (ctx, ct) =>
        {
            var channel = new StreamChannel<T>(capacity);
            await using (channel)
            {
                var producerCtx = new StreamingCommandContext<T>(ctx, channel);
                var consumerCtx = new ConsumingCommandContext<T>(ctx, channel);

                var producerTask = Task.Run(async () =>
                {
                    try
                    {
                        return await producer(producerCtx, ct);
                    }
                    finally
                    {
                        channel.Complete();
                    }
                }, ct);

                var consumerTask = consumer(consumerCtx, ct);

                int producerExit = 0;
                int consumerExit = 0;
                Exception? producerEx = null;
                Exception? consumerEx = null;

                try
                {
                    producerExit = await producerTask.ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    producerEx = ex;
                }

                try
                {
                    consumerExit = await consumerTask.ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    consumerEx = ex;
                }

                if (producerEx is not null && consumerEx is not null)
                {
                    Vice.Log.Emit(ViceLogLevel.Warn, "secondary stage exception (consumer)", consumerEx);
                    throw producerEx;
                }
                if (producerEx is not null)
                {
                    throw producerEx;
                }

                if (consumerEx is not null)
                {
                    throw consumerEx;
                }

                return producerExit != 0 ? producerExit : consumerExit;
            }
        };

        _registrations.Add(new CommandRegistration(
            chain, description, defaultHandler,
            StageMode.StreamProducer, options, launcher,
            isBuiltin, showInHelp));
        InvalidateCaches();
    }

    public IReadOnlyList<IChainDescriptor> GetDescriptors()
    {
        var cached = _descriptorsCache;
        if (cached is not null)
        {
            return cached;
        }

        var built = _registrations.Select(r => (IChainDescriptor)r.Chain).ToList();
        Interlocked.CompareExchange(ref _descriptorsCache, built, null);
        if (Interlocked.Exchange(ref _collisionsReported, 1) == 0)
        {
            var collisions = ValidateCollisions();
            if (collisions.Count > 0)
            {
                foreach (var msg in collisions)
                {
                    Vice.Log.Emit(ViceLogLevel.Warn, "command registry collision: " + msg);
                }
            }
        }
        return _descriptorsCache!;
    }

    public CommandRegistration? FindByVerb(string verb)
    {
        return _registrations.FirstOrDefault(r =>
        {
            if (r.IsBuiltin)
            {
                return false;
            }

            var chain = (IChainDescriptor)r.Chain;
            if (string.Equals(chain.Name, verb, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            foreach (var synonym in chain.Synonyms)
            {
                if (string.Equals(synonym, verb, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        });
    }

    public IReadOnlyList<CommandRegistration> FindContaining(string token)
        => _registrations
            .Where(r => !r.IsBuiltin && ChainContains(r.Chain, token))
            .ToList();

    private static bool ChainContains(Vice.Nodes.ChainNode? node, string token)
    {
        for (var n = (IChainDescriptor?)node; n is not null; n = n.Next)
        {
            if (string.Equals(n.Name, token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            foreach (var syn in n.Synonyms)
            {
                if (string.Equals(syn, token, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static string ChainSignature(IChainDescriptor root)
    {
        var parts = new List<string>();
        for (var current = (IChainDescriptor?)root; current is not null; current = current.Next)
        {
            var kind = current.Kind.ToString().ToLowerInvariant();
            var name = (current.Name ?? string.Empty).ToLowerInvariant();
            parts.Add($"{kind}:{name}");
        }
        return string.Join(" -> ", parts);
    }

    public IReadOnlyList<string> ValidateCollisions()
    {
        var bySignature = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (var i = 0; i < _registrations.Count; i++)
        {
            var sig = ChainSignature(_registrations[i].Chain);
            bySignature.TryGetValue(sig, out var bucket);
            bucket ??= bySignature[sig] = new List<int>();
            bucket.Add(i);
        }

        var collisions = new List<string>();
        foreach (var kv in bySignature.Where(kv => kv.Value.Count >= 2))
        {
            var indices = string.Join(", ", kv.Value);
            collisions.Add($"chain signature '{kv.Key}' is claimed by {kv.Value.Count} registrations (indices: {indices})");
        }
        return collisions;
    }
}

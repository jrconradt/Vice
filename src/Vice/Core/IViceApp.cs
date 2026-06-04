using Vice.Contracts;
using Vice.Execution;
using Vice.Nodes;
using Vice.Options;
using Vice.Streaming;

namespace Vice.Core;

public interface IViceApp : IAsyncDisposable
{
    void Register(
        ChainNode chain,
        string description,
        Func<CommandContext, CancellationToken, Task<int>> handler,
        bool showInHelp = true);

    void RegisterPipeline(
        ChainNode chain,
        string description,
        Func<CommandContext, CancellationToken, Task<int>> defaultHandler,
        Dictionary<int, Func<CommandContext, CancellationToken, Task<int>>> stageHandlers);

    void RegisterStreaming<T>(
        ChainNode chain,
        string description,
        Func<IStreamingCommandContext<T>, CancellationToken, Task<int>> handler,
        BatchOptions? options = null,
        Func<CommandContext, CancellationToken, Task<int>>? classicFallback = null,
        bool showInHelp = true);

    void RegisterStreamConsumer<T>(
        ChainNode chain,
        string description,
        Func<IConsumingCommandContext<T>, CancellationToken, Task<int>> handler,
        BatchOptions? options = null);

    void RegisterStreamingPipeline<T>(
        ChainNode chain,
        string description,
        Func<IStreamingCommandContext<T>, CancellationToken, Task<int>> producer,
        Func<IConsumingCommandContext<T>, CancellationToken, Task<int>> consumer,
        BatchOptions? options = null);

    IReadOnlyCollection<GlobalOption> RegisteredGlobalOptions { get; }

    Task<int> RunAsync(string[] args, CancellationToken ct = default);
    Task<int> RunSessionAsync(CancellationToken ct = default);
    Task<int> RunDaemonAsync(CancellationToken ct = default);
}

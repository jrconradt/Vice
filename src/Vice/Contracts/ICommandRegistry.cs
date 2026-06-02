using Vice.Nodes;
using Vice.Parser;

namespace Vice.Contracts;

internal interface ICommandRegistry
{
    IReadOnlyList<CommandRegistration> Registrations { get; }
    IReadOnlyList<CommandRegistration> UserRegistrations { get; }
    IReadOnlyList<CommandRegistration> HelpVisibleRegistrations { get; }

    void Register(
        ChainNode chain,
        string description,
        Func<CommandContext, CancellationToken, Task<int>> handler,
        bool isBuiltin = false,
        bool? showInHelp = null);

    void RegisterPipeline(
        ChainNode chain,
        string description,
        Func<CommandContext, CancellationToken, Task<int>> defaultHandler,
        Dictionary<int, Func<CommandContext, CancellationToken, Task<int>>> stageHandlers,
        bool isBuiltin = false,
        bool? showInHelp = null);

    void RegisterStreaming<T>(
        ChainNode chain,
        string description,
        Func<IStreamingCommandContext<T>, CancellationToken, Task<int>> handler,
        BatchOptions? options = null,
        Func<CommandContext, CancellationToken, Task<int>>? classicFallback = null,
        bool isBuiltin = false,
        bool? showInHelp = null);

    void RegisterStreamConsumer<T>(
        ChainNode chain,
        string description,
        Func<IConsumingCommandContext<T>, CancellationToken, Task<int>> handler,
        BatchOptions? options = null,
        bool isBuiltin = false,
        bool? showInHelp = null);

    void RegisterStreamingPipeline<T>(
        ChainNode chain,
        string description,
        Func<IStreamingCommandContext<T>, CancellationToken, Task<int>> producer,
        Func<IConsumingCommandContext<T>, CancellationToken, Task<int>> consumer,
        BatchOptions? options = null,
        bool isBuiltin = false,
        bool? showInHelp = null);

    IReadOnlyList<IChainDescriptor> GetDescriptors();
    CommandRegistration? FindByVerb(string verb);

    IReadOnlyList<CommandRegistration> FindContaining(string token);
}

using Vice.Nodes;

namespace Vice.Contracts;

internal sealed class CommandRegistration
{
    public ChainNode Chain { get; }
    public string Description { get; }
    public Func<CommandContext, CancellationToken, Task<int>> Handler { get; }
    public IReadOnlyDictionary<int, Func<CommandContext, CancellationToken, Task<int>>>? StageHandlers { get; }
    public bool IsBuiltin { get; }
    public bool ShowInHelp { get; }
    public StageMode Mode { get; }
    public BatchOptions? StreamOptions { get; }
    public IStreamingLauncher? Launcher { get; }
    public string? SourceNamespace { get; }

    public Type? StreamItemType => Launcher?.ItemType;

    public CommandRegistration(
        ChainNode chain,
        string description,
        Func<CommandContext, CancellationToken, Task<int>> handler,
        bool isBuiltin = false,
        bool? showInHelp = null,
        string? sourceNamespace = null)
    {
        Chain = chain;
        Description = description;
        Handler = handler;
        StageHandlers = null;
        IsBuiltin = isBuiltin;
        ShowInHelp = showInHelp ?? !isBuiltin;
        Mode = StageMode.Buffered;
        SourceNamespace = sourceNamespace;
    }

    public CommandRegistration(
        ChainNode chain,
        string description,
        Func<CommandContext, CancellationToken, Task<int>> handler,
        IReadOnlyDictionary<int, Func<CommandContext, CancellationToken, Task<int>>> stageHandlers,
        bool isBuiltin = false,
        bool? showInHelp = null,
        string? sourceNamespace = null)
    {
        Chain = chain;
        Description = description;
        Handler = handler;
        StageHandlers = stageHandlers;
        IsBuiltin = isBuiltin;
        ShowInHelp = showInHelp ?? !isBuiltin;
        Mode = StageMode.Buffered;
        SourceNamespace = sourceNamespace;
    }

    public CommandRegistration(
        ChainNode chain,
        string description,
        Func<CommandContext, CancellationToken, Task<int>> handler,
        StageMode mode,
        BatchOptions? streamOptions,
        IStreamingLauncher launcher,
        bool isBuiltin = false,
        bool? showInHelp = null,
        string? sourceNamespace = null)
    {
        Chain = chain;
        Description = description;
        Handler = handler;
        StageHandlers = null;
        IsBuiltin = isBuiltin;
        ShowInHelp = showInHelp ?? !isBuiltin;
        Mode = mode;
        StreamOptions = streamOptions;
        Launcher = launcher;
        SourceNamespace = sourceNamespace;
    }
}

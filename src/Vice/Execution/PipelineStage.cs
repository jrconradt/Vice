using Vice.Parser;
using Vice.Streaming;

namespace Vice.Execution;

internal sealed class PipelineStage
{
    public ConjunctiveKind? Operator { get; }
    public string? OperatorWord { get; }
    public IReadOnlyDictionary<string, string> Targets { get; }
    public IReadOnlyList<ResolvedCommand> ResolvedNodes { get; }
    public Func<CommandContext, CancellationToken, Task<int>> Handler { get; }
    public StageMode Mode { get; }
    public BatchOptions? Options { get; }
    public IStreamingLauncher? Launcher { get; }

    public Type? StreamItemType => Launcher?.ItemType;

    public PipelineStage(
        ConjunctiveKind? op,
        string? operatorWord,
        IReadOnlyDictionary<string, string> targets,
        Func<CommandContext, CancellationToken, Task<int>> handler,
        IReadOnlyList<ResolvedCommand>? resolvedNodes = null)
    {
        Operator = op;
        OperatorWord = operatorWord;
        Targets = targets;
        ResolvedNodes = resolvedNodes ?? Array.Empty<ResolvedCommand>();
        Handler = handler;
        Mode = StageMode.Classic;
    }

    public PipelineStage(
        ConjunctiveKind? op,
        string? operatorWord,
        IReadOnlyDictionary<string, string> targets,
        Func<CommandContext, CancellationToken, Task<int>> handler,
        StageMode mode,
        BatchOptions? options,
        IStreamingLauncher? launcher,
        IReadOnlyList<ResolvedCommand>? resolvedNodes = null)
    {
        Operator = op;
        OperatorWord = operatorWord;
        Targets = targets;
        ResolvedNodes = resolvedNodes ?? Array.Empty<ResolvedCommand>();
        Handler = handler;
        Mode = mode;
        Options = options;
        Launcher = launcher;
    }
}

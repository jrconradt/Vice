using Vice.Display;
using Vice.Display.Rendering;
using Vice.Logging;
using Vice.Options;
using Vice.Parser;
using Vice.Session;

namespace Vice.Execution;

public interface ICommandContext
{
    IConsoleWriter Console { get; }
    RenderContext Render { get; }
    bool Verbose { get; }
    bool Quiet { get; }
    bool DryRun { get; }
    bool NonInteractive { get; }
    bool NoPager { get; }
    OutputFormatKind OutputFormat { get; }
    bool WantsJson { get; }
    string? Locale { get; }
    string? PipelineInput { get; }
    IProgress<string>? StatusUpdater { get; }
    IProgress<double>? ProgressReporter { get; }
    SessionContext? Session { get; }
    IViceLogger Logger { get; }
    string InvocationId { get; }
    CancellationToken CancellationToken { get; }

    IReadOnlyList<ResolvedCommand> ResolvedNodes { get; }

    string? this[string name] { get; }
    string Require(string name);
    string? GetTarget(string name);
    IReadOnlyList<string> GetTargets(string name);
    string? GetGlobalOption(string name);
    bool HasGlobalOption(string name);

    string? GetGlobalOption(ValueBearingOption option);

    bool HasGlobalOption(FlagOption option);

    T? Get<T>(Option<T> option);
}

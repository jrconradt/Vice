using Vice.Contracts;
using Vice.Display;
using Vice.Display.Rendering;
using Vice.Execution;
using Vice.Logging;
using Vice.Options;
using Vice.Parser;

namespace Vice.Streaming;

internal abstract class DelegatingCommandContext : ICommandContext
{
    protected readonly CommandContext Inner;
    private readonly IConsoleWriter? _console;

    protected DelegatingCommandContext(CommandContext inner)
    {
        Inner = inner;
    }

    protected DelegatingCommandContext(CommandContext inner, IConsoleWriter console)
    {
        Inner = inner;
        _console = console;
    }

    public IConsoleWriter Console => _console ?? Inner.Console;
    public RenderContext Render => Inner.Render;
    public bool Verbose => Inner.Verbose;
    public bool Quiet => Inner.Quiet;
    public bool DryRun => Inner.DryRun;
    public bool NonInteractive => Inner.NonInteractive;
    public bool NoPager => Inner.NoPager;
    public OutputFormatKind OutputFormat => Inner.OutputFormat;
    public bool WantsJson => Inner.WantsJson;
    public string? Locale => Inner.Locale;
    public string? PipelineInput => Inner.PipelineInput;
    public IProgress<string>? StatusUpdater => Inner.StatusUpdater;
    public IProgress<double>? ProgressReporter => Inner.ProgressReporter;
    public ISessionContext? Session => Inner.Session;
    public IViceLogger Logger => Inner.Logger;
    public CancellationToken CancellationToken => Inner.CancellationToken;
    public string InvocationId => Inner.InvocationId;

    public IReadOnlyList<ResolvedCommand> ResolvedNodes => Inner.ResolvedNodes;

    public string? this[string name] => Inner[name];
    public string Require(string name) => Inner.Require(name);
    public string? GetTarget(string name) => Inner.GetTarget(name);
    public IReadOnlyList<string> GetTargets(string name) => Inner.GetTargets(name);
    public string? GetGlobalOption(string name) => Inner.GetGlobalOption(name);
    public bool HasGlobalOption(string name) => Inner.HasGlobalOption(name);
    public string? GetGlobalOption(ValueBearingOption option) => Inner.GetGlobalOption(option);
    public bool HasGlobalOption(FlagOption option) => Inner.HasGlobalOption(option);
    public T? Get<T>(Option<T> option) => Inner.Get(option);
}

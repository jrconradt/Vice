using Vice.Logging;
using Vice.Display;
using Vice.Display.Rendering;
using Vice.Options;
using Vice.Parser;
using Vice.Session;

namespace Vice.Execution;

public sealed class CommandContext : ICommandContext
{
    private readonly IReadOnlyDictionary<string, string> _targetValues;
    private readonly IReadOnlyDictionary<string, string?> _globalOptions;
    private readonly IReadOnlyList<ResolvedCommand> _resolvedNodes;

    public IReadOnlyList<ResolvedCommand> ResolvedNodes => _resolvedNodes;

    public IConsoleWriter Console { get; }
    public RenderContext Render { get; }
    public bool Verbose => _globalOptions.ContainsKey(new VerboseOption().Name);
    public bool Quiet => _globalOptions.ContainsKey(new QuietOption().Name);
    public bool DryRun => _globalOptions.ContainsKey(new DryRunOption().Name);
    public bool NonInteractive => _globalOptions.ContainsKey(new NonInteractiveOption().Name);
    public bool NoPager => _globalOptions.ContainsKey(new NoPagerOption().Name);
    public OutputFormatKind OutputFormat { get; }
    public bool WantsJson => OutputFormat is OutputFormatKind.Json or OutputFormatKind.Jsonl or OutputFormatKind.Ndjson;
    public string? Locale =>
        _globalOptions.TryGetValue(new LocaleOption().Name, out var l) && !string.IsNullOrEmpty(l) ? l : null;
    public string? PipelineInput { get; }
    public IProgress<string>? StatusUpdater { get; }
    public IProgress<double>? ProgressReporter { get; }
    public SessionContext? Session { get; }
    public IViceLogger Logger { get; }
    public CancellationToken CancellationToken { get; init; }

    internal CommandContext(
        IReadOnlyDictionary<string, string> targetValues,
        IReadOnlyDictionary<string, string?> globalOptions,
        IConsoleWriter console,
        string? pipelineInput,
        IProgress<string>? statusUpdater,
        RenderContext? render,
        IProgress<double>? progressReporter,
        SessionContext? session,
        IViceLogger logger,
        IReadOnlyList<ResolvedCommand>? resolvedNodes = null)
    {
        _targetValues = targetValues;
        _globalOptions = globalOptions;
        _resolvedNodes = resolvedNodes ?? Array.Empty<ResolvedCommand>();
        OutputFormat = OutputFormatKindParser.Parse(
            globalOptions.TryGetValue(new FormatOption().Name, out var rawFormat) ? rawFormat : null);
        Console = console;
        PipelineInput = pipelineInput;
        StatusUpdater = statusUpdater;
        Render = render ?? new RenderContext(console, TerminalCapabilities.Detect());
        ProgressReporter = progressReporter;
        Session = session;
        Logger = logger ?? NullViceLogger.Instance;
    }

    public string? this[string name] =>
        _targetValues.TryGetValue(name, out var value) ? value : null;

    public string Require(string name)
    {
        if (_targetValues.TryGetValue(name, out var value) && value is not null)
        {
            return value;
        }

        throw new InvalidOperationException($"Required target '{name}' was not provided.");
    }

    public string? GetTarget(string name) =>
        _targetValues.TryGetValue(name, out var value) ? value : null;

    public string? Target(TargetDef target) =>
        _targetValues.TryGetValue(target.Name, out var value) ? value : null;

    public bool HasTarget(TargetDef target) => _targetValues.ContainsKey(target.Name);

    public string RequireTarget(TargetDef target)
    {
        if (_targetValues.TryGetValue(target.Name, out var value) && value is not null)
        {
            return value;
        }

        throw new InvalidOperationException($"Required target '{target.Name}' was not provided.");
    }

    public IReadOnlyList<string> Targets(TargetDef target) => GetTargets(target.Name);

    public IReadOnlyList<string> GetTargets(string name)
    {
        if (_resolvedNodes.Count == 0)
        {
            return _targetValues.TryGetValue(name, out var only)
                ? new[] { only }
                : Array.Empty<string>();
        }

        var values = new List<string>();
        foreach (var node in _resolvedNodes)
        {
            if (node.TargetValues.TryGetValue(name, out var v))
            {
                values.Add(v);
            }
        }
        return values;
    }

    public string? GetGlobalOption(string name) =>
        _globalOptions.TryGetValue(name, out var value) ? value : null;

    public bool HasGlobalOption(string name) => _globalOptions.ContainsKey(name);

    public string? GetGlobalOption(ValueBearingOption option) =>
        _globalOptions.TryGetValue(option.Name, out var value) ? value : option.Default;

    public bool HasGlobalOption(FlagOption option) => _globalOptions.ContainsKey(option.Name);

    public T? Get<T>(Option<T> option)
    {
        if (!_globalOptions.TryGetValue(option.Name, out var raw))
        {
            return option.Default;
        }

        if (!option.TakesValue)
        {
            return option.ParseOrDefault(raw ?? string.Empty);
        }

        return option.ParseOrDefault(raw);
    }
}

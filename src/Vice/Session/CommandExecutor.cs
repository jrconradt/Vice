using System.Collections.Frozen;
using System.Diagnostics;
using Vice.Commands;
using Vice.Contracts;
using Vice.Display;
using Vice.Display.Rendering;
using Vice.Execution;
using Vice.Help;
using Vice.Logging;
using Vice.Options;
using Vice.Parser;

namespace Vice.Session;

internal sealed class CommandExecutor : ICommandExecutor
{
    private readonly CommandRegistry _registry;
    private readonly IReadOnlyCollection<GlobalOption> _options;
    private readonly FrozenSet<string> _valueBearingNames;
    private readonly FrozenSet<string> _flagNames;
    private readonly Vice.Parser.OptionRegistry _optionRegistry;
    private readonly IConsoleWriter _console;
    private readonly IOutputSink _outputSink;
    private readonly IStatusDisplay _status;
    private readonly TerminalCapabilities _capabilities;
    private readonly SessionContext? _session;
    private readonly IViceLogger _logger;
    private readonly SessionBuiltinRegistry? _builtins;
    private readonly string _appName;
    private readonly string _version;
    private readonly string? _description;

    public CommandExecutor(
        CommandRegistry registry,
        IReadOnlyCollection<GlobalOption> globalOptions,
        IConsoleWriter console,
        IStatusDisplay status,
        TerminalCapabilities capabilities,
        IOutputSink outputSink,
        SessionContext? session = null,
        string appName = "",
        string version = "",
        string? description = null,
        IViceLogger? logger = null,
        SessionBuiltinRegistry? builtins = null)
    {
        _registry = registry;
        _options = globalOptions;
        _valueBearingNames = globalOptions.Where(o => o.TakesValue).Select(o => o.Name)
            .ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        _flagNames = globalOptions.Where(o => !o.TakesValue).Select(o => o.Name)
            .ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        _optionRegistry = OptionRegistryBuilder.Build(globalOptions);
        _console = console;
        _outputSink = outputSink ?? NullOutputSink.Instance;
        _status = status;
        _capabilities = capabilities;
        _session = session;
        _logger = logger ?? NullViceLogger.Instance;
        _builtins = builtins;
        _appName = appName;
        _version = version;
        _description = description;
    }

    public Task<int> ExecuteAsync(string input, CancellationToken ct = default)
        => ExecuteResolvedAsync(ResolveCommand(input), ct);

    public Task<int> ExecuteAsync(string[] args, CancellationToken ct = default)
        => ExecuteResolvedAsync(ResolveCommand(args), ct);

    private ParseResult ResolveCommand(string input)
        => CommandResolver.Resolve(input, _registry.GetDescriptors(), _optionRegistry);

    private ParseResult ResolveCommand(string[] args)
        => CommandResolver.Resolve(args, _registry.GetDescriptors(), _optionRegistry);

    private static IReadOnlyList<string> ExtractChainWords(Vice.Parser.CommandChain chain)
    {
        var words = new List<string>(chain.Nodes.Count);
        foreach (var node in chain.Nodes)
        {
            words.Add(node.MatchedName);
        }

        return words;
    }

    private async Task<int> ExecuteResolvedAsync(ParseResult result, CancellationToken ct)
    {
        var caps = CapabilityOverrides.Apply(_capabilities, result.GlobalOptions);

        var originalCulture = System.Globalization.CultureInfo.CurrentCulture;
        var originalUICulture = System.Globalization.CultureInfo.CurrentUICulture;
        var localeApplied = false;
        if (result.GlobalOptions.TryGetValue(new LocaleOption().Name, out var localeValue)
            && !string.IsNullOrWhiteSpace(localeValue))
        {
            try
            {
                var culture = System.Globalization.CultureInfo.GetCultureInfo(localeValue);
                System.Globalization.CultureInfo.CurrentCulture = culture;
                System.Globalization.CultureInfo.CurrentUICulture = culture;
                localeApplied = true;
            }
            catch (System.Globalization.CultureNotFoundException)
            {
                _console.WriteError($"Unknown locale '{localeValue}'. Falling back to system default.");
            }
        }

        try
        {
            return await ExecuteResolvedCoreAsync(result, caps, ct).ConfigureAwait(false);
        }
        finally
        {
            FlushOutput();
            if (localeApplied)
            {
                System.Globalization.CultureInfo.CurrentCulture = originalCulture;
                System.Globalization.CultureInfo.CurrentUICulture = originalUICulture;
            }
        }
    }

    private async Task<int> ExecuteResolvedCoreAsync(ParseResult result, TerminalCapabilities caps, CancellationToken ct)
    {
        if (result.GlobalOptions.ContainsKey(new HelpOption().Name))
        {
            var renderCtx = new RenderContext(_console, caps);
            HelpBuilder.WriteHelp(
                BuiltinCommands.BuildHelpTitle(_appName, _version, _description),
                _registry.HelpVisibleRegistrations,
                BuiltinCommands.OrderGlobalOptions(_options, StringComparer.OrdinalIgnoreCase),
                renderCtx);
            return 0;
        }

        if (result.GlobalOptions.ContainsKey(new VersionOption().Name))
        {
            _console.WriteLine($"{_appName} v{_version}");
            return 0;
        }

        if (!result.Success)
        {
            foreach (var error in result.Errors)
            {
                _console.WriteError(error);
            }

            if (result.Errors.Count > 0 && !string.IsNullOrEmpty(_appName))
            {
                _console.WriteError($"Run '{_appName} --help' for usage information.");
            }

            return 1;
        }

        var statusDisplay = result.GlobalOptions.ContainsKey(new NoStatusOption().Name)
            ? (IStatusDisplay)NullStatusDisplay.Instance
            : _status;

        if (result.Segments is not null)
        {
            var stages = PipelineSplitter.BuildFromSegments(result.Segments, _registry.Registrations);
            var pipelineExec = new PipelineExecutor(_session, _logger);
            return await pipelineExec.ExecuteAsync(stages, result.GlobalOptions, _console, statusDisplay, caps, ct);
        }

        var registration = _registry.Registrations[result.MatchedRegistrationIndex];

        if (PipelineSplitter.HasPipeline(result.Chain!))
        {
            var stages = PipelineSplitter.Split(result.Chain!, registration);
            var pipelineExec = new PipelineExecutor(_session, _logger);
            return await pipelineExec.ExecuteAsync(stages, result.GlobalOptions, _console, statusDisplay, caps, ct);
        }

        var targetValues = result.Chain!.AllTargetValues();

        await using var handle = statusDisplay.Start(registration.Description, _console);
        var statusUpdater = new Progress<string>(label => handle.UpdateLabel(label));
        var progressReporter = handle.SupportsProgress
            ? new Progress<double>(frac => handle.UpdateProgress(frac))
            : null;
        var renderCtx2 = new RenderContext(handle.Writer, caps);
        var ctx = new CommandContext(targetValues, result.GlobalOptions, handle.Writer, null, statusUpdater, renderCtx2, progressReporter, _session, _logger, result.Chain!.Nodes) { CancellationToken = ct };

        if (_builtins is not null && result.Chain is not null
            && _builtins.TryGetHandler(ExtractChainWords(result.Chain), out var builtinHandler)
            && builtinHandler is not null)
        {
            var builtinExit = await InvokeWithObservabilityAsync(
                $"session-builtin {registration.Description}", builtinHandler, ctx, ct, handle).ConfigureAwait(false);
            if (builtinExit == 0)
            {
                handle.Succeed();
            }
            else
            {
                handle.Fail();
            }

            return builtinExit;
        }

        var exitCode = await InvokeWithObservabilityAsync(
            registration.Description, registration.Handler, ctx, ct, handle).ConfigureAwait(false);
        if (exitCode == 0)
        {
            handle.Succeed();
        }
        else
        {
            handle.Fail();
        }

        return exitCode;
    }

    private void FlushOutput()
    {
        if (_outputSink is ConsoleOutputSink sink)
        {
            sink.Flush();
        }
    }

    private static async Task<int> InvokeWithObservabilityAsync(
        string commandName,
        Func<CommandContext, CancellationToken, Task<int>> handler,
        CommandContext ctx,
        CancellationToken ct,
        IStatusHandle handle)
    {
        ctx.Logger.Log(new CommandStarted(commandName));
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await handler(ctx, ct).ConfigureAwait(false);
            sw.Stop();
            ctx.Logger.Log(new CommandCompleted(commandName, result, sw.Elapsed));
            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            ctx.Logger.Log(new CommandFailed(commandName, ex, sw.Elapsed));
            handle.Fail();
            throw;
        }
    }
}

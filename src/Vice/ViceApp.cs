using Vice.Commands;
using Vice.Contracts;
using Vice.Display;
using Vice.Display.Rendering;
using Vice.Execution;
using Vice.Ipc;
using Vice.Jobs;
using Vice.Logging;
using Vice.Nodes;
using Vice.Options;
using Vice.Plugins;
using Vice.Session;
using Vice.Streaming;

namespace Vice;

public sealed class ViceApp : IViceApp, IAsyncDisposable
{
    private readonly string _name;
    private readonly string _version;
    private readonly string? _description;
    private readonly CommandRegistry _registry;
    private readonly IConsoleWriter _console;
    private readonly IOutputSink _outputSink;
    private readonly IStatusDisplay _status;
    private readonly TerminalCapabilities _capabilities;
    private readonly int _concurrency;
    private readonly IReadOnlyList<IJobRunner> _jobRunners;
    private readonly IReadOnlyDictionary<Type, object> _sessionServices;
    private readonly IViceLogger _logger;
    private readonly TimeSpan _shutdownTimeout;
    private readonly Dictionary<string, GlobalOption> _options
        = new(StringComparer.OrdinalIgnoreCase);
    private readonly IOutputSink _priorOutputSink;
    private readonly IStatusSink _priorStatusSink;
    private readonly ILogSink _priorLogSink;
    private readonly IDisposable? _ownedOutputSink;
    private bool _disposed;

    private static readonly GlobalOption[] FrameworkGlobalOptions =
    {
        new HelpOption(),
        new VersionOption(),
        new VerboseOption(),
        new QuietOption(),
        new DryRunOption(),
        new NonInteractiveOption(),
        new NoStatusOption(),
        new NoPagerOption(),
        new NoColorOption(),
        new ColorOption(),
        new LocaleOption(),
        new FormatOption(),
        new EncodingOption(),
        new MetadataOption(),
        new LimitOption(),
        new OffsetOption(),
        new PageOption(),
        new TimeoutOption(),
        new ChunkSizeOption(),
        new DepthOption(),
    };

    internal ViceApp(string name, string version, string? description,
        IConsoleWriter? console = null,
        IOutputSink? outputSink = null,
        IStatusDisplay? status = null,
        TerminalCapabilities? capabilities = null,
        int concurrency = 3,
        IReadOnlyList<IJobRunner>? jobRunners = null,
        IReadOnlyDictionary<Type, object>? sessionServices = null,
        IReadOnlyList<GlobalOption>? globalOptions = null,
        IViceLogger? logger = null,
        TimeSpan? shutdownTimeout = null)
    {
        _name = name;
        _version = version;
        _description = description;
        _concurrency = concurrency;
        _jobRunners = jobRunners ?? Array.Empty<IJobRunner>();
        _sessionServices = sessionServices ?? new Dictionary<Type, object>();
        _logger = logger ?? NullViceLogger.Instance;
        _registry = new CommandRegistry(_logger);
        _shutdownTimeout = shutdownTimeout ?? TimeSpan.FromSeconds(10);

        foreach (var opt in FrameworkGlobalOptions)
        {
            _options[opt.Name] = opt;
        }

        if (globalOptions is not null)
        {
            foreach (var opt in globalOptions)
            {
                _options[opt.Name] = opt;
            }
        }

        _priorOutputSink = Vice.Output.Current;
        _priorStatusSink = Vice.Status.Current;
        _priorLogSink = Vice.Log.Current;

        try
        {
            IOutputSink resolvedSink;
            if (outputSink is not null)
            {
                resolvedSink = outputSink;
                Vice.Output.Configure(outputSink);
            }
            else if (console is null)
            {
                var ownedSink = new ConsoleOutputSink();
                _ownedOutputSink = ownedSink;
                resolvedSink = ownedSink;
                Vice.Output.Configure(ownedSink);
            }
            else
            {
                resolvedSink = Vice.Output.Current;
            }
            _outputSink = resolvedSink;
            _console = console ?? new ConsoleWriter(resolvedSink);
            _capabilities = capabilities ?? TerminalCapabilities.Detect();
            if (status is null && _console is ConsoleWriter
                && !System.Console.IsErrorRedirected)
            {
                Vice.Status.Configure(new UnifiedStatusSink(_capabilities, _console));
                _status = new UnifiedStatusDisplay(_capabilities);
            }
            else
            {
                _status = status ?? NullStatusDisplay.Instance;
            }

            if (_logger is not NullViceLogger)
            {
                Vice.Log.Configure(new ViceLoggerLogSink(_logger));
            }

            BuiltinCommands.Register(_registry, this);
            SessionBuiltins.RegisterChains(_registry);
        }
        catch
        {
            Vice.Output.Configure(_priorOutputSink);
            Vice.Status.Configure(_priorStatusSink);
            Vice.Log.Configure(_priorLogSink);
            _ownedOutputSink?.Dispose();
            throw;
        }
    }

    public IReadOnlyCollection<GlobalOption> RegisteredGlobalOptions => _options.Values;

    public IViceLogger Logger => _logger;

    public TimeSpan ShutdownTimeout => _shutdownTimeout;

    public static ViceAppBuilder Create(string name, string version)
        => new ViceAppBuilder(name, version);

    public void Register(
        ChainNode chain,
        string description,
        Func<CommandContext, CancellationToken, Task<int>> handler,
        bool showInHelp = true)
    {
        _registry.Register(chain, description, handler, isBuiltin: false, showInHelp: showInHelp);
    }

    public void RegisterPipeline(
        ChainNode chain,
        string description,
        Func<CommandContext, CancellationToken, Task<int>> defaultHandler,
        Dictionary<int, Func<CommandContext, CancellationToken, Task<int>>> stageHandlers)
    {
        _registry.RegisterPipeline(chain, description, defaultHandler, stageHandlers);
    }

    public void RegisterStreaming<T>(
        ChainNode chain,
        string description,
        Func<IStreamingCommandContext<T>, CancellationToken, Task<int>> handler,
        BatchOptions? options = null,
        Func<CommandContext, CancellationToken, Task<int>>? classicFallback = null,
        bool showInHelp = true)
    {
        _registry.RegisterStreaming(chain, description, handler, options, classicFallback,
            isBuiltin: false, showInHelp: showInHelp);
    }

    public void RegisterStreamConsumer<T>(
        ChainNode chain,
        string description,
        Func<IConsumingCommandContext<T>, CancellationToken, Task<int>> handler,
        BatchOptions? options = null)
    {
        _registry.RegisterStreamConsumer(chain, description, handler, options);
    }

    public void RegisterStreamingPipeline<T>(
        ChainNode chain,
        string description,
        Func<IStreamingCommandContext<T>, CancellationToken, Task<int>> producer,
        Func<IConsumingCommandContext<T>, CancellationToken, Task<int>> consumer,
        BatchOptions? options = null)
    {
        _registry.RegisterStreamingPipeline(chain, description, producer, consumer, options);
    }

    public async Task<int> RunAsync(string[] args, CancellationToken ct = default)
    {
        if (RawArgsSplitter.ContainsPiping(args) && ShouldUseMultiProcessPipeline(args))
        {
            return await MultiProcessPipeline.RunAsync(_name, RawArgsSplitter.Split(args), _registry, _logger, ct).ConfigureAwait(false);
        }

        if (PluginDispatcher.TryFind(_name, args, _registry, _logger, out var pluginPath, out var pluginArgs))
        {
            return await PluginDispatcher.RunAsync(pluginPath, pluginArgs, _logger, ct).ConfigureAwait(false);
        }

        var state = SessionState.For(_name);
        var session = SessionContext.OneShot(state, _sessionServices, _logger);
        return await CreateExecutor(session: session).ExecuteAsync(args, ct).ConfigureAwait(false);
    }

    private bool ShouldUseMultiProcessPipeline(string[] args)
    {
        if (RawArgsSplitter.ContainsFan(args))
        {
            return true;
        }

        var optionRegistry = OptionRegistryBuilder.Build(_options.Values);
        var parse = Vice.Parser.CommandResolver.Resolve(args, _registry.GetDescriptors(), optionRegistry);
        if (parse.Success)
        {
            return false;
        }

        var segments = RawArgsSplitter.Split(args);
        return segments.Any(seg =>
        {
            if (seg.Args.Length == 0)
            {
                return false;
            }

            var verb = seg.Args[0];
            return !verb.StartsWith("-", StringComparison.Ordinal) && _registry.FindByVerb(verb) is null
                && PluginDispatcher.TryFindOnPath($"{_name}-{verb}", _logger, out _);
        });
    }

    private CommandExecutor CreateExecutor(
        SessionContext? session,
        IConsoleWriter? console = null,
        IStatusDisplay? status = null,
        SessionBuiltinRegistry? builtins = null) =>
        new(_registry, _options.Values, console ?? _console, status ?? _status, _capabilities, _outputSink,
            session: session, appName: _name, version: _version, description: _description,
            logger: _logger, builtins: builtins);

    internal CommandExecutor CreateDaemonExecutor(
        SessionContext session,
        IConsoleWriter console,
        IStatusDisplay status) =>
        CreateExecutor(session, console: console, status: status);

    public async Task<int> RunSessionAsync(CancellationToken ct = default)
    {
        var state = SessionState.For(_name);
        await using var jobManager = new JobManager(_jobRunners, _concurrency, _logger, ct, _shutdownTimeout);

        var sessionCtx = new SessionContext(jobManager, state, _sessionServices, _logger);
        await using var history = new InputHistory();

        var builtins = new SessionBuiltinRegistry(jobManager, history);

        var executor = CreateExecutor(sessionCtx, builtins: builtins);
        using var loop = new SessionLoop(executor, jobManager, history, _console, System.Console.In, _logger);

        var shouldDaemonize = await loop.RunAsync(ct);

        if (shouldDaemonize)
        {
            var daemonCtx = new SessionContext(jobManager, state, _sessionServices, _logger, isInteractive: false);
            return await RunDaemonInternalAsync(jobManager, state, daemonCtx, ct);
        }

        return 0;
    }

    public async Task<int> RunDaemonAsync(CancellationToken ct = default)
    {
        var state = SessionState.For(_name);
        return await RunDaemonAsync(state, ct).ConfigureAwait(false);
    }

    internal async Task<int> RunDaemonAsync(SessionState state, CancellationToken ct = default)
    {
        await using var jobManager = new JobManager(_jobRunners, _concurrency, _logger, ct, _shutdownTimeout);

        var sessionCtx = new SessionContext(jobManager, state, _sessionServices, _logger, isInteractive: false);

        return await RunDaemonInternalAsync(jobManager, state, sessionCtx, ct);
    }

    private async Task<int> RunDaemonInternalAsync(
        JobManager jobManager, SessionState state,
        SessionContext sessionCtx,
        CancellationToken ct)
    {
        PipeClient? existing;
        try
        {
            existing = await PipeClient.TryConnectAsync(state.PipeName, timeoutMs: 500, _logger, ct).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException ex)
        {
            Vice.Log.Emit(ViceLogLevel.Error, $"permission denied probing daemon pipe '{state.PipeName}'; refusing to start", ex);
            return ViceExitCode.FAILURE;
        }

        if (existing is not null)
        {
            await using (existing)
            {
                Vice.Log.Emit(ViceLogLevel.Info, $"{_name} daemon already running on '{state.PipeName}'; not starting a second instance");
            }

            return ViceExitCode.SUCCESS;
        }

        var handler = new DaemonMessageHandler(
            this,
            jobManager,
            sessionCtx,
            NullConsoleWriter.Instance,
            DaemonMessageHandler.DaemonControlVerbs);

        await using var server = new PipeServer(state.PipeName, handler.HandleAsync, _logger);

        handler.BindLiveness(() => new DaemonLiveness(
            server.IsListening,
            server.AcceptLoopCrashed,
            server.Faulted?.Message));

        await server.StartAsync(ct);

        Vice.Log.Emit(ViceLogLevel.Info, $"{_name} daemon listening on '{state.PipeName}'");

        return await SuperviseAcceptLoopAsync(server, ct).ConfigureAwait(false);
    }

    private static async Task<int> SuperviseAcceptLoopAsync(PipeServer server, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using (ct.Register(() => tcs.TrySetResult(ViceExitCode.SUCCESS)))
        {
            while (!ct.IsCancellationRequested)
            {
                if (server.AcceptLoopCrashed
                    || (!server.IsListening && !ct.IsCancellationRequested))
                {
                    Vice.Log.Emit(ViceLogLevel.Error, "daemon pipe accept loop terminated; exiting so a supervisor can restart", server.Faulted);
                    return ViceExitCode.FAILURE;
                }

                var poll = Task.Delay(TimeSpan.FromMilliseconds(250), CancellationToken.None);
                var completed = await Task.WhenAny(tcs.Task, poll).ConfigureAwait(false);
                if (completed == tcs.Task)
                {
                    return await tcs.Task.ConfigureAwait(false);
                }
            }

            return ViceExitCode.SUCCESS;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        foreach (var svc in _sessionServices.Values.Reverse())
        {
            try
            {
                if (svc is IAsyncDisposable ad)
                {
                    await ad.DisposeAsync().ConfigureAwait(false);
                }
                else if (svc is IDisposable d)
                {
                    d.Dispose();
                }
            }
            catch (Exception ex)
            {
                Vice.Log.Emit(ViceLogLevel.Warn, $"session service {svc?.GetType().FullName} dispose threw", ex);
            }
        }

        Vice.Output.Configure(_priorOutputSink);
        Vice.Status.Configure(_priorStatusSink);
        Vice.Log.Configure(_priorLogSink);
        _ownedOutputSink?.Dispose();
    }

    internal CommandRegistry Registry => _registry;
    internal IConsoleWriter Console => _console;
    internal IStatusDisplay Status => _status;
    internal TerminalCapabilities Capabilities => _capabilities;
    internal string Name => _name;
    internal string Version => _version;
    internal string? Description => _description;
}

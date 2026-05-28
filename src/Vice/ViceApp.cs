using System.Collections.Frozen;
using Vice.Commands;
using Vice.Execution;
using Vice.Ipc;
using Vice.Jobs;
using Vice.Logging;
using Vice.Nodes;
using Vice.Display;
using Vice.Display.Rendering;
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
    private readonly CommandRegistry _registry = new();
    private readonly IConsoleWriter _console;
    private readonly IStatusDisplay _status;
    private readonly TerminalCapabilities _capabilities;
    private readonly int _concurrency;
    private readonly IReadOnlyList<IJobRunner> _jobRunners;
    private readonly IReadOnlyDictionary<Type, object> _sessionServices;
    private readonly IViceLogger _logger;
    private readonly TimeSpan _shutdownTimeout;
    private readonly Dictionary<string, GlobalOption> _options
        = new(StringComparer.OrdinalIgnoreCase);
    private readonly FrozenSet<string> _valueBearingOptionNames;
    private readonly FrozenSet<string> _flagOptionNames;
    private readonly IOutputSink _priorOutputSink;
    private readonly IStatusSink _priorStatusSink;
    private readonly ILogSink _priorLogSink;
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
        IConsoleWriter? console = null, IStatusDisplay? status = null,
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

        _valueBearingOptionNames = _options.Values.Where(o => o.TakesValue).Select(o => o.Name)
            .ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        _flagOptionNames = _options.Values.Where(o => !o.TakesValue).Select(o => o.Name)
            .ToFrozenSet(StringComparer.OrdinalIgnoreCase);

        _priorOutputSink = Vice.Output.Current;
        _priorStatusSink = Vice.Status.Current;
        _priorLogSink = Vice.Log.Current;

        if (console is null)
        {
            Vice.Output.Configure(new ConsoleOutputSink());
        }
        _console = console ?? new ConsoleWriter();
        _capabilities = capabilities ?? TerminalCapabilities.Detect();
        if (status is null && _console is ConsoleWriter && !System.Console.IsErrorRedirected)
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

    public IReadOnlyCollection<GlobalOption> RegisteredGlobalOptions => _options.Values;

    public IViceLogger Logger => _logger;

    public TimeSpan ShutdownTimeout => _shutdownTimeout;

    private IReadOnlySet<string> ValueBearingOptionNames => _valueBearingOptionNames;

    private IReadOnlySet<string> FlagOptionNames => _flagOptionNames;

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
            return await MultiProcessPipeline.RunAsync(_name, RawArgsSplitter.Split(args), _registry, ct).ConfigureAwait(false);
        }

        if (PluginDispatcher.TryFind(_name, args, _registry, out var pluginPath, out var pluginArgs))
        {
            return await PluginDispatcher.RunAsync(pluginPath, pluginArgs, ct).ConfigureAwait(false);
        }

        await using var state = SessionState.For(_name, logger: _logger);
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
                && PluginDispatcher.TryFindOnPath($"{_name}-{verb}", out _);
        });
    }

    private CommandExecutor CreateExecutor(
        SessionContext? session,
        IConsoleWriter? console = null,
        IStatusDisplay? status = null,
        SessionBuiltinRegistry? builtins = null) =>
        new(_registry, _options.Values, console ?? _console, status ?? _status, _capabilities,
            session: session, appName: _name, version: _version, description: _description,
            logger: _logger, builtins: builtins);

    internal CommandExecutor CreateDaemonExecutor(
        SessionContext session,
        IConsoleWriter console,
        IStatusDisplay status) =>
        CreateExecutor(session, console: console, status: status);

    public async Task<int> RunSessionAsync(CancellationToken ct = default)
    {
        await using var state = SessionState.For(_name, logger: _logger);
        var persistence = new JobPersistence(state.JobsPath);

        await using var jobManager = await JobManager.CreateAsync(_jobRunners, persistence, _concurrency, _logger, ct, _shutdownTimeout);

        var sessionCtx = new SessionContext(jobManager, state, _sessionServices, _logger);
        await using var history = new InputHistory(state.HistoryPath);
        history.Load();

        var builtins = new SessionBuiltinRegistry(jobManager, state, history);

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
        await using var state = SessionState.For(_name, logger: _logger);
        return await RunDaemonAsync(state, ct).ConfigureAwait(false);
    }

    internal async Task<int> RunDaemonAsync(SessionState state, CancellationToken ct = default)
    {
        var persistence = new JobPersistence(state.JobsPath);

        await using var jobManager = await JobManager.CreateAsync(_jobRunners, persistence, _concurrency, _logger, ct, _shutdownTimeout);

        var sessionCtx = new SessionContext(jobManager, state, _sessionServices, _logger, isInteractive: false);

        return await RunDaemonInternalAsync(jobManager, state, sessionCtx, ct);
    }

    private async Task<int> RunDaemonInternalAsync(
        JobManager jobManager, SessionState state,
        SessionContext sessionCtx,
        CancellationToken ct)
    {
        var handler = new DaemonMessageHandler(this, jobManager, sessionCtx, NullConsoleWriter.Instance);

        await using var server = new PipeServer(state.PipeName, handler.HandleAsync);

        await server.StartAsync(ct);

        var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using (ct.Register(() => tcs.TrySetResult(0)))
        {
            return await tcs.Task.ConfigureAwait(false);
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
    }

    internal CommandRegistry Registry => _registry;
    internal IConsoleWriter Console => _console;
    internal IStatusDisplay Status => _status;
    internal TerminalCapabilities Capabilities => _capabilities;
    internal string Name => _name;
    internal string Version => _version;
    internal string? Description => _description;
}

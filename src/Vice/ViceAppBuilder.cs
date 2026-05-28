using Vice.Jobs;
using Vice.Logging;
using Vice.Display;
using Vice.Display.Rendering;
using Vice.Options;

namespace Vice;

public sealed class ViceAppBuilder
{
    private readonly string _name;
    private readonly string _version;
    private string? _description;
    private IConsoleWriter? _console;
    private IStatusDisplay? _status;
    private TerminalCapabilities? _capabilities;
    private int _concurrency = 3;
    private IViceLogger? _logger;
    private TimeSpan _shutdownTimeout = TimeSpan.FromSeconds(10);
    private readonly List<IJobRunner> _jobRunners = new();
    private readonly Dictionary<Type, object> _sessionServices = new();

    internal ViceAppBuilder(string name, string version)
    {
        _name = name;
        _version = version;
    }

    public ViceAppBuilder WithDescription(string description)
    {
        _description = description;
        return this;
    }

    public ViceAppBuilder WithStatusDisplay(IStatusDisplay status)
    {
        _status = status;
        return this;
    }

    public ViceAppBuilder WithConsoleWriter(IConsoleWriter writer)
    {
        _console = writer;
        return this;
    }

    public ViceAppBuilder WithCapabilities(TerminalCapabilities capabilities)
    {
        _capabilities = capabilities;
        return this;
    }

    public ViceAppBuilder WithConcurrency(int maxConcurrency)
    {
        _concurrency = maxConcurrency;
        return this;
    }

    public ViceAppBuilder WithLogger(IViceLogger logger)
    {
        _logger = logger;
        return this;
    }

    public ViceAppBuilder WithShutdownTimeout(TimeSpan timeout)
    {
        _shutdownTimeout = timeout;
        return this;
    }

    public ViceAppBuilder WithJobRunner(IJobRunner runner)
    {
        _jobRunners.Add(runner);
        return this;
    }

    public ViceAppBuilder WithJobRunners(IEnumerable<IJobRunner> runners)
    {
        _jobRunners.AddRange(runners);
        return this;
    }

    public ViceAppBuilder WithSessionService<T>(T instance) where T : class
    {
        _sessionServices[typeof(T)] = instance;
        return this;
    }

    private readonly List<GlobalOption> _globalOptions = new();

    public ViceAppBuilder WithGlobalOption(params GlobalOption[] options)
    {
        _globalOptions.AddRange(options);
        return this;
    }

    public IViceApp Build()
    {
        return new ViceApp(_name, _version, _description,
            console: _console,
            status: _status, capabilities: _capabilities,
            concurrency: _concurrency,
            jobRunners: _jobRunners.ToArray(),
            sessionServices: new Dictionary<Type, object>(_sessionServices),
            globalOptions: _globalOptions.ToArray(),
            logger: _logger,
            shutdownTimeout: _shutdownTimeout);
    }
}

using Vice.Display;
using Vice.Display.Rendering;
using Vice.Jobs;
using Vice.Logging;

namespace Vice.Session;

internal sealed class SessionLoop : ISessionLoop
{
    private readonly CommandExecutor _executor;
    private readonly string _jobsRoot;
    private readonly InputHistory _history;
    private readonly IConsoleWriter _console;
    private readonly TextReader _reader;
    private readonly IViceLogger _logger;
    private readonly string _prompt;
    private readonly HashSet<int> _announcedTerminal = new();
    private bool _seeded;

    internal const int EXIT_SENTINEL = int.MinValue + 1;

    internal const int EXIT_SIGNAL = EXIT_SENTINEL;

    public SessionLoop(
        CommandExecutor executor,
        string jobsRoot,
        InputHistory history,
        IConsoleWriter console,
        TextReader reader,
        IViceLogger? logger = null,
        string prompt = "vice> ")
    {
        _executor = executor;
        _jobsRoot = jobsRoot;
        _history = history;
        _console = console;
        _reader = reader;
        _logger = logger ?? NullViceLogger.Instance;
        _prompt = prompt;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await AnnounceTerminalJobsAsync(ct).ConfigureAwait(false);

            _console.Write(_prompt);

            string? line;
            try
            {
                line = await Task.Factory.StartNew(() => _reader.ReadLine(),
                                                   ct,
                                                   TaskCreationOptions.LongRunning,
                                                   TaskScheduler.Default).WaitAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (line is null)
            {
                return;
            }

            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                continue;
            }

            await _history.AppendAsync(trimmed, ct).ConfigureAwait(false);

            int exitCode;
            try
            {
                exitCode = await _executor.ExecuteAsync(trimmed, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.Log(ViceLogLevel.Error, "REPL handler exception", ex);
                _console.WriteError($"Error: {ex.Message} (run with VICE_LOG_LEVEL=debug for stack)");
                continue;
            }

            if (exitCode == EXIT_SENTINEL)
            {
                return;
            }
        }
    }

    private async Task AnnounceTerminalJobsAsync(CancellationToken ct)
    {
        IReadOnlyList<JobState> records;
        try
        {
            records = await JobLedger.ReadAllAsync(_jobsRoot, _logger, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Log(ViceLogLevel.Debug, "job ledger scan failed", ex);
            return;
        }

        if (!_seeded)
        {
            foreach (var record in records)
            {
                if (record.Status != JobStatus.Running)
                {
                    _announcedTerminal.Add(record.Id);
                }
            }

            _seeded = true;
            return;
        }

        foreach (var record in records)
        {
            if (record.Status == JobStatus.Running
                || !_announcedTerminal.Add(record.Id))
            {
                continue;
            }

            if (record.Status == JobStatus.Completed)
            {
                _console.WriteLine($"  Job #{record.Id} completed: {JobLabel(record)}");
            }
            else
            {
                _console.WriteLine($"  Job #{record.Id} failed: {JobLabel(record)} -- {record.ErrorMessage ?? "unknown error"}");
            }
        }
    }

    private static string JobLabel(JobState job)
    {
        return string.IsNullOrEmpty(job.Label) ? job.Kind.Name : job.Label;
    }
}

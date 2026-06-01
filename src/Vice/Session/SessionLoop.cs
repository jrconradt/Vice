using System.Threading.Channels;
using Vice.Display;
using Vice.Jobs;
using Vice.Logging;

namespace Vice.Session;

internal sealed class SessionLoop : ISessionLoop, IDisposable
{
    private readonly CommandExecutor _executor;
    private readonly JobManager _jobManager;
    private readonly InputHistory _history;
    private readonly IConsoleWriter _console;
    private readonly TextReader _reader;
    private readonly string _prompt;

    private readonly Channel<string> _notifications = Channel.CreateBounded<string>(
        new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });
    private readonly Action<JobState> _onJobCompletedHandler;
    private readonly Action<JobState, string> _onJobFailedHandler;
    private volatile bool _disposed;

    internal const int EXIT_SENTINEL = int.MinValue + 1;

    internal const int EXIT_SIGNAL = EXIT_SENTINEL;

    public SessionLoop(
        CommandExecutor executor,
        JobManager jobManager,
        InputHistory history,
        IConsoleWriter console,
        TextReader reader,
        IViceLogger? logger = null,
        string prompt = "vice> ")
    {
        _ = logger;
        _executor = executor;
        _jobManager = jobManager;
        _history = history;
        _console = console;
        _reader = reader;
        _prompt = prompt;

        _onJobCompletedHandler = OnJobCompleted;
        _onJobFailedHandler = OnJobFailed;
        _jobManager.JobCompleted += _onJobCompletedHandler;
        _jobManager.JobFailed += _onJobFailedHandler;
    }

    public async Task<bool> RunAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                DrainNotifications();

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
                    return false;
                }

                if (line is null)
                {
                    if (ShouldDaemonize())
                    {
                        Dispose();
                        return true;
                    }
                    return false;
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
                    return false;
                }
                catch (Exception ex)
                {
                    Vice.Log.Emit(ViceLogLevel.Error, "REPL handler exception", ex);
                    _console.WriteError($"Error: {ex.Message} (run with VICE_LOG_LEVEL=debug for stack)");
                    continue;
                }

                if (exitCode == EXIT_SENTINEL)
                {
                    if (ShouldDaemonize())
                    {
                        Dispose();
                        return true;
                    }
                    return false;
                }
            }

            return false;
        }
        finally
        {
            if (!_disposed)
            {
                Dispose();
            }
        }
    }

    private bool ShouldDaemonize()
    {
        var jobs = _jobManager.GetJobs();
        var activeJobs = jobs.Count(j => j.Status is JobStatus.Running or JobStatus.Queued);
        if (activeJobs > 0)
        {
            _console.WriteLine($"Continuing with {activeJobs} active job(s) in this same process; jobs keep running while this terminal stays open.");
            _console.WriteLine("Closing the terminal sends SIGHUP and stops the jobs. Run under nohup/systemd/supervisord, or 'vice daemon', for terminal-independent persistence.");
            _console.WriteLine("Run 'vice' to reconnect.");
            return true;
        }
        return false;
    }

    private void DrainNotifications()
    {
        while (_notifications.Reader.TryRead(out var msg))
        {
            _console.WriteLine(msg);
        }
    }

    private void Publish(string message)
    {
        if (!_notifications.Writer.TryWrite(message))
        {
            Vice.Log.Emit(ViceLogLevel.Debug, $"Notification channel closed; dropped: {message}");
        }
    }

    private void OnJobCompleted(JobState job)
    {
        if (_disposed)
        {
            return;
        }

        var view = JobView.From(job);
        Publish($"  Job #{view.Id} completed: {view.Label}");
    }

    private void OnJobFailed(JobState job, string error)
    {
        if (_disposed)
        {
            return;
        }

        var view = JobView.From(job);
        Publish($"  Job #{view.Id} failed: {view.Label} -- {error}");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _jobManager.JobCompleted -= _onJobCompletedHandler;
        _jobManager.JobFailed -= _onJobFailedHandler;
        _disposed = true;
        _notifications.Writer.TryComplete();
        DrainNotifications();
    }
}

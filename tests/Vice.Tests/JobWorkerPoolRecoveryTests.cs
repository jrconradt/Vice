using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vice.Jobs;
using Vice.Logging;
using Xunit;

namespace Vice.Tests;

public class JobWorkerPoolRecoveryTests
{
    private const string PER_JOB_WARN = "worker observed unhandled exception";
    private const string RESTART_MARKER = "restarting";
    private const string DEGRADED_MARKER = "POOL_DEGRADED job worker slot";
    private const string SHUTDOWN_MARKER = "terminated during shutdown";

    private sealed class FaultInjectingLogger : IViceLogger
    {
        private readonly ConcurrentQueue<string> _messages = new();
        private readonly Func<string, bool> _shouldFault;
        private readonly Action? _beforeFault;

        public FaultInjectingLogger(Func<string, bool> shouldFault, Action? beforeFault = null)
        {
            _shouldFault = shouldFault;
            _beforeFault = beforeFault;
        }

        public string[] Messages => _messages.ToArray();

        public bool IsEnabled(ViceLogLevel level) => true;

        public void Log(ViceError error)
        {
        }

        public void Log(
            ViceLogLevel level,
            string message,
            Exception? exception = null,
            string? caller = null,
            string? file = null,
            int line = 0)
        {
            _messages.Enqueue(message);
            if (_shouldFault(message))
            {
                _beforeFault?.Invoke();
                throw new InvalidOperationException("injected worker-fatal fault");
            }
        }
    }

    private static JobStateHolder QueuedHolder(int id)
        => new JobStateHolder(new JobState
        {
            Id = id,
            Status = JobStatus.Queued,
        });

    private static async Task WaitForMessageAsync(FaultInjectingLogger logger, string marker)
    {
        while (!logger.Messages.Any(m => m.Contains(marker)))
        {
            await Task.Yield();
        }
    }

    [Fact]
    public async Task RecoverWorker_TransientFatalFault_RestartsWorker_AndStaysLive()
    {
        var faulted = 0;
        var logger = new FaultInjectingLogger(message =>
        {
            if (message.Contains(PER_JOB_WARN)
                && Interlocked.CompareExchange(ref faulted, 1, 0) == 0)
            {
                return true;
            }

            return false;
        });

        var pool = new JobWorkerPool(
            1,
            _ => throw new InvalidOperationException("job blew up"),
            CancellationToken.None,
            TimeSpan.FromSeconds(5),
            logger);

        await pool.EnqueueAsync(QueuedHolder(1), CancellationToken.None);

        await WaitForMessageAsync(logger, RESTART_MARKER);

        Assert.Contains(logger.Messages, m => m.Contains("POOL_WORKER_TERMINATED")
                                              && m.Contains(RESTART_MARKER));
        Assert.False(pool.IsDegraded);
        Assert.Equal(1, pool.LiveWorkerCount);

        await pool.DrainAsync();
    }

    [Fact]
    public async Task RecoverWorker_RestartBudgetExhausted_ReportsDegraded_AndLogsPoolDegraded()
    {
        var logger = new FaultInjectingLogger(message => message.Contains(PER_JOB_WARN));

        var pool = new JobWorkerPool(
            1,
            _ => throw new InvalidOperationException("job blew up"),
            CancellationToken.None,
            TimeSpan.FromSeconds(5),
            logger);

        for (var i = 0; i < JobWorkerPool.MAX_WORKER_RESTARTS + 1; i++)
        {
            await pool.EnqueueAsync(QueuedHolder(i), CancellationToken.None);
        }

        await WaitForMessageAsync(logger, DEGRADED_MARKER);

        Assert.Contains(logger.Messages, m => m.Contains(DEGRADED_MARKER)
                                              && m.Contains("restart budget"));
        Assert.True(pool.IsDegraded);
        Assert.Equal(0, pool.LiveWorkerCount);
        Assert.Equal(1, pool.ConfiguredConcurrency);

        await pool.DrainAsync();
    }

    [Fact]
    public async Task RecoverWorker_FatalFaultDuringShutdown_LogsWorkerTerminated_WithoutRestart()
    {
        using var shutdown = new CancellationTokenSource();
        var logger = new FaultInjectingLogger(
            message => message.Contains(PER_JOB_WARN),
            beforeFault: () => shutdown.Cancel());

        var pool = new JobWorkerPool(
            1,
            _ => throw new InvalidOperationException("job blew up"),
            shutdown.Token,
            TimeSpan.FromSeconds(5),
            logger);

        await pool.EnqueueAsync(QueuedHolder(1), CancellationToken.None);

        await WaitForMessageAsync(logger, SHUTDOWN_MARKER);

        Assert.Contains(logger.Messages, m => m.Contains("POOL_WORKER_TERMINATED")
                                              && m.Contains(SHUTDOWN_MARKER));
        Assert.DoesNotContain(logger.Messages, m => m.Contains(RESTART_MARKER));
        Assert.True(pool.IsDegraded);
        Assert.Equal(0, pool.LiveWorkerCount);

        await pool.DrainAsync();
    }
}

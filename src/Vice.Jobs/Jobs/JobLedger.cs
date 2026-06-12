using System.Diagnostics;
using System.Text.Json;
using Vice.Logging;
using Vice.Persistence;

namespace Vice.Jobs;

public static class JobLedger
{
    public const int MAX_RETAINED_RECORDS = 100;

    private static readonly TimeSpan StartTimeTolerance = TimeSpan.FromSeconds(5);

    public static string RootFor(string appName)
    {
        var stateHome = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
        var baseDir = string.IsNullOrWhiteSpace(stateHome)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "vice")
            : Path.Combine(stateHome.Trim(), "vice");
        return Path.Combine(baseDir, $"{appName}-jobs");
    }

    public static string RecordPath(string root, int id)
        => Path.Combine(root, $"{id}.json");

    public static async Task WriteAsync(string root,
                                        JobState state,
                                        CancellationToken ct)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(state, JobJsonContext.Default.JobState);
        await AtomicFile.WriteAllBytesAsync(RecordPath(root, state.Id), payload, ct).ConfigureAwait(false);
    }

    public static async Task<JobState?> ReadAsync(string root,
                                                  int id,
                                                  IViceLogger logger,
                                                  CancellationToken ct)
    {
        var payload = await AtomicFile.ReadAllBytesOrNullAsync(RecordPath(root, id), ct).ConfigureAwait(false);
        if (payload is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(payload, JobJsonContext.Default.JobState);
        }
        catch (JsonException ex)
        {
            logger.Log(ViceLogLevel.Warn, $"job record {RecordPath(root, id)} is unreadable; ignoring", ex);
            return null;
        }
    }

    public static async Task<IReadOnlyList<JobState>> ReadAllAsync(string root,
                                                                   IViceLogger logger,
                                                                   CancellationToken ct)
    {
        if (!Directory.Exists(root))
        {
            return Array.Empty<JobState>();
        }

        var records = new List<JobState>();
        foreach (var path in Directory.EnumerateFiles(root, "*.json"))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (!int.TryParse(name, out var id))
            {
                continue;
            }

            var record = await ReadAsync(root, id, logger, ct).ConfigureAwait(false);
            if (record is not null)
            {
                records.Add(Reconcile(record));
            }
        }

        records.Sort(static (a, b) => a.CreatedAt.CompareTo(b.CreatedAt));
        return records;
    }

    public static JobState Reconcile(JobState record)
    {
        if (record.Status != JobStatus.Running
            || IsProcessAlive(record.Id, record.ProcessStartTimeUtc))
        {
            return record;
        }

        return record with
        {
            Status = JobStatus.Failed,
            ErrorMessage = "process exited without recording a result",
        };
    }

    public static bool IsProcessAlive(int pid, DateTime? recordedStartUtc)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (process.HasExited)
            {
                return false;
            }

            if (recordedStartUtc is not { } recorded)
            {
                return true;
            }

            var drift = process.StartTime.ToUniversalTime() - recorded;
            return drift > -StartTimeTolerance
                && drift < StartTimeTolerance;
        }
        catch (Exception ex) when (ex is ArgumentException
                                       or InvalidOperationException
                                       or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    public static async Task MarkCancelledAsync(string root,
                                                JobState record,
                                                CancellationToken ct)
    {
        var cancelled = record with
        {
            Status = JobStatus.Failed,
            ErrorMessage = "Cancelled",
            CompletedAt = DateTime.UtcNow,
        };
        await WriteAsync(root, cancelled, ct).ConfigureAwait(false);
    }

    public static void Prune(string root, IReadOnlyList<JobState> records)
    {
        var terminal = records.Where(static r => r.Status != JobStatus.Running)
            .OrderByDescending(static r => r.CompletedAt ?? r.CreatedAt)
            .Skip(MAX_RETAINED_RECORDS)
            .ToList();

        foreach (var stale in terminal)
        {
            SafeFile.TryDelete(RecordPath(root, stale.Id));
        }
    }
}

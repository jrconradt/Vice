using System.Text;
using System.Text.Json;
using Vice.Logging;
using Vice.Persistence;

namespace Vice.Jobs;

internal sealed class JobPersistence : IJobPersistence
{
    private const int MaxRetainedQuarantineFiles = 5;

    private readonly string _jobsFilePath;

    public JobPersistence(string jobsFilePath, IViceLogger? logger = null)
    {
        _ = logger;
        _jobsFilePath = jobsFilePath;
    }

    public async Task<List<JobState>> LoadAsync(CancellationToken ct)
    {
        var json = await AtomicFile.ReadAllTextOrNullAsync(_jobsFilePath, ct).ConfigureAwait(false);
        if (json is null)
        {
            Vice.Log.Emit(ViceLogLevel.Info, "no prior jobs.json, starting empty");
            return new List<JobState>();
        }
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<JobState>();
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            await QuarantineCorruptAsync(json, ex, ct).ConfigureAwait(false);
            return new List<JobState>();
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                await QuarantineCorruptAsync(json,
                                            new JsonException($"expected a JSON array, found {document.RootElement.ValueKind}"),
                                            ct).ConfigureAwait(false);
                return new List<JobState>();
            }

            var jobs = new List<JobState>();
            var skipped = 0;
            foreach (var element in document.RootElement.EnumerateArray())
            {
                JobState? job;
                try
                {
                    job = JsonSerializer.Deserialize(element.GetRawText(), JobPersistenceJsonContext.Default.JobState);
                }
                catch (JsonException ex)
                {
                    skipped++;
                    Vice.Log.Emit(ViceLogLevel.Warn, "skipping unparseable job record in jobs.json", ex);
                    continue;
                }

                if (job is not null)
                {
                    jobs.Add(job);
                }
            }

            if (skipped > 0)
            {
                Vice.Log.Emit(ViceLogLevel.Warn,
                              $"loaded {jobs.Count} job(s) from jobs.json; skipped {skipped} unparseable record(s)");
            }

            return jobs;
        }
    }

    public async Task SaveAsync(IReadOnlyList<JobState> jobs, CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(_jobsFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var list = jobs as List<JobState> ?? new List<JobState>(jobs);
        var json = JsonSerializer.Serialize(list, JobPersistenceJsonContext.Default.ListJobState);
        await AtomicFile.WriteAllTextAsync(_jobsFilePath, json, ct).ConfigureAwait(false);
    }

    private async Task QuarantineCorruptAsync(string payload, Exception ex, CancellationToken ct)
    {
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssZ");
        var corruptPath = $"{_jobsFilePath}.corrupt-{stamp}";
        try
        {
            var options = new FileStreamOptions
            {
                Mode = FileMode.Create,
                Access = FileAccess.Write,
            };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            await using (var fs = new FileStream(corruptPath, options))
            {
                var bytes = Encoding.UTF8.GetBytes(payload);
                await fs.WriteAsync(bytes, ct).ConfigureAwait(false);
                await fs.FlushAsync(ct).ConfigureAwait(false);
            }
            Vice.Persistence.FileAccessControl.RestrictToCurrentUser(corruptPath);
            PruneQuarantineFiles();
        }
        catch (Exception writeEx)
        {
            Vice.Log.Emit(ViceLogLevel.Warn, $"failed to quarantine corrupt jobs.json to {corruptPath}", writeEx);
        }
        Vice.Log.Emit(ViceLogLevel.Warn, $"jobs.json deserialization failed, starting empty (quarantined to {corruptPath})", ex);
    }

    private void PruneQuarantineFiles()
    {
        var directory = Path.GetDirectoryName(_jobsFilePath);
        if (string.IsNullOrEmpty(directory))
        {
            return;
        }

        var pattern = $"{Path.GetFileName(_jobsFilePath)}.corrupt-*";
        string[] existing;
        try
        {
            existing = Directory.GetFiles(directory, pattern);
        }
        catch (Exception enumEx)
        {
            Vice.Log.Emit(ViceLogLevel.Warn, "failed to enumerate quarantine files for pruning", enumEx);
            return;
        }

        if (existing.Length <= MaxRetainedQuarantineFiles)
        {
            return;
        }

        var ordered = existing
            .OrderByDescending(p => p, StringComparer.Ordinal)
            .Skip(MaxRetainedQuarantineFiles);

        foreach (var stale in ordered)
        {
            try
            {
                File.Delete(stale);
            }
            catch (Exception deleteEx)
            {
                Vice.Log.Emit(ViceLogLevel.Warn, $"failed to prune stale quarantine file {stale}", deleteEx);
            }
        }
    }
}

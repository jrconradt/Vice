using System.Text;
using System.Text.Json;
using Vice.Logging;
using Vice.Persistence;

namespace Vice.Jobs;

internal sealed class JobPersistence : IJobPersistence
{
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

        try
        {
            return JsonSerializer.Deserialize(json, JobPersistenceJsonContext.Default.ListJobState) ?? new List<JobState>();
        }
        catch (JsonException ex)
        {
            await QuarantineCorruptAsync(json, ex, ct).ConfigureAwait(false);
            return new List<JobState>();
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
        }
        catch (Exception writeEx)
        {
            Vice.Log.Emit(ViceLogLevel.Warn, $"failed to quarantine corrupt jobs.json to {corruptPath}", writeEx);
        }
        Vice.Log.Emit(ViceLogLevel.Warn, $"jobs.json deserialization failed, starting empty (quarantined to {corruptPath})", ex);
    }
}

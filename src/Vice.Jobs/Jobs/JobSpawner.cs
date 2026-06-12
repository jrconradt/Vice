using System.Diagnostics;
using System.Text.Json;
using Vice.Logging;

namespace Vice.Jobs;

public sealed class JobSpawner : IJobSubmitter
{
    private const string DESTINATION_PATH_KEY = "destinationPath";

    private readonly string _root;
    private readonly string _executablePath;
    private readonly IViceLogger _logger;

    public JobSpawner(string appName,
                      IViceLogger? logger = null,
                      string? executablePath = null)
    {
        if (string.IsNullOrWhiteSpace(appName))
        {
            throw new ArgumentException("appName must be non-empty.", nameof(appName));
        }

        _root = JobLedger.RootFor(appName);
        _executablePath = executablePath
            ?? Environment.ProcessPath
            ?? throw new InvalidOperationException("Cannot determine the executable path for job spawning.");
        _logger = logger ?? NullViceLogger.Instance;
    }

    public async Task<int> SubmitAsync(JobDescriptor descriptor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        var records = await JobLedger.ReadAllAsync(_root, _logger, ct).ConfigureAwait(false);
        RejectDestinationCollision(descriptor, records);
        JobLedger.Prune(_root, records);

        var json = JsonSerializer.Serialize(descriptor, JobJsonContext.Default.JobDescriptor);
        var startInfo = new ProcessStartInfo
        {
            FileName = _executablePath,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("job");
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add($"\"{json}\"");
        startInfo.ArgumentList.Add("--quiet");
        startInfo.ArgumentList.Add("--no-status");
        startInfo.ArgumentList.Add("--non-interactive");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Job process failed to start: '{_executablePath}'.");
        var id = process.Id;
        _logger.Log(ViceLogLevel.Info,
                    $"job spawned pid={id} kind={descriptor.Kind} label={descriptor.Label}");
        return id;
    }

    private static void RejectDestinationCollision(JobDescriptor descriptor,
                                                   IReadOnlyList<JobState> records)
    {
        if (!descriptor.Options.TryGetValue(DESTINATION_PATH_KEY, out var destination)
            || string.IsNullOrEmpty(destination))
        {
            return;
        }

        var requested = Path.GetFullPath(destination);
        foreach (var record in records)
        {
            if (record.Status != JobStatus.Running
                || !record.Options.TryGetValue(DESTINATION_PATH_KEY, out var owned)
                || string.IsNullOrEmpty(owned))
            {
                continue;
            }

            if (string.Equals(Path.GetFullPath(owned), requested, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Job submission rejected: destination '{requested}' is already owned by live job #{record.Id}; concurrent downloads to the same destination would collide on its .partial file.");
            }
        }
    }
}

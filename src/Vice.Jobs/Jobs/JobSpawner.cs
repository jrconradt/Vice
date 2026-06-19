using System.Diagnostics;
using System.Text.Json;
using Vice.Logging;

namespace Vice.Jobs;

public sealed class JobSpawner : IJobSubmitter
{
    private readonly string _executablePath;
    private readonly IViceLogger _logger;

    public JobSpawner(IViceLogger? logger = null,
                      string? executablePath = null)
    {
        _executablePath = executablePath
            ?? Environment.ProcessPath
            ?? throw new InvalidOperationException("Cannot determine the executable path for job spawning.");
        _logger = logger ?? NullViceLogger.Instance;
    }

    public Task<int> SubmitAsync(JobDescriptor descriptor, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

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
        return Task.FromResult(id);
    }
}

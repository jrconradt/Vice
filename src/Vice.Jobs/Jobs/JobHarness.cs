using System.Runtime.InteropServices;
using System.Text.Json;
using Vice.Foundation.Execution;
using Vice.Logging;

namespace Vice.Jobs;

public static class JobHarness
{
    public static async Task<int> RunAsync(IReadOnlyList<IJobRunner> runners,
                                           string descriptorJson,
                                           IViceLogger? logger,
                                           CancellationToken ct)
    {
        var log = logger ?? NullViceLogger.Instance;

        JobDescriptor? descriptor;
        try
        {
            descriptor = JsonSerializer.Deserialize(descriptorJson, JobJsonContext.Default.JobDescriptor);
        }
        catch (JsonException ex)
        {
            log.Log(ViceLogLevel.Error, "job descriptor is not valid JSON", ex);
            return ViceExitCode.USAGE_ERROR;
        }

        if (descriptor is null)
        {
            log.Log(ViceLogLevel.Error, "job descriptor deserialized to null");
            return ViceExitCode.USAGE_ERROR;
        }

        var runner = FindRunner(runners, descriptor.Kind);
        if (runner is null)
        {
            log.Log(ViceLogLevel.Error, $"job has no runner for kind '{descriptor.Kind}'");
            return ViceExitCode.FAILURE;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var sighup = PosixSignalRegistration.Create(PosixSignal.SIGHUP, static context => context.Cancel = true);
        using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
        {
            context.Cancel = true;
            cts.Cancel();
        });

        try
        {
            await runner.RunAsync(descriptor, cts.Token).ConfigureAwait(false);
            log.Log(ViceLogLevel.Info, $"job terminal kind={descriptor.Kind} status=Completed label={descriptor.Label}");
            return ViceExitCode.SUCCESS;
        }
        catch (OperationCanceledException)
        {
            log.Log(ViceLogLevel.Info, $"job terminal kind={descriptor.Kind} status=Cancelled label={descriptor.Label}");
            return ViceExitCode.INTERRUPTED;
        }
        catch (Exception ex)
        {
            log.Log(ViceLogLevel.Warn, $"job terminal kind={descriptor.Kind} status=Failed label={descriptor.Label} error={ex.Message}", ex);
            return ViceExitCode.FAILURE;
        }
    }

    private static IJobRunner? FindRunner(IReadOnlyList<IJobRunner> runners, JobKind kind)
    {
        foreach (var runner in runners)
        {
            if (runner.CanHandle(kind))
            {
                return runner;
            }
        }

        return null;
    }
}

using System.Diagnostics;
using Vice.Logging;
using Vice.Display;
using Vice.Display.Rendering;
using Vice.Session;
using Vice.Streaming;

namespace Vice.Execution;

internal sealed class PipelineExecutor
{
    private readonly SessionContext? _session;
    private readonly IViceLogger _logger;

    public PipelineExecutor(SessionContext? session, IViceLogger logger)
    {
        _session = session;
        _logger = logger ?? NullViceLogger.Instance;
    }

    private static PipelineOperator ClassifyOperator(string? word) => word switch
    {
        "or" => PipelineOperator.Or,
        "and" => PipelineOperator.And,
        _ => PipelineOperator.Then,
    };

    public async Task<int> ExecuteAsync(
        List<PipelineStage> stages,
        IReadOnlyDictionary<string, string?> globalOptions,
        IConsoleWriter console,
        IStatusDisplay status,
        TerminalCapabilities capabilities,
        CancellationToken ct)
    {
        string? previousOutput = null;
        int lastExitCode = 0;

        for (int i = 0; i < stages.Count; i++)
        {
            var stage = stages[i];
            var op = ClassifyOperator(stage.OperatorWord);

            switch (op)
            {
                case PipelineOperator.Or:
                    {
                        var (newExit, newOutput) = await ExecuteOr(
                            stage, lastExitCode, previousOutput,
                            globalOptions, console, status, capabilities, i + 1, stages.Count, ct);
                        lastExitCode = newExit;
                        previousOutput = newOutput;
                        break;
                    }
                case PipelineOperator.And:
                    {
                        if (lastExitCode != 0)
                        {
                            return lastExitCode;
                        }

                        var (newExit, newOutput) = await ExecuteAnd(
                            stage, previousOutput,
                            globalOptions, console, status, capabilities, i + 1, stages.Count, ct);
                        lastExitCode = newExit;
                        previousOutput = newOutput;
                        break;
                    }
                default:
                    {
                        if (i > 0 && lastExitCode != 0)
                        {
                            return lastExitCode;
                        }

                        var (newExit, newOutput, advance) = await ExecuteThenOrStreamingPair(
                            stages, i, previousOutput,
                            globalOptions, console, status, capabilities, ct);
                        lastExitCode = newExit;
                        previousOutput = newOutput;
                        i += advance;
                        break;
                    }
            }
        }

        return lastExitCode;
    }

    private async Task<(int ExitCode, string? Output, int Advance)> ExecuteThenOrStreamingPair(
        List<PipelineStage> stages, int i, string? previousOutput,
        IReadOnlyDictionary<string, string?> globalOptions,
        IConsoleWriter console, IStatusDisplay status, TerminalCapabilities capabilities,
        CancellationToken ct)
    {
        var stage = stages[i];
        var consumerStage = (i + 1 < stages.Count) ? stages[i + 1] : null;
        if (CanStreamPair(stage, consumerStage))
        {
            var result = await ExecuteStreamingPair(
                stage, consumerStage!, globalOptions, console, status, capabilities,
                previousOutput, i + 1, stages.Count, ct);
            return (result.ExitCode, result.Output, 1);
        }

        var (newExit, newOutput) = await ExecuteThen(
            stage, previousOutput,
            globalOptions, console, status, capabilities, i + 1, stages.Count, ct);
        return (newExit, newOutput, 0);
    }

    private async Task<(int ExitCode, string? Output)> ExecuteOr(
        PipelineStage stage, int lastExitCode, string? previousOutput,
        IReadOnlyDictionary<string, string?> globalOptions,
        IConsoleWriter console, IStatusDisplay status, TerminalCapabilities capabilities,
        int stageNumber, int totalStages, CancellationToken ct)
    {
        if (lastExitCode == 0)
        {
            return (lastExitCode, previousOutput);
        }

        var result = await ExecuteStage(stage, globalOptions, console, status, capabilities,
            previousOutput, stageNumber, totalStages, ct);
        return (result.ExitCode, result.Output);
    }

    private async Task<(int ExitCode, string? Output)> ExecuteAnd(
        PipelineStage stage, string? previousOutput,
        IReadOnlyDictionary<string, string?> globalOptions,
        IConsoleWriter console, IStatusDisplay status, TerminalCapabilities capabilities,
        int stageNumber, int totalStages, CancellationToken ct)
    {
        var result = await ExecuteStage(stage, globalOptions, console, status, capabilities,
            previousOutput, stageNumber, totalStages, ct);
        return (result.ExitCode, (previousOutput ?? "") + result.Output);
    }

    private async Task<(int ExitCode, string? Output)> ExecuteThen(
        PipelineStage stage, string? previousOutput,
        IReadOnlyDictionary<string, string?> globalOptions,
        IConsoleWriter console, IStatusDisplay status, TerminalCapabilities capabilities,
        int stageNumber, int totalStages, CancellationToken ct)
    {
        var result = await ExecuteStage(stage, globalOptions, console, status, capabilities,
            previousOutput, stageNumber, totalStages, ct);
        return (result.ExitCode, result.Output);
    }

    private static bool CanStreamPair(PipelineStage producer, PipelineStage? consumer)
    {
        if (consumer is null)
        {
            return false;
        }

        if (consumer.OperatorWord is "or")
        {
            return false;
        }

        if (producer.Mode != StageMode.StreamProducer)
        {
            return false;
        }

        if (consumer.Mode != StageMode.StreamConsumer)
        {
            return false;
        }

        var producerLauncher = producer.Launcher;
        var consumerLauncher = consumer.Launcher;
        if (producerLauncher is null || consumerLauncher is null)
        {
            return false;
        }

        if (!producerLauncher.HasProducer || !consumerLauncher.HasConsumer)
        {
            return false;
        }

        if (producerLauncher.ItemType != consumerLauncher.ItemType)
        {
            return false;
        }

        return true;
    }

    private async Task<PipelineResult> ExecuteStreamingPair(
        PipelineStage producer, PipelineStage consumer,
        IReadOnlyDictionary<string, string?> globalOptions,
        IConsoleWriter console,
        IStatusDisplay status,
        TerminalCapabilities capabilities,
        string? pipelineInput,
        int stageNumber,
        int totalStages,
        CancellationToken ct)
    {
        var producerLauncher = producer.Launcher!;
        var consumerLauncher = consumer.Launcher!;
        var capacity = producer.Options?.ChannelCapacity
                       ?? consumer.Options?.ChannelCapacity
                       ?? producerLauncher.DefaultChannelCapacity;

        var (channel, disposable) = producerLauncher.CreateChannel(capacity);
        await using (disposable)
        {
            var producerLabel = totalStages > 1 ? $"Stage {stageNumber}/{totalStages}" : "Producing";
            await using var producerHandle = status.Start(producerLabel, console);
            var producerCapturing = new CapturingConsoleWriter(producerHandle.Writer);
            var producerRender = new RenderContext(producerCapturing, capabilities);
            var producerStatus = new Progress<string>(l => producerHandle.UpdateLabel(l));
            var producerProgress = producerHandle.SupportsProgress
                ? new Progress<double>(frac => producerHandle.UpdateProgress(frac))
                : null;
            var producerCtx = new CommandContext(producer.Targets, globalOptions, producerCapturing, pipelineInput, producerStatus, producerRender, producerProgress, _session, _logger, producer.ResolvedNodes) { CancellationToken = ct };

            var consumerLabel = totalStages > 1 ? $"Stage {stageNumber + 1}/{totalStages}" : "Consuming";
            await using var consumerHandle = status.Start(consumerLabel, console);
            var consumerCapturing = new CapturingConsoleWriter(consumerHandle.Writer);
            var consumerRender = new RenderContext(consumerCapturing, capabilities);
            var consumerStatus = new Progress<string>(l => consumerHandle.UpdateLabel(l));
            var consumerProgress = consumerHandle.SupportsProgress
                ? new Progress<double>(frac => consumerHandle.UpdateProgress(frac))
                : null;
            var consumerCtx = new CommandContext(consumer.Targets, globalOptions, consumerCapturing, null, consumerStatus, consumerRender, consumerProgress, _session, _logger, consumer.ResolvedNodes) { CancellationToken = ct };

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var producerTask = RunProducerAsync(producerLauncher, producerCtx, channel, linkedCts.Token);
            var consumerTask = RunConsumerAsync(consumerLauncher, consumerCtx, channel, linkedCts);

            var (consumerExit, consumerEx) = await AwaitCollectingAsync(consumerTask).ConfigureAwait(false);

            var unblockProducer = !linkedCts.IsCancellationRequested && !ct.IsCancellationRequested;
            if (unblockProducer)
            {
                try
                {
                    linkedCts.Cancel();
                }
                catch (ObjectDisposedException ode)
                {
                    System.Diagnostics.Debug.WriteLine(ode);
                }
            }

            var (producerExit, producerEx) = await AwaitCollectingAsync(producerTask).ConfigureAwait(false);

            if (producerEx is OperationCanceledException
                && unblockProducer
                && !ct.IsCancellationRequested)
            {
                producerEx = null;
                producerExit = consumerExit;
            }

            ReportPairOutcome(producerHandle, consumerHandle, producerExit, consumerExit, producerEx, consumerEx);

            var exitCode = producerExit != 0 ? producerExit : consumerExit;
            return new PipelineResult(exitCode, consumerCapturing.CapturedOutput);
        }
    }

    private static async Task<(int Exit, Exception? Error)> AwaitCollectingAsync(Task<int> task)
    {
        try
        {
            var exit = await task.ConfigureAwait(false);
            return (exit, null);
        }
        catch (Exception ex)
        {
            return (0, ex);
        }
    }

    private void ReportPairOutcome(
        IStatusHandle producerHandle, IStatusHandle consumerHandle,
        int producerExit, int consumerExit,
        Exception? producerEx, Exception? consumerEx)
    {
        if (producerEx is not null && consumerEx is not null)
        {
            Vice.Log.Emit(ViceLogLevel.Warn, "secondary stage exception (consumer)", consumerEx);
            producerHandle.Fail();
            consumerHandle.Fail();
            throw producerEx;
        }
        if (producerEx is not null)
        {
            producerHandle.Fail();
            if (consumerExit == 0)
            {
                consumerHandle.Succeed();
            }
            else
            {
                consumerHandle.Fail();
            }

            throw producerEx;
        }
        if (consumerEx is not null)
        {
            if (producerExit == 0)
            {
                producerHandle.Succeed();
            }
            else
            {
                producerHandle.Fail();
            }

            consumerHandle.Fail();
            throw consumerEx;
        }

        if (producerExit == 0)
        {
            producerHandle.Succeed();
        }
        else
        {
            producerHandle.Fail();
        }

        if (consumerExit == 0)
        {
            consumerHandle.Succeed();
        }
        else
        {
            consumerHandle.Fail();
        }
    }

    private static Task<int> RunProducerAsync(
        IStreamingLauncher launcher, CommandContext ctx, object channel, CancellationToken ct)
    {
        return Task.Run(async () =>
        {
            Exception? failure = null;
            try
            {
                return await launcher.InvokeProducerAsync(ctx, channel, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                failure = ex;
                throw;
            }
            finally
            {
                if (failure is null)
                {
                    launcher.CompleteChannel(channel);
                }
                else
                {
                    launcher.FaultChannel(channel, failure);
                }
            }
        }, ct);
    }

    private static Task<int> RunConsumerAsync(
        IStreamingLauncher launcher, CommandContext ctx, object channel, CancellationTokenSource linkedCts)
    {
        return Task.Run(async () =>
        {
            try
            {
                return await launcher.InvokeConsumerAsync(ctx, channel, linkedCts.Token).ConfigureAwait(false);
            }
            catch
            {
                try
                {
                    linkedCts.Cancel();
                }
                catch (ObjectDisposedException ode)
                {
                    System.Diagnostics.Debug.WriteLine(ode);
                }

                throw;
            }
        }, linkedCts.Token);
    }

    private async Task<PipelineResult> ExecuteStage(
        PipelineStage stage,
        IReadOnlyDictionary<string, string?> globalOptions,
        IConsoleWriter console,
        IStatusDisplay status,
        TerminalCapabilities capabilities,
        string? pipelineInput,
        int stageNumber,
        int totalStages,
        CancellationToken ct)
    {
        var label = totalStages > 1 ? $"Stage {stageNumber}/{totalStages}" : stage.OperatorWord ?? "Executing";
        await using var handle = status.Start(label, console);

        var capturing = new CapturingConsoleWriter(handle.Writer);
        var renderCtx = new RenderContext(capturing, capabilities);
        var statusUpdater = new Progress<string>(l => handle.UpdateLabel(l));
        var progressReporter = handle.SupportsProgress
            ? new Progress<double>(frac => handle.UpdateProgress(frac))
            : null;
        var ctx = new CommandContext(stage.Targets, globalOptions, capturing, pipelineInput, statusUpdater, renderCtx, progressReporter, _session, _logger, stage.ResolvedNodes) { CancellationToken = ct };

        var stageName = ResolveStageName(stage, label);
        Vice.Log.Emit(new CommandStarted(stageName));
        var sw = Stopwatch.StartNew();
        int exitCode;
        try
        {
            exitCode = await stage.Handler(ctx, ct);
            sw.Stop();
            Vice.Log.Emit(new CommandCompleted(stageName, exitCode, sw.Elapsed));
        }
        catch (Exception ex)
        {
            sw.Stop();
            if (ex is not ViceError)
            {
                Vice.Log.Emit(new CommandFailed(stageName, ex, sw.Elapsed));
            }
            handle.Fail();
            throw;
        }

        if (exitCode == 0)
        {
            handle.Succeed();
        }
        else
        {
            handle.Fail();
        }

        return new PipelineResult(exitCode, capturing.CapturedOutput);
    }

    private static string ResolveStageName(PipelineStage stage, string fallback)
    {
        if (stage.ResolvedNodes.Count > 0)
        {
            return stage.ResolvedNodes[0].MatchedName;
        }

        return fallback;
    }
}

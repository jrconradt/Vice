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

    private sealed record PipelineRunContext(
        IReadOnlyDictionary<string, string?> GlobalOptions,
        IConsoleWriter Console,
        IStatusDisplay Status,
        TerminalCapabilities Capabilities,
        int TotalStages,
        CancellationToken Ct);

    private sealed class OutputAccumulator
    {
        private readonly List<string> _segments = new();
        private string? _materialized;
        private bool _hasValue;

        public void Replace(string? output)
        {
            _segments.Clear();
            if (output is null)
            {
                _materialized = null;
                _hasValue = false;
                return;
            }

            _segments.Add(output);
            _materialized = output;
            _hasValue = true;
        }

        public void Append(string? output)
        {
            if (output is null)
            {
                return;
            }

            _segments.Add(output);
            _materialized = null;
            _hasValue = true;
        }

        public string? Materialize()
        {
            if (!_hasValue)
            {
                return null;
            }

            if (_materialized is null)
            {
                _materialized = _segments.Count == 1
                    ? _segments[0]
                    : string.Concat(_segments);
            }

            return _materialized;
        }
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
        var context = new PipelineRunContext(
            globalOptions,
            console,
            status,
            capabilities,
            stages.Count,
            ct);

        var accumulated = new OutputAccumulator();
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
                            stage,
                            lastExitCode,
                            accumulated.Materialize(),
                            i + 1,
                            context);
                        lastExitCode = newExit;
                        accumulated.Replace(newOutput);
                        break;
                    }
                case PipelineOperator.And:
                    {
                        if (lastExitCode != 0)
                        {
                            return lastExitCode;
                        }

                        var result = await ExecuteStage(
                            stage,
                            accumulated.Materialize(),
                            i + 1,
                            context);
                        lastExitCode = result.ExitCode;
                        accumulated.Append(result.Output);
                        break;
                    }
                default:
                    {
                        if (i > 0 && lastExitCode != 0)
                        {
                            return lastExitCode;
                        }

                        var (newExit, newOutput, advance) = await ExecuteThenOrStreamingPair(
                            stages,
                            i,
                            accumulated.Materialize(),
                            context);
                        lastExitCode = newExit;
                        accumulated.Replace(newOutput);
                        i += advance;
                        break;
                    }
            }
        }

        return lastExitCode;
    }

    private async Task<(int ExitCode, string? Output, int Advance)> ExecuteThenOrStreamingPair(
        List<PipelineStage> stages,
        int i,
        string? previousOutput,
        PipelineRunContext context)
    {
        var stage = stages[i];
        var consumerStage = (i + 1 < stages.Count) ? stages[i + 1] : null;
        if (CanStreamPair(stage, consumerStage))
        {
            var result = await ExecuteStreamingPair(
                stage,
                consumerStage!,
                previousOutput,
                i + 1,
                context);
            return (result.ExitCode, result.Output, 1);
        }

        var (newExit, newOutput) = await ExecuteThen(
            stage,
            previousOutput,
            i + 1,
            context);
        return (newExit, newOutput, 0);
    }

    private async Task<(int ExitCode, string? Output)> ExecuteOr(
        PipelineStage stage,
        int lastExitCode,
        string? previousOutput,
        int stageNumber,
        PipelineRunContext context)
    {
        if (lastExitCode == 0)
        {
            return (lastExitCode, previousOutput);
        }

        var result = await ExecuteStage(
            stage,
            previousOutput,
            stageNumber,
            context);
        return (result.ExitCode, result.Output);
    }

    private async Task<(int ExitCode, string? Output)> ExecuteThen(
        PipelineStage stage,
        string? previousOutput,
        int stageNumber,
        PipelineRunContext context)
    {
        var result = await ExecuteStage(
            stage,
            previousOutput,
            stageNumber,
            context);
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
        PipelineStage producer,
        PipelineStage consumer,
        string? pipelineInput,
        int stageNumber,
        PipelineRunContext context)
    {
        var producerLauncher = producer.Launcher!;
        var consumerLauncher = consumer.Launcher!;
        var capacity = producer.Options?.ChannelCapacity
                       ?? consumer.Options?.ChannelCapacity
                       ?? producerLauncher.DefaultChannelCapacity;

        var (channel, disposable) = producerLauncher.CreateChannel(capacity);
        await using (disposable)
        {
            var producerLabel = context.TotalStages > 1 ? $"Stage {stageNumber}/{context.TotalStages}" : "Producing";
            await using var producerHandle = context.Status.Start(producerLabel, context.Console);
            var producerCapturing = new CapturingConsoleWriter(producerHandle.Writer);
            var producerRender = new RenderContext(producerCapturing, context.Capabilities);
            var producerStatus = new Progress<string>(l => producerHandle.UpdateLabel(l));
            var producerProgress = producerHandle.SupportsProgress
                ? new Progress<double>(frac => producerHandle.UpdateProgress(frac))
                : null;
            var producerCtx = new CommandContext(
                producer.Targets,
                context.GlobalOptions,
                producerCapturing,
                pipelineInput,
                producerStatus,
                producerRender,
                producerProgress,
                _session,
                _logger,
                producer.ResolvedNodes) { CancellationToken = context.Ct };

            var consumerLabel = context.TotalStages > 1 ? $"Stage {stageNumber + 1}/{context.TotalStages}" : "Consuming";
            await using var consumerHandle = context.Status.Start(consumerLabel, context.Console);
            var consumerCapturing = new CapturingConsoleWriter(consumerHandle.Writer);
            var consumerRender = new RenderContext(consumerCapturing, context.Capabilities);
            var consumerStatus = new Progress<string>(l => consumerHandle.UpdateLabel(l));
            var consumerProgress = consumerHandle.SupportsProgress
                ? new Progress<double>(frac => consumerHandle.UpdateProgress(frac))
                : null;
            var consumerCtx = new CommandContext(
                consumer.Targets,
                context.GlobalOptions,
                consumerCapturing,
                null,
                consumerStatus,
                consumerRender,
                consumerProgress,
                _session,
                _logger,
                consumer.ResolvedNodes) { CancellationToken = context.Ct };

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(context.Ct);
            var producerTask = RunProducerAsync(producerLauncher, producerCtx, channel, linkedCts.Token);
            var consumerTask = RunConsumerAsync(consumerLauncher, consumerCtx, channel, linkedCts);

            var (consumerExit, consumerEx) = await AwaitCollectingAsync(consumerTask).ConfigureAwait(false);

            var unblockProducer = !linkedCts.IsCancellationRequested && !context.Ct.IsCancellationRequested;
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
                && !context.Ct.IsCancellationRequested)
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

    private enum HandleDisposition
    {
        Succeed,
        Fail,
    }

    private static HandleDisposition DispositionFor(Exception? ex, int exit)
    {
        if (ex is not null)
        {
            return HandleDisposition.Fail;
        }

        if (exit == 0)
        {
            return HandleDisposition.Succeed;
        }

        return HandleDisposition.Fail;
    }

    private static void Apply(IStatusHandle handle, HandleDisposition disposition)
    {
        if (disposition == HandleDisposition.Succeed)
        {
            handle.Succeed();
        }
        else
        {
            handle.Fail();
        }
    }

    private void ReportPairOutcome(
        IStatusHandle producerHandle,
        IStatusHandle consumerHandle,
        int producerExit,
        int consumerExit,
        Exception? producerEx,
        Exception? consumerEx)
    {
        if (producerEx is not null && consumerEx is not null)
        {
            Vice.Log.Emit(ViceLogLevel.Warn, "secondary stage exception (consumer)", consumerEx);
            Apply(producerHandle, HandleDisposition.Fail);
            Apply(consumerHandle, HandleDisposition.Fail);
            throw producerEx;
        }

        var producerDisposition = producerEx is not null
            ? HandleDisposition.Fail
            : DispositionFor(null, producerExit);
        var consumerDisposition = consumerEx is not null
            ? HandleDisposition.Fail
            : DispositionFor(null, consumerExit);

        Apply(producerHandle, producerDisposition);
        Apply(consumerHandle, consumerDisposition);

        if (producerEx is not null)
        {
            throw producerEx;
        }

        if (consumerEx is not null)
        {
            throw consumerEx;
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
        string? pipelineInput,
        int stageNumber,
        PipelineRunContext context)
    {
        var label = context.TotalStages > 1 ? $"Stage {stageNumber}/{context.TotalStages}" : stage.OperatorWord ?? "Executing";
        await using var handle = context.Status.Start(label, context.Console);

        var capturing = new CapturingConsoleWriter(handle.Writer);
        var renderCtx = new RenderContext(capturing, context.Capabilities);
        var statusUpdater = new Progress<string>(l => handle.UpdateLabel(l));
        var progressReporter = handle.SupportsProgress
            ? new Progress<double>(frac => handle.UpdateProgress(frac))
            : null;
        var ctx = new CommandContext(
            stage.Targets,
            context.GlobalOptions,
            capturing,
            pipelineInput,
            statusUpdater,
            renderCtx,
            progressReporter,
            _session,
            _logger,
            stage.ResolvedNodes) { CancellationToken = context.Ct };

        var stageName = ResolveStageName(stage, label);
        Vice.Log.Emit(new CommandStarted(stageName));
        var sw = Stopwatch.StartNew();
        int exitCode;
        try
        {
            exitCode = await stage.Handler(ctx, context.Ct);
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

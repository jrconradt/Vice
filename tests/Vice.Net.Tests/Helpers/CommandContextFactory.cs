using Vice.Contracts;
using Vice.Display;
using Vice.Display.Rendering;
using Vice.Execution;
using Vice.Logging;

namespace Vice.Net.Tests;

internal static class CommandContextFactory
{
    public static CommandContext Build(
        IConsoleWriter console,
        IReadOnlyDictionary<string, string>? targets = null,
        IReadOnlyDictionary<string, string?>? globalOptions = null,
        CancellationToken cancellationToken = default)
    {
        var caps = new TerminalCapabilities(
            supportsAnsi: false, supportsColor: false, colorDepth: ColorDepth.None,
            width: 80, isInteractive: false, supportsUnicode: false);
        return new CommandContext(
            targetValues: targets ?? new Dictionary<string, string>(),
            globalOptions: globalOptions ?? new Dictionary<string, string?>(),
            console: console,
            pipelineInput: null,
            statusUpdater: null,
            render: new RenderContext(console, caps),
            progressReporter: null,
            session: null,
            logger: NullViceLogger.Instance)
        { CancellationToken = cancellationToken };
    }
}

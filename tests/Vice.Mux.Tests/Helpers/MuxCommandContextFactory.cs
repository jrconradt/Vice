using Vice.Display;
using Vice.Display.Rendering;
using Vice.Execution;
using Vice.Logging;

namespace Vice.Mux.Tests;

internal sealed class SilentConsoleWriter : IConsoleWriter
{
    public void Write(string text)
    {
    }

    public void WriteLine(string text)
    {
    }

    public void WriteLine()
    {
    }

    public void WriteError(string text)
    {
    }
}

internal static class MuxCommandContextFactory
{
    public static CommandContext Build(
        IReadOnlyDictionary<string, string>? targets = null,
        IReadOnlyDictionary<string, string?>? globalOptions = null,
        CancellationToken cancellationToken = default)
    {
        var console = new SilentConsoleWriter();
        var caps = new TerminalCapabilities(
            supportsAnsi: false,
            supportsColor: false,
            colorDepth: ColorDepth.None,
            width: 80,
            isInteractive: false,
            supportsUnicode: false);
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
        {
            CancellationToken = cancellationToken
        };
    }
}

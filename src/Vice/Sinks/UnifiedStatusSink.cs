using Vice.Display;
using Vice.Display.Rendering;

namespace Vice;

internal sealed class UnifiedStatusSink : IStatusSink
{
    private readonly UnifiedStatusDisplay _display;
    private readonly IConsoleWriter _console;

    public UnifiedStatusSink(TerminalCapabilities capabilities, IConsoleWriter console)
    {
        _display = new UnifiedStatusDisplay(capabilities);
        _console = console;
    }

    public IStatusHandle Start(string label) => _display.Start(label, _console);
}

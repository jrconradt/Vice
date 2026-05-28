using System.Diagnostics;
using Vice.Display.Rendering;

namespace Vice.Display;

internal sealed class UnifiedStatusDisplay : IStatusDisplay
{
    private static readonly string[] Frames = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];
    private const int INTERVAL_MS = 80;

    private readonly TerminalCapabilities _capabilities;
    private readonly bool _stderrIsTty;

    public UnifiedStatusDisplay(TerminalCapabilities capabilities)
    {
        _capabilities = capabilities;
        bool stderrRedirected;
        try
        {
            stderrRedirected = Console.IsErrorRedirected;
        }
        catch (IOException ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            stderrRedirected = true;
        }
        _stderrIsTty = !stderrRedirected;
    }

    public IStatusHandle Start(string label, IConsoleWriter console)
    {
        var handle = new UnifiedHandle(label, console, _capabilities, _stderrIsTty);
        handle.StartAnimation();
        return handle;
    }

    private sealed record Snapshot(string Label, double? Progress);

    private sealed class UnifiedHandle : IStatusHandle
    {
        private Snapshot _state;
        private int _completedFlag;
        private int _lastLineLength;
        private readonly BufferingConsoleWriter _buffered;
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private readonly CancellationTokenSource _cts = new();
        private Task? _animationTask;
        private readonly TerminalCapabilities _caps;
        private readonly bool _useAnsi;

        public IConsoleWriter Writer => _buffered;
        public bool SupportsProgress => true;

        public UnifiedHandle(string label, IConsoleWriter console, TerminalCapabilities caps, bool stderrIsTty)
        {
            _state = new Snapshot(label, null);
            _buffered = new BufferingConsoleWriter(console);
            _caps = caps;
            _useAnsi = stderrIsTty && caps.SupportsAnsi;
        }

        public void StartAnimation()
        {
            if (_useAnsi)
            {
                Console.Error.Write("[?25l");
            }

            if (!_useAnsi)
            {
                _animationTask = Task.CompletedTask;
                return;
            }

            var token = _cts.Token;
            _animationTask = Task.Run(async () =>
            {
                int frame = 0;
                while (!token.IsCancellationRequested)
                {
                    if (Volatile.Read(ref _completedFlag) != 0)
                    {
                        break;
                    }

                    var snap = Volatile.Read(ref _state);
                    var line = RenderLine(Frames[frame], snap);
                    var visibleLength = CalculateVisibleLength(Frames[frame], snap);
                    var padding = Math.Max(0, Volatile.Read(ref _lastLineLength) - visibleLength);
                    Console.Error.Write(padding == 0
                        ? $"\r{line}"
                        : $"\r{line}{new string(' ', padding)}");
                    Volatile.Write(ref _lastLineLength, visibleLength);
                    frame = (frame + 1) % Frames.Length;
                    try
                    {
                        await Task.Delay(INTERVAL_MS, token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }, token);
        }

        public void UpdateLabel(string label)
        {
            Snapshot current, next;
            do
            {
                current = Volatile.Read(ref _state);
                next = current with { Label = label };
            }
            while (!ReferenceEquals(Interlocked.CompareExchange(ref _state, next, current), current));
        }

        public void UpdateProgress(double fraction)
        {
            var clamped = Math.Clamp(fraction, 0.0, 1.0);
            Snapshot current, next;
            do
            {
                current = Volatile.Read(ref _state);
                next = current with { Progress = clamped };
            }
            while (!ReferenceEquals(Interlocked.CompareExchange(ref _state, next, current), current));
        }

        public void Succeed() => Complete(Color.Green, "✓");
        public void Fail() => Complete(Color.Red, "✗");

        private string RenderLine(string frame, Snapshot snap)
        {
            var spinnerText = Colorize(frame, Color.Yellow);

            if (snap.Progress is not { } frac)
            {
                return $"{spinnerText} {snap.Label}";
            }

            var pctStr = $"{frac * 100:F0}%";
            var elapsed = FormatDuration();

            var fixedWidth = frame.Length + 1 + snap.Label.Length + 1 + pctStr.Length + 2 + elapsed.Length + 1;
            var termWidth = _caps.Width > 0 ? _caps.Width : 80;
            var barWidth = termWidth - fixedWidth;

            if (barWidth >= 10)
            {
                var bar = RenderBar(frac, barWidth);
                return $"{spinnerText} {snap.Label} {bar} {pctStr} ({elapsed})";
            }

            return $"{spinnerText} {snap.Label} {pctStr} ({elapsed})";
        }

        private int CalculateVisibleLength(string frame, Snapshot snap)
        {
            if (snap.Progress is not { } frac)
            {
                return frame.Length + 1 + snap.Label.Length;
            }

            var pctStr = $"{frac * 100:F0}%";
            var elapsed = FormatDuration();
            var fixedWidth = frame.Length + 1 + snap.Label.Length + 1 + pctStr.Length + 2 + elapsed.Length + 1;
            var termWidth = _caps.Width > 0 ? _caps.Width : 80;
            var barWidth = termWidth - fixedWidth;

            if (barWidth >= 10)
            {
                return frame.Length + 1 + snap.Label.Length + 1 + barWidth + 1 + pctStr.Length + 2 + elapsed.Length + 1;
            }

            return frame.Length + 1 + snap.Label.Length + 1 + pctStr.Length + 2 + elapsed.Length + 1;
        }

        private string RenderBar(double fraction, int width)
        {
            var filled = (int)Math.Round(fraction * width);
            var empty = width - filled;

            char filledChar, emptyChar;
            if (_caps.SupportsUnicode)
            {
                filledChar = '█';
                emptyChar = '░';
            }
            else
            {
                filledChar = '#';
                emptyChar = '-';
            }

            var filledStr = new string(filledChar, filled);
            var emptyStr = new string(emptyChar, empty);

            return Colorize(filledStr, Color.Green) + Colorize(emptyStr, Color.BrightBlack);
        }

        private void Complete(Color symbolColor, string rawSymbol)
        {
            if (Interlocked.Exchange(ref _completedFlag, 1) != 0)
            {
                return;
            }

            try
            {
                _cts.Cancel();
            }
            catch (ObjectDisposedException ode)
            {
                System.Diagnostics.Debug.WriteLine(ode);
            }

            var snap = Volatile.Read(ref _state);
            var duration = FormatDuration();
            var symbol = Colorize(rawSymbol, symbolColor);
            var line = $"{symbol} {snap.Label} ({duration})";
            var visibleLength = rawSymbol.Length + 1 + snap.Label.Length + 2 + duration.Length + 1;
            var padding = Math.Max(0, Volatile.Read(ref _lastLineLength) - visibleLength);

            if (_useAnsi)
            {
                Console.Error.Write(padding == 0
                    ? $"\r{line}\n[?25h"
                    : $"\r{line}{new string(' ', padding)}\n[?25h");
            }
            else
            {
                Console.Error.WriteLine(line);
            }

            _buffered.Flush();
        }

        private string FormatDuration()
        {
            var elapsed = _stopwatch.Elapsed;
            return elapsed.TotalSeconds >= 1
                ? $"{elapsed.TotalSeconds:F1}s"
                : $"{elapsed.TotalMilliseconds:F0}ms";
        }

        private string Colorize(string text, Color color)
        {
            if (!_caps.SupportsColor || !_useAnsi)
            {
                return text;
            }

            return AnsiBuilder.Apply(text, new Style(Foreground: color), _caps.ColorDepth);
        }

        public async ValueTask DisposeAsync()
        {
            if (Volatile.Read(ref _completedFlag) == 0)
            {
                Fail();
            }

            if (_animationTask is { } t)
            {
                try
                {
                    await t.ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is OperationCanceledException or IOException)
                {
                    System.Diagnostics.Debug.WriteLine(ex);
                }
            }
            _cts.Dispose();
            await _buffered.DisposeAsync().ConfigureAwait(false);
        }
    }
}

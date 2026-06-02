using System.Diagnostics;
using Vice.Display.Rendering;

namespace Vice.Display;

internal sealed class UnifiedStatusDisplay : IStatusDisplay
{
    private static readonly string[] UnicodeFrames = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];
    private static readonly string[] AsciiFrames = ["|", "/", "-", "\\"];
    private const int INTERVAL_MS = 80;

    private readonly TerminalCapabilities _capabilities;
    private readonly bool _stderrIsTty;
    private readonly bool _reducedMotion;

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
        _reducedMotion = DetectReducedMotion();
    }

    public static bool DetectReducedMotion()
    {
        if (IsTruthy(Environment.GetEnvironmentVariable("VICE_NO_STATUS")))
        {
            return true;
        }

        if (IsTruthy(Environment.GetEnvironmentVariable("VICE_REDUCED_MOTION")))
        {
            return true;
        }

        if (IsTruthy(Environment.GetEnvironmentVariable("ACCESSIBLE")))
        {
            return true;
        }

        var term = Environment.GetEnvironmentVariable("TERM");
        if (string.Equals(term, "dumb", StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    private static bool IsTruthy(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        return !string.Equals(value, "0", StringComparison.Ordinal)
            && !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(value, "no", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(value, "off", StringComparison.OrdinalIgnoreCase);
    }

    public IStatusHandle Start(string label, IConsoleWriter console)
    {
        var handle = new UnifiedHandle(label,
                                       console,
                                       _capabilities,
                                       _stderrIsTty,
                                       _reducedMotion);
        handle.StartAnimation();
        return handle;
    }

    private sealed record Snapshot(string Label, double? Progress);

    private sealed class UnifiedHandle : IStatusHandle
    {
        private const int MIN_BAR_WIDTH = 10;
        private const int DEFAULT_TERMINAL_WIDTH = 80;
        private const int REDUCED_MOTION_STEP_PERCENT = 10;
        private const string HIDE_CURSOR = "\u001b[?25l";
        private const string SHOW_CURSOR = "\u001b[?25h";

        private Snapshot _state;
        private int _completedFlag;
        private int _lastLineLength;
        private int _lastAnnouncedPercent;
        private readonly BufferingConsoleWriter _buffered;
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private readonly CancellationTokenSource _cts = new();
        private readonly object _consoleLock = new();
        private Task? _animationTask;
        private readonly TerminalCapabilities _caps;
        private readonly bool _useAnsi;
        private readonly bool _reducedMotion;
        private readonly string[] _frames;
        private readonly string _successSymbol;
        private readonly string _failureSymbol;

        public IConsoleWriter Writer => _buffered;
        public bool SupportsProgress => true;

        public UnifiedHandle(string label,
                             IConsoleWriter console,
                             TerminalCapabilities caps,
                             bool stderrIsTty,
                             bool reducedMotion)
        {
            _state = new Snapshot(AnsiStripper.Strip(label), null);
            _buffered = new BufferingConsoleWriter(console);
            _caps = caps;
            _reducedMotion = reducedMotion;
            _useAnsi = stderrIsTty && caps.SupportsAnsi
                       && !reducedMotion;
            _frames = caps.SupportsUnicode ? UnicodeFrames : AsciiFrames;
            _successSymbol = caps.SupportsUnicode ? "✓" : "[OK]";
            _failureSymbol = caps.SupportsUnicode ? "✗" : "[FAIL]";
            _lastAnnouncedPercent = -1;
        }

        public void StartAnimation()
        {
            if (_reducedMotion)
            {
                _animationTask = Task.CompletedTask;
                AnnounceDiscrete($"{_state.Label}...");
                return;
            }

            if (_useAnsi)
            {
                Console.Error.Write(HIDE_CURSOR);
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
                    var line = RenderLine(_frames[frame], snap);
                    var visibleLength = CalculateVisibleLength(_frames[frame], snap);
                    var padding = Math.Max(0, Volatile.Read(ref _lastLineLength) - visibleLength);
                    lock (_consoleLock)
                    {
                        if (Volatile.Read(ref _completedFlag) != 0)
                        {
                            break;
                        }
                        Console.Error.Write(padding == 0
                            ? $"\r{line}"
                            : $"\r{line}{new string(' ', padding)}");
                        Volatile.Write(ref _lastLineLength, visibleLength);
                    }
                    frame = (frame + 1) % _frames.Length;
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
            var sanitized = AnsiStripper.Strip(label);
            Snapshot current, next;
            do
            {
                current = Volatile.Read(ref _state);
                next = current with { Label = sanitized };
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

            if (_reducedMotion)
            {
                AnnounceProgress(clamped);
            }
        }

        public void Succeed() => Complete(Color.Green, _successSymbol);
        public void Fail() => Complete(Color.Red, _failureSymbol);

        private void AnnounceProgress(double fraction)
        {
            var percent = (int)Math.Round(fraction * 100);
            int previous;
            do
            {
                previous = Volatile.Read(ref _lastAnnouncedPercent);
                if (previous >= 0
                    && percent < previous + REDUCED_MOTION_STEP_PERCENT
                    && percent != 100)
                {
                    return;
                }
            }
            while (Interlocked.CompareExchange(ref _lastAnnouncedPercent, percent, previous) != previous);

            var snap = Volatile.Read(ref _state);
            AnnounceDiscrete($"{snap.Label} {percent}%");
        }

        private void AnnounceDiscrete(string text)
        {
            lock (_consoleLock)
            {
                if (Volatile.Read(ref _completedFlag) != 0)
                {
                    return;
                }
                Console.Error.WriteLine(text);
            }
        }

        private readonly record struct ProgressLayout(
            string PercentText,
            string Elapsed,
            int FixedWidth,
            int BarWidth,
            bool ShowBar);

        private ProgressLayout ComputeLayout(string frame, Snapshot snap, double frac)
        {
            var pctStr = $"{frac * 100:F0}%";
            var elapsed = FormatDuration();
            var fixedWidth = frame.Length + 1 + snap.Label.Length + 1 + pctStr.Length + 2 + elapsed.Length + 1;
            var termWidth = _caps.Width > 0 ? _caps.Width : DEFAULT_TERMINAL_WIDTH;
            var barWidth = termWidth - fixedWidth;
            return new ProgressLayout(
                pctStr,
                elapsed,
                fixedWidth,
                barWidth,
                barWidth >= MIN_BAR_WIDTH);
        }

        private string RenderLine(string frame, Snapshot snap)
        {
            var spinnerText = Colorize(frame, Color.Yellow);

            if (snap.Progress is not { } frac)
            {
                return $"{spinnerText} {snap.Label}";
            }

            var layout = ComputeLayout(frame, snap, frac);

            if (layout.ShowBar)
            {
                var bar = RenderBar(frac, layout.BarWidth);
                return $"{spinnerText} {snap.Label} {bar} {layout.PercentText} ({layout.Elapsed})";
            }

            return $"{spinnerText} {snap.Label} {layout.PercentText} ({layout.Elapsed})";
        }

        private int CalculateVisibleLength(string frame, Snapshot snap)
        {
            if (snap.Progress is not { } frac)
            {
                return frame.Length + 1 + snap.Label.Length;
            }

            var layout = ComputeLayout(frame, snap, frac);

            if (layout.ShowBar)
            {
                return layout.FixedWidth + 1 + layout.BarWidth;
            }

            return layout.FixedWidth;
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

            lock (_consoleLock)
            {
                var padding = Math.Max(0, Volatile.Read(ref _lastLineLength) - visibleLength);

                if (_useAnsi)
                {
                    Console.Error.Write(padding == 0
                        ? $"\r{line}\n{SHOW_CURSOR}"
                        : $"\r{line}{new string(' ', padding)}\n{SHOW_CURSOR}");
                }
                else
                {
                    Console.Error.WriteLine(line);
                }

                Volatile.Write(ref _lastLineLength, visibleLength);
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

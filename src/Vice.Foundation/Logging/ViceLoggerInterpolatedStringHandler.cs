using System.Runtime.CompilerServices;

namespace Vice.Logging;

[InterpolatedStringHandler]
public ref struct ViceLoggerInterpolatedStringHandler
{
    private DefaultInterpolatedStringHandler _inner;

    public bool IsEnabled { get; }

    public ViceLoggerInterpolatedStringHandler(
        int literalLength,
        int formattedCount,
        IViceLogger logger,
        ViceLogLevel level,
        out bool handlerIsValid)
    {
        IsEnabled = logger.IsEnabled(level);
        handlerIsValid = IsEnabled;
        if (IsEnabled)
        {
            _inner = new DefaultInterpolatedStringHandler(literalLength, formattedCount);
        }
        else
        {
            _inner = default;
        }
    }

    public void AppendLiteral(string value)
    {
        _inner.AppendLiteral(value);
    }

    public void AppendFormatted<T>(T value)
    {
        _inner.AppendFormatted(value);
    }

    public void AppendFormatted<T>(T value, string? format)
    {
        _inner.AppendFormatted(value, format);
    }

    public void AppendFormatted<T>(T value, int alignment)
    {
        _inner.AppendFormatted(value, alignment);
    }

    public void AppendFormatted<T>(T value, int alignment, string? format)
    {
        _inner.AppendFormatted(value, alignment, format);
    }

    public void AppendFormatted(string? value)
    {
        _inner.AppendFormatted(value);
    }

    public void AppendFormatted(ReadOnlySpan<char> value)
    {
        _inner.AppendFormatted(value);
    }

    public string ToStringAndClear()
    {
        return _inner.ToStringAndClear();
    }
}

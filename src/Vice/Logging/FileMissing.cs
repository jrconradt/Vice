namespace Vice.Logging;

public sealed class FileMissing(string path, Exception? inner = null) : ViceError(inner, path)
{
    public string Path { get; } = path;
    public override ViceLogLevel LogLevel => ViceLogLevel.Debug;
    public override string? Hint => "Verify the path exists and you have read access.";
    public override string ToString() => $"File not found: {Path}";
}

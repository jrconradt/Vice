namespace Vice.Contracts;

public sealed class SessionState
{
    public const int ProtocolVersion = 1;

    public string AppName { get; }
    public string PipeName { get; }

    public SessionState(string appName, string? pipeName = null)
    {
        if (string.IsNullOrWhiteSpace(appName))
        {
            throw new ArgumentException("appName must be non-empty.", nameof(appName));
        }

        AppName = appName;
        PipeName = pipeName ?? $"{appName}-session-v{ProtocolVersion}-{Environment.UserName}";
    }

    public static SessionState For(string appName, string? pipeName = null)
        => new(appName, pipeName);
}

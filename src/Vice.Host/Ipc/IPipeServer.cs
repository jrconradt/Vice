namespace Vice.Ipc;

internal interface IPipeServer : IAsyncDisposable
{
    bool IsListening { get; }
    Exception? Faulted { get; }
    Task StartAsync(CancellationToken ct);
}

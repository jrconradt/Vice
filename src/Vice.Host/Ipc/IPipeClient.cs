namespace Vice.Ipc;

internal interface IPipeClient : IAsyncDisposable
{
    bool IsConnected { get; }
    Task<PipeMessage?> SendAsync(PipeMessage message, CancellationToken ct);
}

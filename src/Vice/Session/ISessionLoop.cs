namespace Vice.Session;

internal interface ISessionLoop
{
    Task<bool> RunAsync(CancellationToken ct);
}

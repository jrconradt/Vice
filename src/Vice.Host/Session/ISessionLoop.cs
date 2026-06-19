namespace Vice.Session;

internal interface ISessionLoop
{
    Task RunAsync(CancellationToken ct);
}

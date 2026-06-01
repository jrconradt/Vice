namespace Vice.Session;

internal interface IInputHistory : IAsyncDisposable
{
    Task AppendAsync(string command, CancellationToken ct);
    IReadOnlyList<string> GetHistory();
    IReadOnlyList<string> GetHistory(int count);
}

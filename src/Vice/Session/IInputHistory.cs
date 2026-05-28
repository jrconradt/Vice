namespace Vice.Session;

internal interface IInputHistory : IAsyncDisposable
{
    void Load();
    Task AppendAsync(string command, CancellationToken ct);
    IReadOnlyList<string> GetHistory();
    IReadOnlyList<string> GetHistory(int count);
}

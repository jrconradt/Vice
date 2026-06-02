namespace Vice.Session;

internal interface ICommandExecutor
{
    Task<int> ExecuteAsync(string input, CancellationToken ct = default);
    Task<int> ExecuteAsync(string[] args, CancellationToken ct = default);
}

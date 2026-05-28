namespace Vice.Configuration;

public sealed class NullKeyring : IKeyring
{
    public static readonly NullKeyring Instance = new();
    public Task<string?> GetAsync(string key, CancellationToken ct = default) => Task.FromResult<string?>(null);
    public Task SetAsync(string key, string value, CancellationToken ct = default) => Task.CompletedTask;
    public Task<bool> DeleteAsync(string key, CancellationToken ct = default) => Task.FromResult(false);
    public Task<IReadOnlyList<string>> ListKeysAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

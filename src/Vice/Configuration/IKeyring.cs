namespace Vice.Configuration;

public interface IKeyring : IAsyncDisposable
{
    Task<string?> GetAsync(string key, CancellationToken ct = default);
    Task SetAsync(string key, string value, CancellationToken ct = default);
    Task<bool> DeleteAsync(string key, CancellationToken ct = default);
    Task<IReadOnlyList<string>> ListKeysAsync(CancellationToken ct = default);
}

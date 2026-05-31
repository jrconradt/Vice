namespace Vice.Mux.Strategies;

public sealed class StrategyRegistry
{
    public static StrategyRegistry Create() => new();

    private readonly Dictionary<string, StrategyEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    private bool _frozen;

    public void Register(string name, string description, StrategyKind kind,
        RouteStrategy? route = null, BroadcastStrategy? broadcast = null,
        Action<RouteState, string?>? configure = null)
    {
        if (_frozen)
        {
            throw new InvalidOperationException("StrategyRegistry is frozen.");
        }

        _entries[name] = new StrategyEntry(name, description, kind, route, broadcast, configure);
    }

    public void Freeze()
    {
        _frozen = true;
    }

    public bool TryGet(string name, out StrategyEntry entry)
        => _entries.TryGetValue(name, out entry!);

    public IEnumerable<StrategyEntry> All => _entries.Values;

    public static StrategyRegistry Default()
    {
        var r = new StrategyRegistry();
        BuiltinStrategies.RegisterAll(r);
        r.Freeze();
        return r;
    }
}

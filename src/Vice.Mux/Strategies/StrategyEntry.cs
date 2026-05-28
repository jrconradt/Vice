namespace Vice.Mux.Strategies;

public sealed record StrategyEntry(
    string Name,
    string Description,
    StrategyKind Kind,
    RouteStrategy? Route,
    BroadcastStrategy? Broadcast,
    Action<RouteState, string?>? Configure);

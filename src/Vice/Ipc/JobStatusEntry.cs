namespace Vice.Ipc;

internal sealed record JobStatusEntry(
    int Id,
    string Kind,
    string Status,
    double? Progress,
    string Label);

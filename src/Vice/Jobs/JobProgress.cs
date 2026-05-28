namespace Vice.Jobs;

public record JobProgress(
    long? BytesDownloaded = null,
    long? TotalBytes = null,
    long? MessagesReceived = null,
    string? Label = null)
{
    public bool IsIndeterminate => Fraction is null;

    public double? Fraction =>
    BytesDownloaded is not null && TotalBytes is > 0
        ? BytesDownloaded.Value / (double)TotalBytes.Value
        : null;
}

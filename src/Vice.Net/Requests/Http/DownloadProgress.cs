namespace Vice.Net.Requests.Http;

public sealed record DownloadProgress(long BytesDownloaded, long? TotalBytes)
{
    public double? Percentage => TotalBytes > 0
        ? (double)BytesDownloaded / TotalBytes * 100
        : null;

    public string FormatSize() => BytesDownloaded switch
    {
        < 1024 => $"{BytesDownloaded} B",
        < 1024 * 1024 => $"{BytesDownloaded / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{BytesDownloaded / (1024.0 * 1024.0):F1} MB",
        _ => $"{BytesDownloaded / (1024.0 * 1024.0 * 1024.0):F2} GB"
    };
}

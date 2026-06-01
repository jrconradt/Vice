namespace Vice.Jobs;

public record JobDescriptor(
    JobKind Kind,
    string? Source,
    string? ResourceId,
    string? DestinationPath,
    string? Extension,
    string? Format,
    string? Endpoint,
    string? Method,
    string? RequestData,
    Dictionary<string, string?>? Options)
{
    public static JobDescriptor ForDownload(
    string source,
    string resourceId,
    string destinationPath,
    string extension,
    string? format = null,
    Dictionary<string, string?>? options = null)
    {
        return new JobDescriptor(
            Kind: JobKind.Download,
            Source: source,
            ResourceId: resourceId,
            DestinationPath: destinationPath,
            Extension: extension,
            Format: format,
            Endpoint: null,
            Method: null,
            RequestData: null,
            Options: options);
    }

    public static JobDescriptor ForGrpcStream(
    string endpoint,
    string method,
    string? requestData = null,
    Dictionary<string, string?>? options = null)
    {
        return new JobDescriptor(
            Kind: JobKind.GrpcStream,
            Source: null,
            ResourceId: null,
            DestinationPath: null,
            Extension: null,
            Format: null,
            Endpoint: endpoint,
            Method: method,
            RequestData: requestData,
            Options: options);
    }
}

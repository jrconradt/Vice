using System.Text.Json;
using System.Text.Json.Serialization;

namespace Vice.Jobs;

[JsonConverter(typeof(JobKindJsonConverter))]
public readonly struct JobKind : IEquatable<JobKind>
{
    private readonly string? _name;

    private JobKind(string name)
    {
        _name = name;
    }

    public static JobKind Download { get; } = new JobKind("Download");

    public static JobKind GrpcStream { get; } = new JobKind("GrpcStream");

    public string Name => _name ?? "Unknown";

    public static JobKind Custom(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Job kind name must be non-empty.", nameof(name));
        }

        return new JobKind(name);
    }

    public bool Equals(JobKind other)
        => string.Equals(Name, other.Name, StringComparison.Ordinal);

    public override bool Equals(object? obj)
        => obj is JobKind other && Equals(other);

    public override int GetHashCode()
        => StringComparer.Ordinal.GetHashCode(Name);

    public override string ToString() => Name;

    public static bool operator ==(JobKind left, JobKind right) => left.Equals(right);

    public static bool operator !=(JobKind left, JobKind right) => !left.Equals(right);
}

internal sealed class JobKindJsonConverter : JsonConverter<JobKind>
{
    public override JobKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var name = reader.GetString();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new JsonException("Job kind name must be non-empty.");
        }

        if (string.Equals(name, JobKind.Download.Name, StringComparison.Ordinal))
        {
            return JobKind.Download;
        }

        if (string.Equals(name, JobKind.GrpcStream.Name, StringComparison.Ordinal))
        {
            return JobKind.GrpcStream;
        }

        return JobKind.Custom(name);
    }

    public override void Write(Utf8JsonWriter writer, JobKind value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Name);
    }
}

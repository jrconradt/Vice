namespace Vice.Composition;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ViceCommandAttribute : Attribute
{
    public ViceCommandAttribute()
    {
        ExplicitTargets = Array.Empty<string>();
    }

    public ViceCommandAttribute(params string[] explicitTargets)
    {
        ExplicitTargets = explicitTargets ?? Array.Empty<string>();
    }

    public string[] ExplicitTargets { get; }

    public string? Verb { get; init; }

    public string? Description { get; init; }
}

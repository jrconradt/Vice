namespace Vice.Composition;

[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class TargetNameAttribute : Attribute
{
    public TargetNameAttribute(string name)
    {
        Name = name;
    }

    public string Name { get; }
}

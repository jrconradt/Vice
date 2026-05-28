namespace Vice.Composition;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class ViceSessionServiceAttribute : Attribute
{
}

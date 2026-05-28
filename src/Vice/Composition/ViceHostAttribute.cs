namespace Vice.Composition;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
public sealed class ViceHostAttribute : Attribute
{
}

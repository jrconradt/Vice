namespace Vice.Parser;

public interface ITargetDescriptor
{
    string Name { get; }
    bool Required { get; }
    bool Variadic { get; }
}

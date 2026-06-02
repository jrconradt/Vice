namespace Vice.Contracts;

public interface IConsumingCommandContext<T> : ICommandContext
{
    IStreamInput<T> Input { get; }
}

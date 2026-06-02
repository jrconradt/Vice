namespace Vice.Contracts;

public interface IStreamingCommandContext<T> : ICommandContext
{
    IStreamContext<T> Stream { get; }
}

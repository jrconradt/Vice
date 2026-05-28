using Vice.Execution;

namespace Vice.Streaming;

public interface IConsumingCommandContext<T> : ICommandContext
{
    IStreamInput<T> Input { get; }
}

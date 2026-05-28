using Vice.Execution;

namespace Vice.Streaming;

public interface IStreamingCommandContext<T> : ICommandContext
{
    IStreamContext<T> Stream { get; }
}

using Vice.Execution;

namespace Vice.Streaming;

internal sealed class StreamingCommandContext<T> : DelegatingCommandContext, IStreamingCommandContext<T>
{
    public IStreamContext<T> Stream { get; }

    internal StreamingCommandContext(CommandContext inner, IStreamContext<T> stream) : base(inner)
        => Stream = stream;
}

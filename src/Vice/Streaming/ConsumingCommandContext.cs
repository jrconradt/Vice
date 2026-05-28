using Vice.Execution;

namespace Vice.Streaming;

internal sealed class ConsumingCommandContext<T> : DelegatingCommandContext, IConsumingCommandContext<T>
{
    public IStreamInput<T> Input { get; }

    internal ConsumingCommandContext(CommandContext inner, IStreamInput<T> input) : base(inner)
        => Input = input;
}

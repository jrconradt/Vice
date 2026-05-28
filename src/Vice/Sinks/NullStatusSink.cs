using Vice.Display;

namespace Vice;

internal sealed class NullStatusSink : IStatusSink
{
    public static readonly NullStatusSink Instance = new();

    private NullStatusSink() { }

    public IStatusHandle Start(string label) => new NullHandle();

    private sealed class NullHandle : IStatusHandle
    {
        public IConsoleWriter Writer { get; } = NullConsoleWriter.Instance;

        public void Succeed() { }
        public void Fail() { }
        public void UpdateLabel(string label) { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

using Vice.Display.Rendering;

namespace Vice.Display;

internal sealed class NullStatusDisplay : IStatusDisplay
{
    public static readonly NullStatusDisplay Instance = new();

    public IStatusHandle Start(string label, IConsoleWriter console) => new NullHandle(console);

    private sealed class NullHandle : IStatusHandle
    {
        public IConsoleWriter Writer { get; }

        public NullHandle(IConsoleWriter writer) => Writer = writer;

        public void Succeed() { }
        public void Fail() { }
        public void UpdateLabel(string label) { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

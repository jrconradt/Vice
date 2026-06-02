using Vice.Display.Rendering;

namespace Vice.Display;

public interface IStatusHandle : IAsyncDisposable
{
    void Succeed();
    void Fail();
    void UpdateLabel(string label);
    IConsoleWriter Writer { get; }

    void UpdateProgress(double fraction) { }
    void UpdateProgress(double fraction, string? label)
    {
        UpdateProgress(fraction);
        if (label is not null)
        {
            UpdateLabel(label);
        }
    }
    bool SupportsProgress => false;
}

namespace Vice.Display;

public interface IStatusDisplay
{
    IStatusHandle Start(string label, IConsoleWriter console);
}

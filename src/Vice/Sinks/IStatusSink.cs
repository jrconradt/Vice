using Vice.Display;

namespace Vice;

public interface IStatusSink
{
    IStatusHandle Start(string label);
}

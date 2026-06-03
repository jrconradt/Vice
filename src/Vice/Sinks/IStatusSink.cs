using Vice.Display;

namespace Vice.Core;

public interface IStatusSink
{
    IStatusHandle Start(string label);
}

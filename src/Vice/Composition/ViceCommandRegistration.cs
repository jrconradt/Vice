using Vice.Core;
using Vice.Nodes;

namespace Vice.Composition;

public static class ViceCommandRegistration
{
    public static void Register<TCommand>(
        this IViceApp app,
        ChainNode chain,
        string description,
        bool showInHelp = true)
        where TCommand : class, IViceCommand, new()
    {
        if (app is null)
        {
            throw new ArgumentNullException(nameof(app));
        }

        var instance = new TCommand();
        app.Register(chain, description, instance.Handle, showInHelp);
    }
}

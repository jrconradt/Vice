using Vice.Execution;

namespace Vice.Composition;

public interface IViceCommand
{
    Task<int> Handle(CommandContext ctx, CancellationToken ct);
}

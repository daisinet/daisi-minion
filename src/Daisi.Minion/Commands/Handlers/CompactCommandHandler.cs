using Daisi.Minion.Engine;
using Daisi.Minion.Tui;
using Daisi.Llogos.Inference;

namespace Daisi.Minion.Commands.Handlers;

public sealed class CompactCommandHandler(
    ConversationManager conversation,
    AnsiRenderer renderer,
    Func<GenerationParams> getParams) : ISlashCommandHandler
{
    public async Task HandleAsync(string args, CancellationToken ct)
    {
        renderer.WriteInfo("Compacting conversation...");
        await conversation.CompactAsync(getParams(), ct);
        renderer.WriteSuccess("Conversation compacted.");
    }
}

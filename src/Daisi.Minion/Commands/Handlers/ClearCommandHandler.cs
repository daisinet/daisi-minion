using Daisi.Minion.Engine;
using Daisi.Minion.Tui;

namespace Daisi.Minion.Commands.Handlers;

public sealed class ClearCommandHandler(ConversationManager conversation, AnsiRenderer renderer) : ISlashCommandHandler
{
    public Task HandleAsync(string args, CancellationToken ct)
    {
        conversation.Reset();
        InferenceLog.Reset("/clear");
        renderer.WriteSuccess("Conversation cleared.");
        return Task.CompletedTask;
    }
}

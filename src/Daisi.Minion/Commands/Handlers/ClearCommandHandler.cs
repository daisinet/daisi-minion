using Daisi.Minion.Engine;
using Daisi.Minion.Tui;

namespace Daisi.Minion.Commands.Handlers;

public sealed class ClearCommandHandler(
    ConversationManager conversation,
    AnsiRenderer renderer,
    Tui.Layout.LayoutManager? layout,
    int contextSize) : ISlashCommandHandler
{
    public Task HandleAsync(string args, CancellationToken ct)
    {
        conversation.Reset();
        InferenceLog.Reset("/clear");
        layout?.ClearContent();
        layout?.StatusBar.SetContextUsage(0, contextSize);
        layout?.UpdateStatusBar();
        renderer.WriteSuccess("Conversation cleared.");
        return Task.CompletedTask;
    }
}

using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Workspaces.AutomaticPairing;

public sealed record AkburaTypingResult(
    bool Handled,
    ImmutableArray<TextChange> Changes,
    int NewPosition,
    AkburaPairSession? Session,
    bool TriggerCompletion,
    bool TriggerSignatureHelp)
{
    public static AkburaTypingResult PassThrough(
        int position,
        AkburaPairSession? session)
    {
        return new AkburaTypingResult(
            Handled: false,
            Changes: [],
            NewPosition: position,
            Session: session,
            TriggerCompletion: false,
            TriggerSignatureHelp: false);
    }
}

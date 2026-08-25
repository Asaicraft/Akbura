using Microsoft.VisualStudio.Commanding;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Editor.Commanding.Commands;
using Microsoft.VisualStudio.Utilities;
using System.ComponentModel.Composition;

namespace Akbura.VisualStudio.Editor.AutomaticPairing;

[Export(typeof(ICommandHandler))]
[Name(nameof(AkburaDynamicDelimiterBackspaceHandler))]
[ContentType(AkburaContentTypeNames.Akbura)]
[TextViewRole(PredefinedTextViewRoles.Editable)]
internal sealed class AkburaDynamicDelimiterBackspaceHandler :
    IChainedCommandHandler<BackspaceKeyCommandArgs>
{
    public string DisplayName =>
        "Akbura dynamic delimiter paired Backspace";

    public CommandState GetCommandState(
        BackspaceKeyCommandArgs args,
        Func<CommandState> nextCommandHandler)
    {
        return nextCommandHandler();
    }

    public void ExecuteCommand(
        BackspaceKeyCommandArgs args,
        Action nextCommandHandler,
        CommandExecutionContext executionContext)
    {
        if (args.TextView.IsClosed ||
            !args.TextView.Selection.IsEmpty ||
            args.TextView.GetMultiSelectionBroker()
                .HasMultipleSelections)
        {
            nextCommandHandler();
            return;
        }
        var manager = AkburaDynamicDelimiterSessionManager
            .GetOrCreate(args.TextView, args.SubjectBuffer);
        using var commandEdit = manager.BeginCommandEdit();

        var snapshot = args.SubjectBuffer.CurrentSnapshot;
        var caret = args.TextView.Caret.Position.BufferPosition;
        if (ReferenceEquals(
                caret.Snapshot.TextBuffer,
                args.SubjectBuffer) &&
            (TryDeleteEmptySession(
                 args,
                 manager,
                 snapshot,
                 caret.Position,
                 AkburaDynamicDelimiterKind.InterpolatedStringBraces) ||
             TryDeleteEmptySession(
                 args,
                 manager,
                 snapshot,
                 caret.Position,
                 AkburaDynamicDelimiterKind.RawStringQuotes)))
        {
            return;
        }

        nextCommandHandler();
    }

    private static bool TryDeleteEmptySession(
        BackspaceKeyCommandArgs args,
        AkburaDynamicDelimiterSessionManager manager,
        ITextSnapshot snapshot,
        int caret,
        AkburaDynamicDelimiterKind kind)
    {
        if (!manager.TryGetSession(kind, out var session) ||
            !session.TryGetSpans(
                snapshot,
                out var opening,
                out var closing) ||
            opening.End.Position != caret ||
            closing.Start.Position != caret)
        {
            return false;
        }

        using var edit = args.SubjectBuffer.CreateEdit();
        if (!edit.Delete(Span.FromBounds(
                opening.Start.Position,
                closing.End.Position)))
        {
            return false;
        }

        var applied = edit.Apply();
        manager.RemoveSession(kind);
        if (ReferenceEquals(
                args.TextView.TextBuffer,
                args.SubjectBuffer))
        {
            args.TextView.Caret.MoveTo(
                new SnapshotPoint(
                    applied,
                    opening.Start.Position));
        }

        return true;
    }
}

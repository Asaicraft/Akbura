using Akbura.VisualStudio.Editor;
using Microsoft.VisualStudio.Commanding;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Editor.Commanding.Commands;
using Microsoft.VisualStudio.Utilities;
using System.ComponentModel.Composition;
using System.Diagnostics;

namespace Akbura.VisualStudio.Completion;

[Export(typeof(ICommandHandler))]
[Name(nameof(AkburaCompletionCommandHandler))]
[ContentType(AkburaContentTypeNames.Akbura)]
[TextViewRole(PredefinedTextViewRoles.Editable)]
internal sealed class AkburaCompletionCommandHandler :
    IChainedCommandHandler<TypeCharCommandArgs>
{
    private readonly IAsyncCompletionBroker _completionBroker;

    [ImportingConstructor]
    public AkburaCompletionCommandHandler(
        IAsyncCompletionBroker completionBroker)
    {
        _completionBroker = completionBroker ??
            throw new ArgumentNullException(
                nameof(completionBroker));

        Debug.WriteLine(
            "[Akbura.Completion] Command handler created.");
    }

    public string DisplayName =>
        "Akbura completion trigger";

    public CommandState GetCommandState(
        TypeCharCommandArgs args,
        Func<CommandState> nextCommandHandler)
    {
        return nextCommandHandler();
    }

    public void ExecuteCommand(
        TypeCharCommandArgs args,
        Action nextCommandHandler,
        CommandExecutionContext executionContext)
    {
        var snapshotBeforeTrigger =
            args.TextView.TextSnapshot;

        nextCommandHandler();

        if (args.TextView.IsClosed ||
            !AkburaMarkupEditingFacts
                .IsCompletionNameCharacter(args.TypedChar))
        {
            return;
        }

        var triggerLocation = args.TextView.Caret
            .Position.BufferPosition;
        if (!AkburaMarkupEditingFacts
                .IsPotentialCompletionPosition(
                    triggerLocation.Snapshot,
                    triggerLocation.Position))
        {
            return;
        }

        var trigger = new CompletionTrigger(
            CompletionTriggerReason.Insertion,
            snapshotBeforeTrigger,
            args.TypedChar);
        var session = _completionBroker.TriggerCompletion(
            args.TextView,
            trigger,
            triggerLocation,
            CancellationToken.None);

        session?.OpenOrUpdate(
            trigger,
            triggerLocation,
            CancellationToken.None);

        Debug.WriteLine(
            $"[Akbura.Completion] Explicit update requested: " +
            $"character='{args.TypedChar}', " +
            $"position={triggerLocation.Position}, " +
            $"session={(session == null ? "missing" : "available")}.");
    }
}

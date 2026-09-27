using Akbura.Workspaces;
using Akbura.Workspaces.Formatting;
using Akbura.VisualStudio.Editor;
using Microsoft.VisualStudio.Commanding;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Editor.Commanding.Commands;
using Microsoft.VisualStudio.Utilities;
using System.Collections.Immutable;
using System.ComponentModel.Composition;

namespace Akbura.VisualStudio.Formatting;

[Export(typeof(ICommandHandler))]
[Name(nameof(AkburaFormattingCommandHandler))]
[ContentType(AkburaContentTypeNames.Akbura)]
[TextViewRole(PredefinedTextViewRoles.Editable)]
internal sealed class AkburaFormattingCommandHandler :
    ICommandHandler<FormatDocumentCommandArgs>,
    ICommandHandler<FormatSelectionCommandArgs>
{
    private readonly AkburaVisualStudioWorkspace _workspaceHost;
    private readonly AkburaParserService _parserService;

    [ImportingConstructor]
    public AkburaFormattingCommandHandler(
        AkburaVisualStudioWorkspace workspaceHost,
        AkburaParserService parserService)
    {
        _workspaceHost = workspaceHost ??
            throw new ArgumentNullException(nameof(workspaceHost));
        _parserService = parserService ??
            throw new ArgumentNullException(nameof(parserService));
    }

    public string DisplayName => "Akbura formatting";

    public CommandState GetCommandState(FormatDocumentCommandArgs args) =>
        CommandState.Available;

    public bool ExecuteCommand(
        FormatDocumentCommandArgs args,
        CommandExecutionContext executionContext)
    {
        var snapshot = args.SubjectBuffer.CurrentSnapshot;
        var document = _parserService.GetSyntacticDocument(snapshot);
        var changes = _workspaceHost.Workspace.LanguageServices.Formatting
            .FormatDocument(document, GetFormattingOptions(args.TextView.Options));
        return ApplyChanges(args.SubjectBuffer, snapshot, changes);
    }

    public CommandState GetCommandState(FormatSelectionCommandArgs args) =>
        args.TextView.Selection.IsEmpty
            ? CommandState.Unavailable
            : CommandState.Available;

    public bool ExecuteCommand(
        FormatSelectionCommandArgs args,
        CommandExecutionContext executionContext)
    {
        var snapshot = args.SubjectBuffer.CurrentSnapshot;
        var selectedSpans = args.TextView.Selection.SelectedSpans;
        if (selectedSpans.Count == 0)
        {
            return false;
        }

        var rangeStart = snapshot.Length;
        var rangeEnd = 0;
        foreach (var selectedSpan in selectedSpans)
        {
            var span = selectedSpan.TranslateTo(
                snapshot,
                SpanTrackingMode.EdgeInclusive);
            rangeStart = Math.Min(rangeStart, span.Start.Position);
            rangeEnd = Math.Max(rangeEnd, span.End.Position);
        }

        var document = _parserService.GetSyntacticDocument(snapshot);
        var changes = _workspaceHost.Workspace.LanguageServices.Formatting
            .FormatRange(
                document,
                new Microsoft.CodeAnalysis.Text.TextSpan(
                    rangeStart,
                    rangeEnd - rangeStart),
                GetFormattingOptions(args.TextView.Options));
        return ApplyChanges(args.SubjectBuffer, snapshot, changes);
    }

    private static AkburaFormattingOptions GetFormattingOptions(
        IEditorOptions editorOptions)
    {
        return new AkburaFormattingOptions(
            TabSize: Math.Max(
                1,
                editorOptions.GetOptionValue(
                    DefaultOptions.TabSizeOptionId)),
            InsertSpaces: editorOptions.GetOptionValue(
                DefaultOptions.ConvertTabsToSpacesOptionId));
    }

    private static bool ApplyChanges(
        ITextBuffer textBuffer,
        ITextSnapshot snapshot,
        ImmutableArray<Microsoft.CodeAnalysis.Text.TextChange> changes)
    {
        if (changes.IsDefaultOrEmpty)
        {
            return true;
        }

        if (!ReferenceEquals(textBuffer.CurrentSnapshot, snapshot))
        {
            return false;
        }

        using var edit = textBuffer.CreateEdit();
        for (var i = changes.Length - 1; i >= 0; i--)
        {
            var change = changes[i];
            if (!edit.Replace(
                    new Span(change.Span.Start, change.Span.Length),
                    change.NewText ?? string.Empty))
            {
                return false;
            }
        }

        edit.Apply();
        return !edit.Canceled;
    }
}

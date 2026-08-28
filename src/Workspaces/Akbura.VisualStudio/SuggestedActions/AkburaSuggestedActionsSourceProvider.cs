using Akbura.VisualStudio.Editor;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.Utilities;
using System.ComponentModel.Composition;

namespace Akbura.VisualStudio.SuggestedActions;

[Export(typeof(ISuggestedActionsSourceProvider))]
[Name(nameof(AkburaSuggestedActionsSourceProvider))]
[ContentType(AkburaContentTypeNames.Akbura)]
[Order]
internal sealed class AkburaSuggestedActionsSourceProvider :
    ISuggestedActionsSourceProvider
{
    private readonly ITextDocumentFactoryService _textDocumentFactory;
    private readonly AkburaVisualStudioWorkspace _workspaceHost;
    private readonly AkburaParserService _parserService;
    private readonly ITextUndoHistoryRegistry _undoHistoryRegistry;

    [ImportingConstructor]
    public AkburaSuggestedActionsSourceProvider(
        ITextDocumentFactoryService textDocumentFactory,
        AkburaVisualStudioWorkspace workspaceHost,
        AkburaParserService parserService,
        ITextUndoHistoryRegistry undoHistoryRegistry)
    {
        _textDocumentFactory = textDocumentFactory ??
            throw new ArgumentNullException(nameof(textDocumentFactory));
        _workspaceHost = workspaceHost ??
            throw new ArgumentNullException(nameof(workspaceHost));
        _parserService = parserService ??
            throw new ArgumentNullException(nameof(parserService));
        _undoHistoryRegistry = undoHistoryRegistry ??
            throw new ArgumentNullException(nameof(undoHistoryRegistry));
    }

    public ISuggestedActionsSource? CreateSuggestedActionsSource(
        ITextView textView,
        ITextBuffer textBuffer)
    {
        if (textView == null)
        {
            throw new ArgumentNullException(nameof(textView));
        }

        if (textBuffer == null)
        {
            throw new ArgumentNullException(nameof(textBuffer));
        }

        _textDocumentFactory.TryGetTextDocument(
            textBuffer,
            out var textDocument);
        var documentKind = textDocument == null
            ? AkburaEditorDocumentKindFacts.GetOrDefault(textBuffer)
            : AkburaEditorDocumentKindFacts.FromFilePath(
                textDocument.FilePath);
        textBuffer.Properties[typeof(AkburaEditorDocumentKind)] =
            documentKind;
        if (documentKind !=
            AkburaEditorDocumentKind.Component)
        {
            return null;
        }

        var bufferContext = textBuffer.Properties
            .GetOrCreateSingletonProperty(
                () => new AkburaTextBufferContext(
                    textBuffer,
                    _textDocumentFactory,
                    _workspaceHost,
                    _parserService));

        return textView.Properties.GetOrCreateSingletonProperty(
            () => new AkburaSuggestedActionsSource(
                textView,
                textBuffer,
                bufferContext,
                _workspaceHost.Workspace.LanguageServices.CodeActions,
                _undoHistoryRegistry));
    }
}

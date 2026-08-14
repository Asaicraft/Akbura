using Akbura.VisualStudio.CSharp;
using Akbura.VisualStudio.Editor;
using Akbura.Workspaces;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using System.ComponentModel.Composition;

namespace Akbura.VisualStudio.Completion;

[Export(typeof(IAsyncCompletionSourceProvider))]
[Name(nameof(AkburaCompletionSourceProvider))]
[ContentType(AkburaContentTypeNames.Akbura)]
[TextViewRole(PredefinedTextViewRoles.Editable)]
internal sealed class AkburaCompletionSourceProvider :
    IAsyncCompletionSourceProvider
{
    private readonly ITextDocumentFactoryService _textDocumentFactory;

    private readonly AkburaVisualStudioWorkspace _workspaceHost;

    private readonly AkburaParserService _parserService;

    private readonly AkburaProjectedCSharpDocumentService
        _projectedDocumentService;

    [ImportingConstructor]
    public AkburaCompletionSourceProvider(
        ITextDocumentFactoryService textDocumentFactory,
        AkburaVisualStudioWorkspace workspaceHost,
        AkburaParserService parserService,
        AkburaProjectedCSharpDocumentService projectedDocumentService)
    {
        _textDocumentFactory = textDocumentFactory ??
            throw new ArgumentNullException(
                nameof(textDocumentFactory));
        _workspaceHost = workspaceHost ??
            throw new ArgumentNullException(
                nameof(workspaceHost));
        _parserService = parserService ??
            throw new ArgumentNullException(
                nameof(parserService));
        _projectedDocumentService = projectedDocumentService ??
            throw new ArgumentNullException(
                nameof(projectedDocumentService));

        AkburaWorkspaceDiagnostics.Write(
            AkburaWorkspaceDiagnostics.Category.Completion,
            "Source provider created.");
    }

    public IAsyncCompletionSource GetOrCreate(ITextView textView)
    {
        if (textView == null)
        {
            throw new ArgumentNullException(nameof(textView));
        }

        var buffer = textView.TextBuffer;
        var isAkburaDocument =
            !_textDocumentFactory.TryGetTextDocument(
                buffer,
                out var textDocument) ||
            string.Equals(
                Path.GetExtension(textDocument.FilePath),
                ".akbura",
                StringComparison.OrdinalIgnoreCase);

        AkburaWorkspaceDiagnostics.Write(
            AkburaWorkspaceDiagnostics.Category.Completion,
            $"Source requested: " +
            $"contentType='{buffer.ContentType.TypeName}', " +
            $"file='{textDocument?.FilePath ?? "<untitled>"}', " +
            $"isAkbura={isAkburaDocument}.");

        var bufferContext = buffer.Properties
            .GetOrCreateSingletonProperty(
                () => new AkburaTextBufferContext(
                    buffer,
                    _textDocumentFactory,
                    _workspaceHost,
                    _parserService));

        return textView.Properties
            .GetOrCreateSingletonProperty(
                () => new AkburaCompletionSource(
                    buffer,
                    isAkburaDocument,
                    bufferContext,
                    _workspaceHost.Workspace
                        .LanguageServices
                        .Completion,
                    _parserService,
                    new AkburaRoslynCompletionService(
                        _projectedDocumentService)));
    }
}

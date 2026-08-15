using Akbura.VisualStudio.CSharp;
using Akbura.VisualStudio.Editor;
using Akbura.Workspaces;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Utilities;
using System.ComponentModel.Composition;

namespace Akbura.VisualStudio.QuickInfo;

[Export(typeof(IAsyncQuickInfoSourceProvider))]
[Name(nameof(AkburaQuickInfoSourceProvider))]
[ContentType(AkburaContentTypeNames.Akbura)]
internal sealed class AkburaQuickInfoSourceProvider :
    IAsyncQuickInfoSourceProvider
{
    private readonly ITextDocumentFactoryService _textDocumentFactory;

    private readonly AkburaVisualStudioWorkspace _workspaceHost;

    private readonly AkburaParserService _parserService;

    private readonly AkburaProjectedCSharpDocumentService
        _projectedDocumentService;

    private readonly IAkburaQuickInfoService _quickInfoService;

    [ImportingConstructor]
    public AkburaQuickInfoSourceProvider(
        ITextDocumentFactoryService textDocumentFactory,
        AkburaVisualStudioWorkspace workspaceHost,
        AkburaParserService parserService,
        AkburaProjectedCSharpDocumentService projectedDocumentService)
    {
        _textDocumentFactory = textDocumentFactory ??
            throw new ArgumentNullException(nameof(textDocumentFactory));
        _workspaceHost = workspaceHost ??
            throw new ArgumentNullException(nameof(workspaceHost));
        _parserService = parserService ??
            throw new ArgumentNullException(nameof(parserService));
        _projectedDocumentService = projectedDocumentService ??
            throw new ArgumentNullException(
                nameof(projectedDocumentService));
        _quickInfoService = workspaceHost.Workspace
            .LanguageServices
            .QuickInfo;
    }

    public IAsyncQuickInfoSource TryCreateQuickInfoSource(
        ITextBuffer textBuffer)
    {
        if (textBuffer == null)
        {
            throw new ArgumentNullException(nameof(textBuffer));
        }

        var bufferContext = textBuffer.Properties
            .GetOrCreateSingletonProperty(
                () => new AkburaTextBufferContext(
                    textBuffer,
                    _textDocumentFactory,
                    _workspaceHost,
                    _parserService));
        return new AkburaQuickInfoSource(
            textBuffer,
            bufferContext,
            _parserService,
            _projectedDocumentService,
            _quickInfoService);
    }
}

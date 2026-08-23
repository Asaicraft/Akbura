using Akbura.VisualStudio.Editor;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;
using System.ComponentModel.Composition;

namespace Akbura.VisualStudio.Diagnostics;

[Export(typeof(ITaggerProvider))]
[ContentType(AkburaContentTypeNames.Akbura)]
[TagType(typeof(IErrorTag))]
internal sealed class AkburaDiagnosticTaggerProvider :
    ITaggerProvider
{
    private readonly ITextDocumentFactoryService _textDocumentFactory;

    private readonly AkburaVisualStudioWorkspace _workspaceHost;

    private readonly AkburaParserService _parserService;

    private readonly AkburaDiagnosticTableDataSource _tableDataSource;

    [ImportingConstructor]
    public AkburaDiagnosticTaggerProvider(
        ITextDocumentFactoryService textDocumentFactory,
        AkburaVisualStudioWorkspace workspaceHost,
        AkburaParserService parserService,
        AkburaDiagnosticTableDataSource tableDataSource)
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
        _tableDataSource = tableDataSource ??
            throw new ArgumentNullException(
                nameof(tableDataSource));
    }

    public ITagger<T>? CreateTagger<T>(
        ITextBuffer buffer)
        where T : ITag
    {
        if (buffer == null)
        {
            throw new ArgumentNullException(nameof(buffer));
        }

        var bufferContext = buffer.Properties
            .GetOrCreateSingletonProperty(
                () => new AkburaTextBufferContext(
                    buffer,
                    _textDocumentFactory,
                    _workspaceHost,
                    _parserService));

        return buffer.Properties
            .GetOrCreateSingletonProperty(
                () => new AkburaDiagnosticTagger(
                    buffer,
                    bufferContext,
                    _tableDataSource)) as ITagger<T>;
    }
}

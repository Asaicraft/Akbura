using Akbura.VisualStudio.Editor;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Utilities;
using System.ComponentModel.Composition;

namespace Akbura.VisualStudio.Classification;

[Export(typeof(IClassifierProvider))]
[ContentType(AkburaContentTypeNames.Akbura)]
internal sealed class AkburaClassifierProvider :
    IClassifierProvider
{
    private readonly ITextDocumentFactoryService _textDocumentFactory;

    private readonly AkburaVisualStudioWorkspace _workspaceHost;

    private readonly AkburaClassificationTypeMap _typeMap;

    [ImportingConstructor]
    public AkburaClassifierProvider(
        ITextDocumentFactoryService textDocumentFactory,
        IClassificationTypeRegistryService classificationTypeRegistry,
        AkburaVisualStudioWorkspace workspaceHost)
    {
        _textDocumentFactory =
            textDocumentFactory ??
            throw new ArgumentNullException(
                nameof(textDocumentFactory));

        _workspaceHost =
            workspaceHost ??
            throw new ArgumentNullException(
                nameof(workspaceHost));

        _typeMap =
            new AkburaClassificationTypeMap(
                classificationTypeRegistry);
    }

    public IClassifier GetClassifier(ITextBuffer textBuffer)
    {
        if (textBuffer == null)
        {
            throw new ArgumentNullException(
                nameof(textBuffer));
        }

        var bufferContext =
            textBuffer.Properties
                .GetOrCreateSingletonProperty(
                    () =>
                        new AkburaTextBufferContext(
                            textBuffer,
                            _textDocumentFactory,
                            _workspaceHost));

        return textBuffer.Properties
            .GetOrCreateSingletonProperty(
                () =>
                    new AkburaClassifier(
                        bufferContext,
                        _typeMap));
    }
}
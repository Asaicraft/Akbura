using Akbura.VisualStudio.Editor;
using Akbura.Workspaces;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using System.ComponentModel.Composition;

namespace Akbura.VisualStudio.Navigation;

[Export(typeof(INavigableSymbolSourceProvider))]
[Name(nameof(AkburaNavigableSymbolSourceProvider))]
[ContentType(AkburaContentTypeNames.Akbura)]
internal sealed class AkburaNavigableSymbolSourceProvider :
    INavigableSymbolSourceProvider
{
    private readonly ITextDocumentFactoryService
        _textDocumentFactory;

    private readonly AkburaVisualStudioWorkspace
        _workspaceHost;

    private readonly IAkburaDefinitionService
        _definitionService;

    private readonly IServiceProvider
        _serviceProvider;

    [ImportingConstructor]
    public AkburaNavigableSymbolSourceProvider(
        ITextDocumentFactoryService textDocumentFactory,
        AkburaVisualStudioWorkspace workspaceHost,
        [Import(typeof(SVsServiceProvider))]
        IServiceProvider serviceProvider)
    {
        _textDocumentFactory =
            textDocumentFactory ??
            throw new ArgumentNullException(
                nameof(textDocumentFactory));

        _workspaceHost =
            workspaceHost ??
            throw new ArgumentNullException(
                nameof(workspaceHost));

        _serviceProvider =
            serviceProvider ??
            throw new ArgumentNullException(
                nameof(serviceProvider));

        _definitionService =
            workspaceHost.Workspace
                .LanguageServices
                .Definition;
    }

    public INavigableSymbolSource
        TryCreateNavigableSymbolSource(
            ITextView textView,
            ITextBuffer buffer)
    {
        if (textView == null)
        {
            throw new ArgumentNullException(
                nameof(textView));
        }

        if (buffer == null)
        {
            throw new ArgumentNullException(
                nameof(buffer));
        }

        /*
         * The classifier and navigation provider share the same
         * parsed buffer context through the text buffer property bag.
         */
        var bufferContext =
            buffer.Properties
                .GetOrCreateSingletonProperty(
                    () =>
                        new AkburaTextBufferContext(
                            buffer,
                            _textDocumentFactory,
                            _workspaceHost));

        return new AkburaNavigableSymbolSource(
            bufferContext,
            _definitionService,
            _serviceProvider);
    }
}
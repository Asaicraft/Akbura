using Akbura.VisualStudio.Editor;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using System.ComponentModel.Composition;

namespace Akbura.VisualStudio.Indentation;

[Export(typeof(ISmartIndentProvider))]
[ContentType(AkburaContentTypeNames.Akbura)]
internal sealed class AkburaSmartIndentProvider :
    ISmartIndentProvider
{
    private readonly AkburaParserService _parserService;

    [ImportingConstructor]
    public AkburaSmartIndentProvider(
        AkburaParserService parserService)
    {
        _parserService = parserService ??
            throw new ArgumentNullException(
                nameof(parserService));
    }

    public ISmartIndent CreateSmartIndent(
        ITextView textView)
    {
        if (textView == null)
        {
            throw new ArgumentNullException(
                nameof(textView));
        }

        return new AkburaSmartIndent(
            textView,
            _parserService);
    }
}

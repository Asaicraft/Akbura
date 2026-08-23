using Akbura.VisualStudio.Editor;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;
using System.ComponentModel.Composition;

namespace Akbura.VisualStudio.Outlining;

[Export(typeof(ITaggerProvider))]
[ContentType(AkburaContentTypeNames.Akbura)]
[TagType(typeof(IOutliningRegionTag))]
internal sealed class AkburaOutliningTaggerProvider :
    ITaggerProvider
{
    private readonly AkburaParserService _parserService;

    [ImportingConstructor]
    public AkburaOutliningTaggerProvider(
        AkburaParserService parserService)
    {
        _parserService = parserService ??
            throw new ArgumentNullException(
                nameof(parserService));
    }

    public ITagger<T>? CreateTagger<T>(
        ITextBuffer buffer)
        where T : ITag
    {
        if (buffer == null)
        {
            throw new ArgumentNullException(
                nameof(buffer));
        }

        return buffer.Properties
            .GetOrCreateSingletonProperty(
                () => new AkburaOutliningTagger(
                    buffer,
                    _parserService)) as ITagger<T>;
    }
}

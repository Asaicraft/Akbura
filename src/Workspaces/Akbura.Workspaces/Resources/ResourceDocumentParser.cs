using Akbura.Pools;
using Microsoft.CodeAnalysis.Text;
using XmlDocumentSyntax = Microsoft.Language.Xml.XmlDocumentSyntax;
using XmlParser = Microsoft.Language.Xml.Parser;
using XmlTextChangeRange = Microsoft.Language.Xml.TextChangeRange;

namespace Akbura.Workspaces.Resources;

internal static class ResourceDocumentParser
{
    public static XmlDocumentSyntax Parse(SourceText text, XmlDocumentSyntax? previousDocument, IEnumerable<TextChangeRange>? changes, CancellationToken cancellationToken)
    {
        if (text == null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var source = text.ToString();

        if (previousDocument == null || changes == null)
        {
            return ParseText(source, cancellationToken);
        }

        var converted = ArrayBuilder<XmlTextChangeRange>.GetInstance();
        try
        {
            foreach (var change in changes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                converted.Add(
                    new XmlTextChangeRange(
                        new Microsoft.Language.Xml.TextSpan(
                            change.Span.Start,
                            change.Span.Length),
                        change.NewLength));
            }

            if (converted.Count == 0)
            {
                return ParseText(source, cancellationToken);
            }

            XmlDocumentSyntax result;
            try
            {
                result = XmlParser.ParseIncremental(
                    source,
                    converted.ToArray(),
                    previousDocument);
            }
            catch (ArgumentException)
            {
                result = XmlParser.ParseText(source);
            }
            catch (InvalidOperationException)
            {
                result = XmlParser.ParseText(source);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        finally
        {
            converted.Free();
        }
    }

    private static XmlDocumentSyntax ParseText(string source, CancellationToken cancellationToken)
    {
        var result = XmlParser.ParseText(source);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }
}

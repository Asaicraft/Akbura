using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using XmlDocumentSyntax = Microsoft.Language.Xml.XmlDocumentSyntax;

namespace Akbura.Workspaces.Resources;

internal sealed class ResourceDocumentSnapshot
{
    private ResourceDocumentSnapshot(ResourceDocumentInput input, XmlDocumentSyntax syntaxTree, LocalResourceFacts facts)
    {
        Input = input;
        SyntaxTree = syntaxTree ??
            throw new ArgumentNullException(nameof(syntaxTree));
        Facts = facts ?? throw new ArgumentNullException(nameof(facts));
    }

    public ResourceDocumentInput Input { get; }

    public Uri Uri => Input.Uri;

    public string PhysicalPath => Input.PhysicalPath;

    public ResourcePath LogicalPath => Input.LogicalPath;

    public SourceText Text => Input.Text;

    public VersionStamp Version => Input.Version;

    public LocalResourceFacts Facts { get; }

    internal XmlDocumentSyntax SyntaxTree { get; }

    public static ResourceDocumentSnapshot Create(ResourceDocumentInput input, CancellationToken cancellationToken)
    {
        var syntaxTree = ResourceDocumentParser.Parse(
            input.Text,
            previousDocument: null,
            changes: null,
            cancellationToken);
        var facts = LocalResourceFactsReader.Read(
            input,
            syntaxTree,
            cancellationToken);

        return new ResourceDocumentSnapshot(
            input,
            syntaxTree,
            facts);
    }

    public ResourceDocumentSnapshot WithText(SourceText text, VersionStamp version, IEnumerable<TextChangeRange>? changes, CancellationToken cancellationToken)
    {
        if (text == null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        var input = Input.WithText(text, version);
        if (text.ContentEquals(Text))
        {
            return new ResourceDocumentSnapshot(
                input,
                SyntaxTree,
                Facts);
        }

        var syntaxTree = ResourceDocumentParser.Parse(
            text,
            SyntaxTree,
            changes,
            cancellationToken);
        var facts = LocalResourceFactsReader.Read(
            input,
            syntaxTree,
            cancellationToken);

        return new ResourceDocumentSnapshot(
            input,
            syntaxTree,
            facts);
    }
}

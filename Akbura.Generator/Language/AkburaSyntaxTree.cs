using Akbura.Language.Syntax;
using Akbura.Language.Syntax.Green;
using Microsoft.CodeAnalysis.Text;
using System.Threading;

namespace Akbura.Language;

internal sealed class AkburaSyntaxTree
{
    private AkburaDocumentSyntax? _root;

    private AkburaSyntaxTree(SourceText text, GreenAkburaDocumentSyntax greenRoot)
    {
        Text = text;
        GreenRoot = greenRoot;
    }

    public SourceText Text { get; }

    public GreenAkburaDocumentSyntax GreenRoot { get; }

    public static AkburaSyntaxTree ParseText(string text, CancellationToken cancellationToken = default)
    {
        return ParseText(SourceText.From(text), cancellationToken);
    }

    public static AkburaSyntaxTree ParseText(SourceText text, CancellationToken cancellationToken = default)
    {
        var lexer = new Lexer(text);
        using var parser = new Parser(lexer, cancellationToken);

        return new AkburaSyntaxTree(text, parser.ParseCompilationUnit());
    }

    public AkburaDocumentSyntax GetRoot()
    {
        return _root ??= (AkburaDocumentSyntax)GreenRoot.CreateRed();
    }
}

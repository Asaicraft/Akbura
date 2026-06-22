using Akbura.Language.Syntax;
using Akbura.Language.Syntax.Green;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Akbura.Language;

internal sealed class AkburaSyntaxTree
{
    private AkburaDocumentSyntax? _root;

    private AkburaSyntaxTree(SourceText text, string filePath, GreenAkburaDocumentSyntax greenRoot)
    {
        Text = text;
        FilePath = filePath;
        GreenRoot = greenRoot;
    }

    public SourceText Text { get; }

    public string FilePath { get; }

    public string ComponentName
    {
        get
        {
            if (string.IsNullOrWhiteSpace(FilePath))
            {
                return string.Empty;
            }

            return Path.GetFileNameWithoutExtension(FilePath);
        }
    }

    public GreenAkburaDocumentSyntax GreenRoot { get; }

    public static AkburaSyntaxTree ParseText(string text, CancellationToken cancellationToken = default)
    {
        return ParseText(SourceText.From(text), filePath: string.Empty, cancellationToken);
    }

    public static AkburaSyntaxTree ParseText(
        string text,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        return ParseText(SourceText.From(text), filePath, cancellationToken);
    }

    public static AkburaSyntaxTree ParseText(SourceText text, CancellationToken cancellationToken = default)
    {
        return ParseText(text, filePath: string.Empty, cancellationToken);
    }

    public static AkburaSyntaxTree ParseText(
        SourceText text,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var lexer = new Lexer(text);
        using var parser = new Parser(lexer, cancellationToken);

        return new AkburaSyntaxTree(text, filePath, parser.ParseCompilationUnit());
    }

    public AkburaSyntaxTree WithChangedText(
        SourceText newText,
        IEnumerable<TextChangeRange>? changes = null,
        CancellationToken cancellationToken = default)
    {
        var changeRanges = changes?.ToArray() ?? newText.GetChangeRanges(Text).ToArray();
        if (changeRanges.Length == 0 && newText.ToString() == Text.ToString())
        {
            return this;
        }

        var lexer = new Lexer(newText);
        using var parser = new Parser(lexer, cancellationToken, GetRoot(), changeRanges);

        return new AkburaSyntaxTree(newText, FilePath, parser.ParseCompilationUnit());
    }

    public AkburaSyntaxTree WithChangedText(
        string newText,
        IEnumerable<TextChangeRange>? changes = null,
        CancellationToken cancellationToken = default)
    {
        return WithChangedText(SourceText.From(newText), changes, cancellationToken);
    }

    public AkburaDocumentSyntax GetRoot()
    {
        return _root ??= (AkburaDocumentSyntax)GreenRoot.CreateRed();
    }
}

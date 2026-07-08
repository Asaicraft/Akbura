using Akbura.Language.Syntax;
using Akbura.Language.Syntax.Green;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Akbura.Language;

internal sealed class AkcssSyntaxTree : AkburaSyntaxTree
{
    private AkcssDocumentSyntax? _root;

    private AkcssSyntaxTree(
        SourceText text,
        string filePath,
        string logicalName,
        GreenAkcssDocumentSyntax greenRoot)
        : base(text, filePath)
    {
        LogicalName = string.IsNullOrWhiteSpace(logicalName)
            ? GetDefaultLogicalName(filePath)
            : logicalName;
        GreenRoot = greenRoot;
    }

    public override SyntaxTreeKind Kind => SyntaxTreeKind.Akcss;

    public string LogicalName { get; }

    public GreenAkcssDocumentSyntax GreenRoot { get; }

    public new static AkcssSyntaxTree ParseText(string text, CancellationToken cancellationToken = default)
    {
        return ParseText(SourceText.From(text), filePath: string.Empty, logicalName: string.Empty, cancellationToken);
    }

    public new static AkcssSyntaxTree ParseText(
        string text,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        return ParseText(SourceText.From(text), filePath, logicalName: string.Empty, cancellationToken);
    }

    public static AkcssSyntaxTree ParseText(
        string text,
        string filePath,
        string logicalName,
        CancellationToken cancellationToken = default)
    {
        return ParseText(SourceText.From(text), filePath, logicalName, cancellationToken);
    }

    public new static AkcssSyntaxTree ParseText(SourceText text, CancellationToken cancellationToken = default)
    {
        return ParseText(text, filePath: string.Empty, logicalName: string.Empty, cancellationToken);
    }

    public static AkcssSyntaxTree ParseText(
        SourceText text,
        string filePath,
        string logicalName,
        CancellationToken cancellationToken = default)
    {
        var lexer = new Lexer(text);
        using var parser = new Parser(lexer, cancellationToken);

        return new AkcssSyntaxTree(text, filePath, logicalName, parser.ParseAkcssDocumentSyntax());
    }

    public AkcssSyntaxTree WithChangedText(
        SourceText newText,
        IEnumerable<TextChangeRange>? changes = null,
        CancellationToken cancellationToken = default)
    {
        var changeRanges = changes?.ToArray() ?? [.. newText.GetChangeRanges(Text)];
        if (changeRanges.Length == 0 && newText.ToString() == Text.ToString())
        {
            return this;
        }

        var lexer = new Lexer(newText);
        using var parser = new Parser(lexer, cancellationToken, GetRoot(), changeRanges);

        return new AkcssSyntaxTree(newText, FilePath, LogicalName, parser.ParseAkcssDocumentSyntax());
    }

    public AkcssSyntaxTree WithChangedText(
        string newText,
        IEnumerable<TextChangeRange>? changes = null,
        CancellationToken cancellationToken = default)
    {
        return WithChangedText(SourceText.From(newText), changes, cancellationToken);
    }

    public new AkcssDocumentSyntax GetRoot()
    {
        return _root ??= (AkcssDocumentSyntax)GreenRoot.CreateRed();
    }

    public override AkburaSyntax GetRootSyntax()
    {
        return GetRoot();
    }

    private static string GetDefaultLogicalName(string filePath)
    {
        return string.IsNullOrWhiteSpace(filePath)
            ? string.Empty
            : Path.GetFileName(filePath);
    }
}

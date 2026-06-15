using Akbura.Language.Syntax;
using Akbura.Language.Syntax.Green;
using Microsoft.CodeAnalysis.Text;
using System.IO;
using System.Threading;

namespace Akbura.Language;

internal sealed class AkcssSyntaxTree
{
    private AkcssDocumentSyntax? _root;

    private AkcssSyntaxTree(
        SourceText text,
        string filePath,
        string logicalName,
        GreenAkcssDocumentSyntax greenRoot)
    {
        Text = text;
        FilePath = filePath;
        LogicalName = string.IsNullOrWhiteSpace(logicalName)
            ? GetDefaultLogicalName(filePath)
            : logicalName;
        GreenRoot = greenRoot;
    }

    public SourceText Text { get; }

    public string FilePath { get; }

    public string LogicalName { get; }

    public GreenAkcssDocumentSyntax GreenRoot { get; }

    public static AkcssSyntaxTree ParseText(string text, CancellationToken cancellationToken = default)
    {
        return ParseText(SourceText.From(text), filePath: string.Empty, logicalName: string.Empty, cancellationToken);
    }

    public static AkcssSyntaxTree ParseText(
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

    public static AkcssSyntaxTree ParseText(SourceText text, CancellationToken cancellationToken = default)
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

    public AkcssDocumentSyntax GetRoot()
    {
        return _root ??= (AkcssDocumentSyntax)GreenRoot.CreateRed();
    }

    private static string GetDefaultLogicalName(string filePath)
    {
        return string.IsNullOrWhiteSpace(filePath)
            ? string.Empty
            : Path.GetFileName(filePath);
    }
}

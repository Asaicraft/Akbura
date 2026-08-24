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
            ? GetLogicalName(filePath)
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

    public new static AkcssSyntaxTree ParseText(
        SourceText text,
        string path,
        CancellationToken cancellationToken = default)
    {
        return ParseText(text, filePath: path, logicalName: GetLogicalName(path), cancellationToken);
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

        var oldRoot = GetRoot();
        if (oldRoot.ContainsDiagnostics || oldRoot.ContainsSkippedText)
        {
            // Recovery nodes are deliberately not a stable reuse boundary.
            return ParseText(
                newText,
                FilePath,
                LogicalName,
                cancellationToken);
        }

        var lexer = new Lexer(newText);
        using var parser = new Parser(lexer, cancellationToken, oldRoot, changeRanges);

        var newRoot = parser.ParseAkcssDocumentSyntax();

        if (newRoot.FullWidth != newText.Length ||
            newRoot.ContainsDiagnostics ||
            newRoot.ContainsSkippedText)
        {
            // A clean reuse tree can still choose an invalid boundary for the
            // current edit. Reparse immediately so one edit cannot publish
            // diagnostics absent from a full parse of the same text.
            return ParseText(
                newText,
                FilePath,
                LogicalName,
                cancellationToken);
        }

        return new AkcssSyntaxTree(newText, FilePath, LogicalName, newRoot);
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

    private static string GetLogicalName(string filePath)
    {
        return string.IsNullOrWhiteSpace(filePath)
            ? string.Empty
            : Path.GetFileName(filePath);
    }
}

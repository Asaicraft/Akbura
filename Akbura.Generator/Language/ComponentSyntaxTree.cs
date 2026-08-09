using Akbura.Language.Syntax;
using Akbura.Language.Syntax.Green;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace Akbura.Language;

internal sealed class ComponentSyntaxTree : AkburaSyntaxTree
{
    private AkburaDocumentSyntax? _root;

    private ComponentSyntaxTree(SourceText text, string filePath, GreenAkburaDocumentSyntax greenRoot)
        : base(text, filePath)
    {
        GreenRoot = greenRoot;
    }

    public override SyntaxTreeKind Kind => SyntaxTreeKind.Component;

    public override string ComponentName
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

    public new static ComponentSyntaxTree ParseText(string text, CancellationToken cancellationToken = default)
    {
        return ParseText(SourceText.From(text), filePath: string.Empty, cancellationToken);
    }

    public new static ComponentSyntaxTree ParseText(
        string text,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        return ParseText(SourceText.From(text), filePath, cancellationToken);
    }

    public new static ComponentSyntaxTree ParseText(SourceText text, CancellationToken cancellationToken = default)
    {
        return ParseText(text, filePath: string.Empty, cancellationToken);
    }

    public new static ComponentSyntaxTree ParseText(
        SourceText text,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var lexer = new Lexer(text);
        using var parser = new Parser(lexer, cancellationToken);

        return new ComponentSyntaxTree(text, filePath, parser.ParseCompilationUnit());
    }

    public ComponentSyntaxTree WithChangedText(
    SourceText newText,
    IEnumerable<TextChangeRange>? changes = null,
    CancellationToken cancellationToken = default)
    {
        Debug.WriteLine("[Akbura] SyntaxTree: GetChangeRanges started");

        var changeRanges = changes?.ToArray() ??
            [.. newText.GetChangeRanges(Text)];

        Debug.WriteLine(
            $"[Akbura] SyntaxTree: GetChangeRanges completed, " +
            $"count={changeRanges.Length}");

        if (changeRanges.Length == 0)
        {
            Debug.WriteLine("[Akbura] SyntaxTree: text comparison started");

            var contentEquals =
                newText.ContentEquals(Text);

            Debug.WriteLine(
                $"[Akbura] SyntaxTree: text comparison completed, " +
                $"equal={contentEquals}");

            if (contentEquals)
            {
                return this;
            }
        }

        Debug.WriteLine("[Akbura] SyntaxTree: parser construction started");

        var lexer = new Lexer(newText);

        using var parser = new Parser(
            lexer,
            cancellationToken,
            GetRoot(),
            changeRanges);

        Debug.WriteLine("[Akbura] SyntaxTree: ParseCompilationUnit started");

        var root = parser.ParseCompilationUnit();

        Debug.WriteLine("[Akbura] SyntaxTree: ParseCompilationUnit completed");

        return new ComponentSyntaxTree(
            newText,
            FilePath,
            root);
    }

    public ComponentSyntaxTree WithChangedText(
        string newText,
        IEnumerable<TextChangeRange>? changes = null,
        CancellationToken cancellationToken = default)
    {
        return WithChangedText(SourceText.From(newText), changes, cancellationToken);
    }

    public override AkburaDocumentSyntax GetRoot()
    {
        return _root ??= (AkburaDocumentSyntax)GreenRoot.CreateRed();
    }

    public override AkburaSyntax GetRootSyntax()
    {
        return GetRoot();
    }
}

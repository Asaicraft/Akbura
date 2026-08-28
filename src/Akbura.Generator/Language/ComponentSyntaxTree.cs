using Akbura.Language.Syntax;
using Akbura.Language.Syntax.Green;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
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
        AkburaWorkspaceDiagnostics.Write(
            AkburaWorkspaceDiagnostics.Category.Syntax,
            "SyntaxTree: GetChangeRanges started");

        var changeRanges = changes?.ToArray() ??
            [.. newText.GetChangeRanges(Text)];

        AkburaWorkspaceDiagnostics.Write(
            AkburaWorkspaceDiagnostics.Category.Syntax,
            $"SyntaxTree: GetChangeRanges completed, " +
            $"count={changeRanges.Length}");

        if (changeRanges.Length == 0)
        {
            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics.Category.Syntax,
                "SyntaxTree: text comparison started");

            var contentEquals =
                newText.ContentEquals(Text);

            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics.Category.Syntax,
                $"SyntaxTree: text comparison completed, " +
                $"equal={contentEquals}");

            if (contentEquals)
            {
                return this;
            }
        }

        AkburaWorkspaceDiagnostics.Write(
            AkburaWorkspaceDiagnostics.Category.Syntax,
            "SyntaxTree: parser construction started");

        var lexer = new Lexer(newText);

        using var parser = new Parser(
            lexer,
            cancellationToken,
            GetRoot(),
            changeRanges);

        AkburaWorkspaceDiagnostics.Write(
            AkburaWorkspaceDiagnostics.Category.Syntax,
            "SyntaxTree: ParseCompilationUnit started");

        var root = parser.ParseCompilationUnit();

        AkburaWorkspaceDiagnostics.Write(
            AkburaWorkspaceDiagnostics.Category.Syntax,
            "SyntaxTree: ParseCompilationUnit completed");

        if (root.FullWidth != newText.Length)
        {
            return ParseText(
                newText,
                FilePath,
                cancellationToken);
        }

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

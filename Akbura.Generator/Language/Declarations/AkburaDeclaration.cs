using Akbura.Language.Syntax;
using System;
using System.Collections.Immutable;

namespace Akbura.Language.Declarations;

internal sealed class AkburaDeclaration
{
    public AkburaDeclaration(
        AkburaDeclarationKind kind,
        string name,
        AkburaSyntax syntax,
        AkburaSyntaxTree? syntaxTree = null,
        AkcssSyntaxTree? akcssSyntaxTree = null,
        ImmutableArray<AkburaDeclaration> children = default)
    {
        Kind = kind;
        Name = name ?? string.Empty;
        Syntax = syntax ?? throw new ArgumentNullException(nameof(syntax));
        SyntaxTree = syntaxTree;
        AkcssSyntaxTree = akcssSyntaxTree;
        Children = children.IsDefault
            ? ImmutableArray<AkburaDeclaration>.Empty
            : children;
    }

    public AkburaDeclarationKind Kind { get; }

    public string Name { get; }

    public AkburaSyntax Syntax { get; }

    public AkburaSyntaxTree? SyntaxTree { get; }

    public AkcssSyntaxTree? AkcssSyntaxTree { get; }

    public ImmutableArray<AkburaDeclaration> Children { get; }

    public bool ContainsDiagnosticsOrSkippedText =>
        Syntax.ContainsDiagnostics || Syntax.ContainsSkippedText;

    public override string ToString()
    {
        return string.IsNullOrEmpty(Name)
            ? Kind.ToString()
            : $"{Kind} {Name}";
    }
}

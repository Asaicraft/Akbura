using Akbura.Language.Syntax;
using System;
using System.Collections.Immutable;

namespace Akbura.Language;

internal sealed class SingleDeclaration : Declaration
{
    private readonly DeclarationKind _kind;
    private readonly AkburaSyntax _syntax;
    private readonly ImmutableArray<Declaration> _children;

    public SingleDeclaration(
        DeclarationKind kind,
        string name,
        AkburaSyntax syntax,
        AkburaSyntaxTree? syntaxTree = null,
        AkcssSyntaxTree? akcssSyntaxTree = null,
        ImmutableArray<Declaration> children = default)
        : base(name ?? string.Empty)
    {
        _kind = kind;
        _syntax = syntax ?? throw new ArgumentNullException(nameof(syntax));
        SyntaxTree = syntaxTree;
        AkcssSyntaxTree = akcssSyntaxTree;
        _children = children.IsDefault
            ? ImmutableArray<Declaration>.Empty
            : children;
    }

    public override DeclarationKind Kind => _kind;

    public override AkburaSyntax Syntax => _syntax;

    public override AkburaSyntaxTree? SyntaxTree { get; }

    public override AkcssSyntaxTree? AkcssSyntaxTree { get; }

    protected override ImmutableArray<Declaration> GetDeclarationChildren()
    {
        return _children;
    }

    public override string ToString()
    {
        return string.IsNullOrEmpty(Name)
            ? Kind.ToString()
            : $"{Kind} {Name}";
    }
}

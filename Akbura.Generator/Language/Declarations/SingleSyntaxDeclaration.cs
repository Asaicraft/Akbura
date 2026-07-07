using Akbura.Language.Syntax;
using System.Collections.Immutable;

namespace Akbura.Language;

internal sealed class SingleSyntaxDeclaration : SingleDeclaration
{
    private readonly DeclarationKind _kind;
    private readonly ImmutableArray<Declaration> _children;

    public SingleSyntaxDeclaration(
        DeclarationKind kind,
        string name,
        AkburaSyntax syntax,
        AkburaSyntaxTree? syntaxTree = null,
        AkcssSyntaxTree? akcssSyntaxTree = null,
        ImmutableArray<Declaration> children = default,
        SourceLocation? nameLocation = null,
        ImmutableArray<AkburaDiagnostic> diagnostics = default)
        : base(
            name,
            syntax,
            nameLocation ?? new SourceLocation(syntax),
            diagnostics)
    {
        _kind = kind;
        SyntaxTree = syntaxTree;
        AkcssSyntaxTree = akcssSyntaxTree;
        _children = children.IsDefault
            ? ImmutableArray<Declaration>.Empty
            : children;
    }

    public override DeclarationKind Kind => _kind;

    public AkburaSyntaxTree? SyntaxTree { get; }

    public AkcssSyntaxTree? AkcssSyntaxTree { get; }

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

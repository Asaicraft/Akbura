// This file is ported and adapted from the Roslyn (dotnet/roslyn)

#nullable disable

using Akbura.Language.Syntax;
using System.Collections.Immutable;

namespace Akbura.Language;

internal abstract class SingleNamespaceOrTypeDeclaration : Declaration
{
    private readonly AkburaSyntax _syntax;
    private readonly SourceLocation _nameLocation;

    /// <summary>
    /// Any diagnostics reported while converting syntax into the Declaration instance.
    /// </summary>
    public readonly ImmutableArray<AkburaDiagnostic> Diagnostics;

    protected SingleNamespaceOrTypeDeclaration(
        string name,
        AkburaSyntax syntax,
        SourceLocation nameLocation,
        ImmutableArray<AkburaDiagnostic> diagnostics)
        : base(name)
    {
        _syntax = syntax;
        _nameLocation = nameLocation;
        Diagnostics = diagnostics.IsDefault
            ? ImmutableArray<AkburaDiagnostic>.Empty
            : diagnostics;
    }

    public SourceLocation Location
    {
        get
        {
            return new SourceLocation(Syntax);
        }
    }

    public AkburaSyntax Syntax
    {
        get
        {
            return _syntax;
        }
    }

    public SourceLocation NameLocation
    {
        get
        {
            return _nameLocation;
        }
    }

    protected override ImmutableArray<Declaration> GetDeclarationChildren()
    {
        return ImmutableArray<Declaration>.CastUp(GetNamespaceOrTypeDeclarationChildren());
    }

    public new ImmutableArray<SingleNamespaceOrTypeDeclaration> Children
    {
        get
        {
            return GetNamespaceOrTypeDeclarationChildren();
        }
    }

    protected abstract ImmutableArray<SingleNamespaceOrTypeDeclaration> GetNamespaceOrTypeDeclarationChildren();
}

// This file is ported and adapted from the Roslyn (dotnet/roslyn)

#nullable disable

using Akbura.Language.Syntax;
using System.Collections.Immutable;

namespace Akbura.Language;

internal abstract class SingleNamespaceOrTypeDeclaration : SingleDeclaration
{
    protected SingleNamespaceOrTypeDeclaration(
        string name,
        AkburaSyntax syntax,
        SourceLocation nameLocation,
        ImmutableArray<AkburaDiagnostic> diagnostics)
        : base(name, syntax, nameLocation, diagnostics)
    {
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

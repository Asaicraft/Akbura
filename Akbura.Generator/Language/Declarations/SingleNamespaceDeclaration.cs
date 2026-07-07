// This file is ported and adapted from the Roslyn (dotnet/roslyn)

#nullable disable

using Akbura.Language.Syntax;
using System.Collections.Immutable;

namespace Akbura.Language;

internal class SingleNamespaceDeclaration : SingleNamespaceOrTypeDeclaration
{
    private readonly ImmutableArray<SingleNamespaceOrTypeDeclaration> _children;

    protected SingleNamespaceDeclaration(
        string name,
        AkburaSyntax syntax,
        SourceLocation nameLocation,
        ImmutableArray<SingleNamespaceOrTypeDeclaration> children,
        ImmutableArray<AkburaDiagnostic> diagnostics)
        : base(name, syntax, nameLocation, diagnostics)
    {
        _children = children.IsDefault
            ? ImmutableArray<SingleNamespaceOrTypeDeclaration>.Empty
            : children;
    }

    public override DeclarationKind Kind
    {
        get
        {
            return DeclarationKind.Namespace;
        }
    }

    protected override ImmutableArray<SingleNamespaceOrTypeDeclaration> GetNamespaceOrTypeDeclarationChildren()
    {
        return _children;
    }

    public virtual bool HasGlobalUsings
    {
        get
        {
            return false;
        }
    }

    public virtual bool HasUsings
    {
        get
        {
            return false;
        }
    }

    public virtual bool HasExternAliases
    {
        get
        {
            return false;
        }
    }

    public static SingleNamespaceDeclaration Create(
        string name,
        bool hasUsings,
        bool hasExternAliases,
        AkburaSyntax syntax,
        SourceLocation nameLocation,
        ImmutableArray<SingleNamespaceOrTypeDeclaration> children,
        ImmutableArray<AkburaDiagnostic> diagnostics)
    {
        if (!hasUsings && !hasExternAliases)
        {
            return new SingleNamespaceDeclaration(
                name, syntax, nameLocation, children, diagnostics);
        }

        return new SingleNamespaceDeclarationEx(
            name, hasUsings, hasExternAliases, syntax, nameLocation, children, diagnostics);
    }
}

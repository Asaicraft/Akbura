// This file is ported and adapted from the Roslyn (dotnet/roslyn)

#nullable disable

using Akbura.Language.Syntax;
using System.Collections.Immutable;

namespace Akbura.Language;

internal sealed class SingleNamespaceDeclarationEx : SingleNamespaceDeclaration
{
    private readonly bool _hasUsings;
    private readonly bool _hasExternAliases;

    public SingleNamespaceDeclarationEx(
        string name,
        bool hasUsings,
        bool hasExternAliases,
        AkburaSyntax syntax,
        SourceLocation nameLocation,
        ImmutableArray<SingleNamespaceOrTypeDeclaration> children,
        ImmutableArray<AkburaDiagnostic> diagnostics)
        : base(name, syntax, nameLocation, children, diagnostics)
    {
        _hasUsings = hasUsings;
        _hasExternAliases = hasExternAliases;
    }

    public override bool HasUsings
    {
        get
        {
            return _hasUsings;
        }
    }

    public override bool HasExternAliases
    {
        get
        {
            return _hasExternAliases;
        }
    }
}

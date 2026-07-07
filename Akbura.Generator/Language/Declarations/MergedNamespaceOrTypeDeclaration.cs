// This file is ported and adapted from the Roslyn (dotnet/roslyn)

#nullable disable

namespace Akbura.Language;

internal abstract class MergedNamespaceOrTypeDeclaration : Declaration
{
    protected MergedNamespaceOrTypeDeclaration(string name)
        : base(name)
    {
    }
}

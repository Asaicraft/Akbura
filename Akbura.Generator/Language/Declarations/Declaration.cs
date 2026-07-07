// This file is ported and adapted from the Roslyn (dotnet/roslyn)

#nullable disable

using System.Collections.Immutable;

namespace Akbura.Language;

/// <summary>
/// A Declaration summarizes the declaration structure of a source file.
/// </summary>
internal abstract class Declaration
{
    protected readonly string name;

    protected Declaration(string name)
    {
        this.name = name;
    }

    public string Name
    {
        get
        {
            return name;
        }
    }

    public ImmutableArray<Declaration> Children => GetDeclarationChildren();

    public abstract DeclarationKind Kind { get; }

    protected abstract ImmutableArray<Declaration> GetDeclarationChildren();
}

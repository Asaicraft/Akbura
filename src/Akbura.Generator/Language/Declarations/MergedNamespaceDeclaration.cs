// This file is ported and adapted from the Roslyn (dotnet/roslyn)

#nullable disable

using Akbura.Pools;
using System.Collections.Immutable;

namespace Akbura.Language;

internal sealed class MergedNamespaceDeclaration : MergedNamespaceOrTypeDeclaration
{
    private readonly ImmutableArray<Declaration> _declarations;
    private ImmutableArray<Declaration> _children;

    private MergedNamespaceDeclaration(
        string name,
        ImmutableArray<Declaration> declarations)
        : base(name)
    {
        _declarations = declarations.IsDefault
            ? ImmutableArray<Declaration>.Empty
            : declarations;
    }

    public override DeclarationKind Kind => DeclarationKind.Namespace;

    public ImmutableArray<Declaration> Declarations => _declarations;

    public static MergedNamespaceDeclaration Create(ImmutableArray<Declaration> declarations)
    {
        return new MergedNamespaceDeclaration(string.Empty, declarations);
    }

    protected override ImmutableArray<Declaration> GetDeclarationChildren()
    {
        if (_children.IsDefault)
        {
            ImmutableInterlocked.InterlockedInitialize(
                ref _children,
                CreateChildren());
        }

        return _children;
    }

    private ImmutableArray<Declaration> CreateChildren()
    {
        if (_declarations.IsDefaultOrEmpty)
        {
            return ImmutableArray<Declaration>.Empty;
        }

        var builder = ArrayBuilder<Declaration>.GetInstance();
        foreach (var declaration in _declarations)
        {
            if (declaration == null)
            {
                continue;
            }

            builder.AddRange(declaration.Children);
        }

        return builder.ToImmutableAndFree();
    }
}

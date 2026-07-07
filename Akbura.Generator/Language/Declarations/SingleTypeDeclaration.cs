// This file is ported and adapted from the Roslyn (dotnet/roslyn)

#nullable disable

using Akbura.Language.Syntax;
using System;
using System.Collections.Immutable;

namespace Akbura.Language;

internal sealed class SingleTypeDeclaration : SingleNamespaceOrTypeDeclaration
{
    private readonly DeclarationKind _kind;
    private readonly ImmutableArray<SingleNamespaceOrTypeDeclaration> _children;

    public SingleTypeDeclaration(
        DeclarationKind kind,
        string name,
        int arity,
        DeclarationModifiers modifiers,
        TypeDeclarationFlags declFlags,
        AkburaSyntax syntax,
        SourceLocation nameLocation,
        ImmutableArray<string> memberNames,
        ImmutableArray<SingleTypeDeclaration> children,
        ImmutableArray<AkburaDiagnostic> diagnostics,
        QuickAttributes quickAttributes)
        : base(name, syntax, nameLocation, diagnostics)
    {
        _kind = kind;
        Arity = arity;
        Modifiers = modifiers;
        DeclarationFlags = declFlags;
        MemberNames = memberNames.IsDefault
            ? ImmutableArray<string>.Empty
            : memberNames;
        _children = children.IsDefault
            ? ImmutableArray<SingleNamespaceOrTypeDeclaration>.Empty
            : ImmutableArray<SingleNamespaceOrTypeDeclaration>.CastUp(children);
        QuickAttributes = quickAttributes;
    }

    public override DeclarationKind Kind
    {
        get
        {
            return _kind;
        }
    }

    public int Arity { get; }

    public DeclarationModifiers Modifiers { get; }

    public TypeDeclarationFlags DeclarationFlags { get; }

    public ImmutableArray<string> MemberNames { get; }

    public QuickAttributes QuickAttributes { get; }

    protected override ImmutableArray<SingleNamespaceOrTypeDeclaration> GetNamespaceOrTypeDeclarationChildren()
    {
        return _children;
    }

    [Flags]
    internal enum TypeDeclarationFlags
    {
        None = 0,
    }
}

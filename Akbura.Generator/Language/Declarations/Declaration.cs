// This file is ported and adapted from the Roslyn (dotnet/roslyn)

#nullable disable

using System.Collections.Immutable;
using Akbura.Language.Syntax;

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

    public ImmutableArray<Declaration> Children
    {
        get
        {
            return GetDeclarationChildren();
        }
    }

    public abstract DeclarationKind Kind { get; }

    public virtual AkburaSyntax Syntax
    {
        get
        {
            return null;
        }
    }

    public virtual AkburaSyntaxTree SyntaxTree
    {
        get
        {
            return null;
        }
    }

    public virtual AkcssSyntaxTree AkcssSyntaxTree
    {
        get
        {
            return null;
        }
    }

    public bool ContainsDiagnosticsOrSkippedText
    {
        get
        {
            var syntax = Syntax;
            return syntax != null &&
                   (syntax.ContainsDiagnostics || syntax.ContainsSkippedText);
        }
    }

    protected abstract ImmutableArray<Declaration> GetDeclarationChildren();
}

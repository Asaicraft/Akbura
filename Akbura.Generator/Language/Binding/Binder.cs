using Akbura.Language.Declarations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace Akbura.Language.Binding;

internal abstract class Binder
{
    protected Binder(
        AkburaCompilation compilation,
        Binder? parent,
        AkburaDeclaration? declaration)
    {
        Compilation = compilation;
        Parent = parent;
        Declaration = declaration;
    }

    public AkburaCompilation Compilation { get; }

    public Binder? Parent { get; }

    public AkburaDeclaration? Declaration { get; }

    public virtual string ScopeKey =>
        Declaration == null
            ? GetType().Name
            : $"{GetType().Name}:{Declaration.Kind}:{Declaration.Name}:{Declaration.Syntax.FullSpan}";

    public virtual AkburaSymbolInfo LookupSymbol(AkburaSyntax syntax)
    {
        return Parent?.LookupSymbol(syntax) ??
               AkburaSymbolInfo.None(Symbols.CandidateReason.UnsupportedSyntax);
    }

    public virtual ImmutableArray<Diagnostic> GetCSharpDiagnostics()
    {
        return ImmutableArray<Diagnostic>.Empty;
    }
}

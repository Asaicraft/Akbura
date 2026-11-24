using Akbura.Language.Binder;
using Akbura.Language.Symbols;
using Microsoft.CodeAnalysis.Text;
using AkburaSymbol = Akbura.Language.Symbols.ISymbol;

namespace Akbura.Workspaces.Navigation;

internal readonly struct AkcssResolvedReference
{
    public AkcssResolvedReference(
        AkcssReferenceKind kind,
        TextSpan sourceSpan,
        AkburaSymbol? symbol,
        CSharpSymbolDefinition csharpDefinition,
        AkcssBindingContext? context = null,
        AkcssTemplateBindingProperty? binding = null,
        bool hasAmbiguousTemplateSource = false)
    {
        Kind = kind;
        SourceSpan = sourceSpan;
        Symbol = symbol;
        CSharpDefinition = csharpDefinition;
        Context = context;
        TemplateBinding = binding;
        HasAmbiguousTemplateSource = hasAmbiguousTemplateSource;
    }

    public AkcssReferenceKind Kind { get; }

    public TextSpan SourceSpan { get; }

    public AkburaSymbol? Symbol { get; }

    public CSharpSymbolDefinition CSharpDefinition { get; }

    public AkcssBindingContext? Context { get; }

    public AkcssTemplateBindingProperty? TemplateBinding { get; }

    public bool HasAmbiguousTemplateSource { get; }
}

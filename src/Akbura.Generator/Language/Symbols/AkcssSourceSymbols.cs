using Akbura.Language.Syntax;
using System.Collections.Immutable;

namespace Akbura.Language.Symbols;

internal interface IAkcssDeclarationOwner : ISymbol
{
    AkburaSyntax? DeclarationSyntax { get; }

    CSharpSymbolDefinition TargetType { get; }
}

internal abstract class AkcssSourceDeclarationSymbol(
    AkburaSemanticModel definitionModel,
    AkburaSyntax moduleSyntax,
    AkburaSyntax declarationSyntax,
    CSharpSymbolDefinition targetType) : Symbol, IAkcssDeclarationOwner
{
    public AkburaSemanticModel DefinitionModel { get; } = definitionModel;

    public AkburaSyntax ModuleSyntax { get; } = moduleSyntax;

    public AkburaSyntax DeclarationSyntax { get; } = declarationSyntax;

    public CSharpSymbolDefinition TargetType { get; } = targetType;

    public IAkcssModuleSymbol? Module => DefinitionModel.GetDeclarationSymbolInfo(ModuleSyntax).Symbol as IAkcssModuleSymbol;

    public override ISymbol? ContainingSymbol => Module;

    public override SymbolLanguage Language => SymbolLanguage.Akcss;
}

internal sealed class AkcssThemeSymbol(
    AkburaSemanticModel definitionModel,
    AkburaSyntax moduleSyntax,
    AkcssThemeDeclarationSyntax declarationSyntax,
    CSharpSymbolDefinition targetType)
    : AkcssSourceDeclarationSymbol(definitionModel, moduleSyntax, declarationSyntax, targetType)
{
    public override SymbolKind Kind => SymbolKind.AkcssTheme;

    public new AkcssThemeDeclarationSyntax DeclarationSyntax => (AkcssThemeDeclarationSyntax)base.DeclarationSyntax;

    public override string Name => DeclarationSyntax.Name.Identifier.ValueText;

    public override void Accept(SymbolVisitor visitor) => visitor.VisitAkcssTheme(this);

    public override TResult Accept<TResult>(SymbolVisitor<TResult> visitor) => visitor.VisitAkcssTheme(this);

    public override TResult Accept<TParameter, TResult>(SymbolVisitor<TParameter, TResult> visitor, TParameter parameter) =>
        visitor.VisitAkcssTheme(this, parameter);
}

internal sealed class AkcssMixinSymbol : AkcssSourceDeclarationSymbol
{
    public AkcssMixinSymbol(
        AkburaSemanticModel definitionModel,
        AkburaSyntax moduleSyntax,
        AkcssMixinDeclarationSyntax declarationSyntax,
        CSharpSymbolDefinition targetType,
        ImmutableArray<CSharpSymbolDefinition> parameterTypes)
        : base(definitionModel, moduleSyntax, declarationSyntax, targetType)
    {
        var parameters = ImmutableArray.CreateBuilder<AkcssMixinParameterSymbol>(parameterTypes.Length);
        for (var ordinal = 0; ordinal < parameterTypes.Length; ordinal++)
        {
            parameters.Add(new AkcssMixinParameterSymbol(
                this, declarationSyntax.Parameters.Parameters[ordinal], parameterTypes[ordinal], ordinal));
        }

        Parameters = parameters.MoveToImmutable();
    }

    public override SymbolKind Kind => SymbolKind.AkcssMixin;

    public new AkcssMixinDeclarationSyntax DeclarationSyntax => (AkcssMixinDeclarationSyntax)base.DeclarationSyntax;

    public override string Name => DeclarationSyntax.Name.Identifier.ValueText;

    public ImmutableArray<AkcssMixinParameterSymbol> Parameters { get; }

    public override void Accept(SymbolVisitor visitor) => visitor.VisitAkcssMixin(this);

    public override TResult Accept<TResult>(SymbolVisitor<TResult> visitor) => visitor.VisitAkcssMixin(this);

    public override TResult Accept<TParameter, TResult>(SymbolVisitor<TParameter, TResult> visitor, TParameter parameter) =>
        visitor.VisitAkcssMixin(this, parameter);
}

internal sealed class AkcssMixinParameterSymbol(
    AkcssMixinSymbol mixin,
    AkcssMixinParameterSyntax declarationSyntax,
    CSharpSymbolDefinition type,
    int ordinal) : Symbol(mixin)
{
    public override SymbolKind Kind => SymbolKind.AkcssMixinParameter;

    public override SymbolLanguage Language => SymbolLanguage.Akcss;

    public override string Name => DeclarationSyntax.Name.Identifier.ValueText;

    public AkcssMixinParameterSyntax DeclarationSyntax { get; } = declarationSyntax;

    public CSharpSymbolDefinition Type { get; } = type;

    public int Ordinal { get; } = ordinal;

    public bool IsOptional => DeclarationSyntax.Default is not null;

    public override void Accept(SymbolVisitor visitor) => visitor.VisitAkcssMixinParameter(this);

    public override TResult Accept<TResult>(SymbolVisitor<TResult> visitor) => visitor.VisitAkcssMixinParameter(this);

    public override TResult Accept<TParameter, TResult>(SymbolVisitor<TParameter, TResult> visitor, TParameter parameter) =>
        visitor.VisitAkcssMixinParameter(this, parameter);
}

internal sealed class AkcssSelectorRuleSymbol(
    AkburaSemanticModel definitionModel,
    AkburaSyntax moduleSyntax,
    AkcssSelectorRuleSyntax declarationSyntax)
    : AkcssSourceDeclarationSymbol(definitionModel, moduleSyntax, declarationSyntax, default)
{
    public override SymbolKind Kind => SymbolKind.AkcssSelectorRule;

    public new AkcssSelectorRuleSyntax DeclarationSyntax => (AkcssSelectorRuleSyntax)base.DeclarationSyntax;

    public override string Name => string.Empty;

    public override void Accept(SymbolVisitor visitor) => visitor.VisitAkcssSelectorRule(this);

    public override TResult Accept<TResult>(SymbolVisitor<TResult> visitor) => visitor.VisitAkcssSelectorRule(this);

    public override TResult Accept<TParameter, TResult>(SymbolVisitor<TParameter, TResult> visitor, TParameter parameter) =>
        visitor.VisitAkcssSelectorRule(this, parameter);
}

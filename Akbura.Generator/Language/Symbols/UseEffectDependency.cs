namespace Akbura.Language.Symbols;

internal readonly struct UseEffectDependency
{
    public UseEffectDependency(
        string expressionText,
        ISymbol? akburaSymbol,
        CSharpSymbolDefinition csharpDefinition)
    {
        ExpressionText = expressionText ?? string.Empty;
        AkburaSymbol = akburaSymbol;
        CSharpDefinition = csharpDefinition;
    }

    public string ExpressionText { get; }

    public ISymbol? AkburaSymbol { get; }

    public CSharpSymbolDefinition CSharpDefinition { get; }

    public bool IsResolved => AkburaSymbol != null || !CSharpDefinition.IsDefault;
}

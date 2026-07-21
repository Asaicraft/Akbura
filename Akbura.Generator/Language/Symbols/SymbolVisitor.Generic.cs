namespace Akbura.Language.Symbols;

internal abstract class SymbolVisitor<TResult>
{
    public virtual TResult DefaultVisit(ISymbol symbol)
    {
        return default!;
    }

    public virtual TResult Visit(ISymbol? symbol)
    {
        return symbol == null
            ? default!
            : symbol.Accept(this);
    }

    public virtual TResult VisitAkburaComponent(IAkburaComponentSymbol symbol) => VisitMarkupComponent(symbol);

    public virtual TResult VisitMarkupComponent(IMarkupComponentSymbol symbol) => DefaultVisit(symbol);

    public virtual TResult VisitState(IStateSymbol symbol) => DefaultVisit(symbol);

    public virtual TResult VisitParameter(IParamSymbol symbol) => DefaultVisit(symbol);

    public virtual TResult VisitInject(IInjectSymbol symbol) => DefaultVisit(symbol);

    public virtual TResult VisitCommand(ICommandSymbol symbol) => DefaultVisit(symbol);

    public virtual TResult VisitCommandParameter(ICommandParameterSymbol symbol) => DefaultVisit(symbol);

    public virtual TResult VisitUseHook(IUseHookSymbol symbol) => DefaultVisit(symbol);

    public virtual TResult VisitProperty(IPropertySymbol symbol) => DefaultVisit(symbol);

    public virtual TResult VisitRoutedEvent(IRoutedEventSymbol symbol) => DefaultVisit(symbol);

    public virtual TResult VisitAkcssModule(IAkcssModuleSymbol symbol) => DefaultVisit(symbol);

    public virtual TResult VisitAkcss(IAkcssSymbol symbol) => DefaultVisit(symbol);

    public virtual TResult VisitTailwindUtility(ITailwindUtilitySymbol symbol) => VisitAkcss(symbol);

    public virtual TResult VisitTailwindUtilityParameter(ITailwindUtilityParameterSymbol symbol) => DefaultVisit(symbol);

    public virtual TResult VisitMarkupItem(IMarkupItemSymbol symbol) => DefaultVisit(symbol);

    public virtual TResult VisitMarkupName(IMarkupNameSymbol symbol) => DefaultVisit(symbol);

    public virtual TResult VisitCSharpSymbol(CSharpLocalSymbol symbol) => DefaultVisit(symbol);
}

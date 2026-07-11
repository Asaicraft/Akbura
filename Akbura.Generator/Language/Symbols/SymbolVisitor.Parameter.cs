namespace Akbura.Language.Symbols;

internal abstract class SymbolVisitor<TParameter, TResult>
{
    public virtual TResult DefaultVisit(ISymbol symbol, TParameter parameter)
    {
        return default!;
    }

    public virtual TResult Visit(ISymbol? symbol, TParameter parameter)
    {
        return symbol == null
            ? default!
            : symbol.Accept(this, parameter);
    }

    public virtual TResult VisitAkburaComponent(
        IAkburaComponentSymbol symbol,
        TParameter parameter) =>
        VisitMarkupComponent(symbol, parameter);

    public virtual TResult VisitMarkupComponent(
        IMarkupComponentSymbol symbol,
        TParameter parameter) =>
        DefaultVisit(symbol, parameter);

    public virtual TResult VisitState(IStateSymbol symbol, TParameter parameter) => DefaultVisit(symbol, parameter);

    public virtual TResult VisitParameter(IParamSymbol symbol, TParameter parameter) => DefaultVisit(symbol, parameter);

    public virtual TResult VisitInject(IInjectSymbol symbol, TParameter parameter) => DefaultVisit(symbol, parameter);

    public virtual TResult VisitCommand(ICommandSymbol symbol, TParameter parameter) => DefaultVisit(symbol, parameter);

    public virtual TResult VisitCommandParameter(
        ICommandParameterSymbol symbol,
        TParameter parameter) =>
        DefaultVisit(symbol, parameter);

    public virtual TResult VisitUseEffect(IUseEffectSymbol symbol, TParameter parameter) => DefaultVisit(symbol, parameter);

    public virtual TResult VisitUserHook(IUserHookSymbol symbol, TParameter parameter) => DefaultVisit(symbol, parameter);

    public virtual TResult VisitProperty(IPropertySymbol symbol, TParameter parameter) => DefaultVisit(symbol, parameter);

    public virtual TResult VisitRoutedEvent(IRoutedEventSymbol symbol, TParameter parameter) => DefaultVisit(symbol, parameter);

    public virtual TResult VisitAkcssModule(
        IAkcssModuleSymbol symbol,
        TParameter parameter) =>
        DefaultVisit(symbol, parameter);

    public virtual TResult VisitAkcss(IAkcssSymbol symbol, TParameter parameter) => DefaultVisit(symbol, parameter);

    public virtual TResult VisitTailwindUtility(
        ITailwindUtilitySymbol symbol,
        TParameter parameter) =>
        VisitAkcss(symbol, parameter);

    public virtual TResult VisitTailwindUtilityParameter(
        ITailwindUtilityParameterSymbol symbol,
        TParameter parameter) =>
        DefaultVisit(symbol, parameter);

    public virtual TResult VisitMarkupItem(
        IMarkupItemSymbol symbol,
        TParameter parameter) =>
        DefaultVisit(symbol, parameter);

    public virtual TResult VisitCSharpSymbol(
        CSharpLocalSymbol symbol,
        TParameter parameter) =>
        DefaultVisit(symbol, parameter);
}

namespace Akbura.Language.Symbols;

internal abstract class SymbolVisitor
{
    public virtual void DefaultVisit(ISymbol symbol)
    {
    }

    public virtual void Visit(ISymbol? symbol)
    {
        symbol?.Accept(this);
    }

    public virtual void VisitAkburaComponent(IAkburaComponentSymbol symbol) => VisitMarkupComponent(symbol);

    public virtual void VisitMarkupComponent(IMarkupComponentSymbol symbol) => DefaultVisit(symbol);

    public virtual void VisitState(IStateSymbol symbol) => DefaultVisit(symbol);

    public virtual void VisitParameter(IParamSymbol symbol) => DefaultVisit(symbol);

    public virtual void VisitInject(IInjectSymbol symbol) => DefaultVisit(symbol);

    public virtual void VisitCommand(ICommandSymbol symbol) => DefaultVisit(symbol);

    public virtual void VisitCommandParameter(ICommandParameterSymbol symbol) => DefaultVisit(symbol);

    public virtual void VisitUseEffect(IUseEffectSymbol symbol) => DefaultVisit(symbol);

    public virtual void VisitUserHook(IUserHookSymbol symbol) => DefaultVisit(symbol);

    public virtual void VisitProperty(IPropertySymbol symbol) => DefaultVisit(symbol);

    public virtual void VisitRoutedEvent(IRoutedEventSymbol symbol) => DefaultVisit(symbol);

    public virtual void VisitAkcssModule(IAkcssModuleSymbol symbol) => DefaultVisit(symbol);

    public virtual void VisitAkcss(IAkcssSymbol symbol) => DefaultVisit(symbol);

    public virtual void VisitTailwindUtility(ITailwindUtilitySymbol symbol) => VisitAkcss(symbol);

    public virtual void VisitTailwindUtilityParameter(ITailwindUtilityParameterSymbol symbol) => DefaultVisit(symbol);
}

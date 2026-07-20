namespace Akbura.Language.Operations;

internal abstract class OperationVisitor<TParameter, TResult>
{
    public virtual TResult? DefaultVisit(IOperation operation, TParameter parameter)
    {
        return default;
    }

    public virtual TResult? Visit(IOperation? operation, TParameter parameter)
    {
        return operation == null
            ? default
            : operation.Accept(this, parameter);
    }

    public virtual TResult? VisitCSharpOperation(
        ICSharpOperation operation,
        TParameter parameter) =>
        DefaultVisit(operation, parameter);

    public virtual TResult? VisitUseHook(
        IUseHookOperation operation,
        TParameter parameter) =>
        DefaultVisit(operation, parameter);

    public virtual TResult? VisitAkcssPropertySetter(
        IAkcssPropertySetterOperation operation,
        TParameter parameter) =>
        DefaultVisit(operation, parameter);

    public virtual TResult? VisitAkcssIf(
        IAkcssIfOperation operation,
        TParameter parameter) =>
        DefaultVisit(operation, parameter);

    public virtual TResult? VisitAkcssApply(
        IAkcssApplyOperation operation,
        TParameter parameter) =>
        DefaultVisit(operation, parameter);

    public virtual TResult? VisitAkcssIntercept(
        IAkcssInterceptOperation operation,
        TParameter parameter) =>
        DefaultVisit(operation, parameter);

    public virtual TResult? VisitMarkupContent(
        IMarkupContentOperation operation,
        TParameter parameter) =>
        DefaultVisit(operation, parameter);

    public virtual TResult? VisitMarkupPropertySetter(
        IMarkupPropertySetterOperation operation,
        TParameter parameter) =>
        DefaultVisit(operation, parameter);

    public virtual TResult? VisitMarkupCommandBinding(
        IMarkupCommandBindingOperation operation,
        TParameter parameter) =>
        DefaultVisit(operation, parameter);

    public virtual TResult? VisitMarkupRoutedEventBinding(
        IMarkupRoutedEventBindingOperation operation,
        TParameter parameter) =>
        DefaultVisit(operation, parameter);

    public virtual TResult? VisitTailwindUtilityAttribute(
        ITailwindUtilityAttributeOperation operation,
        TParameter parameter) =>
        DefaultVisit(operation, parameter);
}

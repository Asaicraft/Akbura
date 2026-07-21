namespace Akbura.Language.Operations;

internal abstract class OperationVisitor
{
    public virtual void DefaultVisit(IOperation operation)
    {
    }

    public virtual void Visit(IOperation? operation)
    {
        operation?.Accept(this);
    }

    public virtual void VisitCSharpOperation(ICSharpOperation operation) => DefaultVisit(operation);

    public virtual void VisitUseHook(IUseHookOperation operation) => DefaultVisit(operation);

    public virtual void VisitAkcssPropertySetter(IAkcssPropertySetterOperation operation) => DefaultVisit(operation);

    public virtual void VisitAkcssIf(IAkcssIfOperation operation) => DefaultVisit(operation);

    public virtual void VisitAkcssApply(IAkcssApplyOperation operation) => DefaultVisit(operation);

    public virtual void VisitAkcssIntercept(IAkcssInterceptOperation operation) => DefaultVisit(operation);

    public virtual void VisitMarkupContent(IMarkupContentOperation operation) => DefaultVisit(operation);

    public virtual void VisitMarkupNameAssignment(IMarkupNameAssignmentOperation operation) => DefaultVisit(operation);

    public virtual void VisitMarkupPropertySetter(IMarkupPropertySetterOperation operation) => DefaultVisit(operation);

    public virtual void VisitMarkupCommandBinding(IMarkupCommandBindingOperation operation) => DefaultVisit(operation);

    public virtual void VisitMarkupRoutedEventBinding(IMarkupRoutedEventBindingOperation operation) => DefaultVisit(operation);

    public virtual void VisitTailwindUtilityAttribute(ITailwindUtilityAttributeOperation operation) => DefaultVisit(operation);
}

namespace Akbura.Language.Operations;

internal class OperationWalker : OperationVisitor
{
    private int _recursionDepth;

    public override void Visit(IOperation? operation)
    {
        if (operation == null)
        {
            return;
        }

        _recursionDepth++;
        StackGuard.EnsureSufficientExecutionStack(_recursionDepth);

        try
        {
            operation.Accept(this);
        }
        finally
        {
            _recursionDepth--;
        }
    }

    public override void DefaultVisit(IOperation operation)
    {
        foreach (var child in operation.Children)
        {
            Visit(child);
        }
    }
}

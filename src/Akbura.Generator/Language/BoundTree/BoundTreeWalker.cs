namespace Akbura.Language.BoundTree;

internal class BoundTreeWalker : BoundTreeVisitor
{
    private int _recursionDepth;

    public override void Visit(BoundNode? node)
    {
        if (node == null)
        {
            return;
        }

        _recursionDepth++;
        StackGuard.EnsureSufficientExecutionStack(_recursionDepth);

        try
        {
            node.Accept(this);
        }
        finally
        {
            _recursionDepth--;
        }
    }

    public override void DefaultVisit(BoundNode node)
    {
        foreach (var child in node.Children)
        {
            Visit(child);
        }
    }
}

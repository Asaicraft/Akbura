namespace Akbura.Language.BoundTree;

internal abstract class BoundTreeVisitor
{
    public virtual void DefaultVisit(BoundNode node)
    {
    }

    public virtual void Visit(BoundNode? node)
    {
        node?.Accept(this);
    }

    public virtual void VisitStatement(BoundStatement node) => DefaultVisit(node);

    public virtual void VisitBlock(BoundBlock node) => VisitStatement(node);

    public virtual void VisitBadStatement(BoundBadStatement node) => VisitStatement(node);

    public virtual void VisitLocalDeclarationStatement(BoundLocalDeclarationStatement node) => VisitStatement(node);

    public virtual void VisitDeclaration(BoundDeclaration node) => DefaultVisit(node);

    public virtual void VisitExpression(BoundExpression node) => DefaultVisit(node);

    public virtual void VisitCSharpExpression(BoundCSharpExpression node) => VisitExpression(node);

    public virtual void VisitConversionExpression(BoundConversionExpression node) => VisitExpression(node);

    public virtual void VisitLiteralExpression(BoundLiteralExpression node) => VisitExpression(node);

    public virtual void VisitBinaryExpression(BoundBinaryExpression node) => VisitExpression(node);

    public virtual void VisitCallExpression(BoundCallExpression node) => VisitExpression(node);

    public virtual void VisitErrorExpression(BoundErrorExpression node) => VisitExpression(node);
}

internal abstract class BoundTreeVisitor<TResult>
{
    public virtual TResult? DefaultVisit(BoundNode node)
    {
        return default;
    }

    public virtual TResult? Visit(BoundNode? node)
    {
        return node == null
            ? default
            : node.Accept(this);
    }

    public virtual TResult? VisitStatement(BoundStatement node) => DefaultVisit(node);

    public virtual TResult? VisitBlock(BoundBlock node) => VisitStatement(node);

    public virtual TResult? VisitBadStatement(BoundBadStatement node) => VisitStatement(node);

    public virtual TResult? VisitLocalDeclarationStatement(BoundLocalDeclarationStatement node) =>
        VisitStatement(node);

    public virtual TResult? VisitDeclaration(BoundDeclaration node) => DefaultVisit(node);

    public virtual TResult? VisitExpression(BoundExpression node) => DefaultVisit(node);

    public virtual TResult? VisitCSharpExpression(BoundCSharpExpression node) => VisitExpression(node);

    public virtual TResult? VisitConversionExpression(BoundConversionExpression node) => VisitExpression(node);

    public virtual TResult? VisitLiteralExpression(BoundLiteralExpression node) => VisitExpression(node);

    public virtual TResult? VisitBinaryExpression(BoundBinaryExpression node) => VisitExpression(node);

    public virtual TResult? VisitCallExpression(BoundCallExpression node) => VisitExpression(node);

    public virtual TResult? VisitErrorExpression(BoundErrorExpression node) => VisitExpression(node);
}

internal abstract class BoundTreeVisitor<TParameter, TResult>
{
    public virtual TResult? DefaultVisit(BoundNode node, TParameter parameter)
    {
        return default;
    }

    public virtual TResult? Visit(BoundNode? node, TParameter parameter)
    {
        return node == null
            ? default
            : node.Accept(this, parameter);
    }

    public virtual TResult? VisitStatement(BoundStatement node, TParameter parameter) =>
        DefaultVisit(node, parameter);

    public virtual TResult? VisitBlock(BoundBlock node, TParameter parameter) =>
        VisitStatement(node, parameter);

    public virtual TResult? VisitBadStatement(BoundBadStatement node, TParameter parameter) =>
        VisitStatement(node, parameter);

    public virtual TResult? VisitLocalDeclarationStatement(
        BoundLocalDeclarationStatement node,
        TParameter parameter) =>
        VisitStatement(node, parameter);

    public virtual TResult? VisitDeclaration(BoundDeclaration node, TParameter parameter) =>
        DefaultVisit(node, parameter);

    public virtual TResult? VisitExpression(BoundExpression node, TParameter parameter) =>
        DefaultVisit(node, parameter);

    public virtual TResult? VisitCSharpExpression(BoundCSharpExpression node, TParameter parameter) =>
        VisitExpression(node, parameter);

    public virtual TResult? VisitConversionExpression(BoundConversionExpression node, TParameter parameter) =>
        VisitExpression(node, parameter);

    public virtual TResult? VisitLiteralExpression(BoundLiteralExpression node, TParameter parameter) =>
        VisitExpression(node, parameter);

    public virtual TResult? VisitBinaryExpression(BoundBinaryExpression node, TParameter parameter) =>
        VisitExpression(node, parameter);

    public virtual TResult? VisitCallExpression(BoundCallExpression node, TParameter parameter) =>
        VisitExpression(node, parameter);

    public virtual TResult? VisitErrorExpression(BoundErrorExpression node, TParameter parameter) =>
        VisitExpression(node, parameter);
}

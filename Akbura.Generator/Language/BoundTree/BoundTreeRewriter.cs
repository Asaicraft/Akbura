using Akbura.Pools;
using System.Collections.Immutable;

namespace Akbura.Language.BoundTree;

internal class BoundTreeRewriter : BoundTreeVisitor<BoundNode?>
{
    private int _recursionDepth;

    public override BoundNode? Visit(BoundNode? node)
    {
        if (node == null)
        {
            return null;
        }

        _recursionDepth++;
        StackGuard.EnsureSufficientExecutionStack(_recursionDepth);

        try
        {
            return node.Accept(this);
        }
        finally
        {
            _recursionDepth--;
        }
    }

    public override BoundNode? DefaultVisit(BoundNode node)
    {
        return node;
    }

    public override BoundNode? VisitBlock(BoundBlock node)
    {
        var statements = VisitList(node.Statements);
        if (statements == node.Statements)
        {
            return node;
        }

        return new BoundBlock(
            node.Syntax,
            node.Binder,
            node.DeclaredSymbols,
            statements,
            node.Diagnostics);
    }

    public override BoundNode? VisitBadStatement(BoundBadStatement node)
    {
        var children = VisitList(node.Children);
        if (children == node.Children)
        {
            return node;
        }

        return new BoundBadStatement(
            node.Syntax,
            node.Binder,
            node.Diagnostics,
            children);
    }

    public override BoundNode? VisitLocalDeclarationStatement(BoundLocalDeclarationStatement node)
    {
        var initializers = VisitExpressionList(node.Initializers);
        if (initializers == node.Initializers)
        {
            return node;
        }

        return new BoundLocalDeclarationStatement(
            node.Syntax,
            node.Binder,
            node.BindingResult,
            node.Locals,
            initializers,
            node.Diagnostics);
    }

    public override BoundNode? VisitDeclaration(BoundDeclaration node)
    {
        var children = VisitList(node.Children);
        if (children == node.Children)
        {
            return node;
        }

        return new BoundDeclaration(
            node.Syntax,
            node.Binder,
            node.SymbolInfo,
            node.Operation,
            node.Diagnostics,
            children);
    }

    public override BoundNode? VisitExpression(BoundExpression node)
    {
        var children = VisitList(node.Children);
        if (children == node.Children)
        {
            return node;
        }

        return new BoundExpression(
            node.Syntax,
            node.Binder,
            node.SymbolInfo,
            node.Operation,
            node.Diagnostics,
            children);
    }

    public override BoundNode? VisitCSharpExpression(BoundCSharpExpression node)
    {
        return VisitExpression(node);
    }

    public override BoundNode? VisitLiteralExpression(BoundLiteralExpression node)
    {
        return VisitExpression(node);
    }

    public override BoundNode? VisitBinaryExpression(BoundBinaryExpression node)
    {
        var left = (BoundExpression?)Visit(node.Left);
        var right = (BoundExpression?)Visit(node.Right);
        if (ReferenceEquals(left, node.Left) &&
            ReferenceEquals(right, node.Right))
        {
            return node;
        }

        if (left == null || right == null)
        {
            return new BoundErrorExpression(
                node.Syntax,
                node.Binder,
                node.Diagnostics);
        }

        return new BoundBinaryExpression(
            node.Syntax,
            node.Binder,
            node.BindingResult,
            node.OperatorKind,
            left,
            right,
            node.Diagnostics);
    }

    public override BoundNode? VisitCallExpression(BoundCallExpression node)
    {
        var receiver = (BoundExpression?)Visit(node.Receiver);
        var arguments = VisitExpressionList(node.Arguments);
        if (ReferenceEquals(receiver, node.Receiver) &&
            arguments == node.Arguments)
        {
            return node;
        }

        return new BoundCallExpression(
            node.Syntax,
            node.Binder,
            node.BindingResult,
            node.TargetMethod,
            receiver,
            arguments,
            node.Diagnostics);
    }

    public override BoundNode? VisitConversionExpression(BoundConversionExpression node)
    {
        var operand = (BoundExpression?)Visit(node.Operand);
        if (ReferenceEquals(operand, node.Operand))
        {
            return node;
        }

        if (operand == null)
        {
            return new BoundErrorExpression(
                node.Syntax,
                node.Binder,
                node.Diagnostics);
        }

        return new BoundConversionExpression(
            node.Syntax,
            node.Binder,
            operand,
            node.Conversion,
            node.Diagnostics);
    }

    public override BoundNode? VisitErrorExpression(BoundErrorExpression node)
    {
        var children = VisitList(node.Children);
        if (children == node.Children)
        {
            return node;
        }

        return new BoundErrorExpression(
            node.Syntax,
            node.Binder,
            node.Diagnostics,
            children);
    }

    protected virtual ImmutableArray<BoundNode> VisitList(ImmutableArray<BoundNode> nodes)
    {
        if (nodes.IsDefaultOrEmpty)
        {
            return nodes.IsDefault ? ImmutableArray<BoundNode>.Empty : nodes;
        }

        ArrayBuilder<BoundNode>? builder = null;

        for (var index = 0; index < nodes.Length; index++)
        {
            var oldNode = nodes[index];
            var newNode = Visit(oldNode);

            if (builder == null)
            {
                if (ReferenceEquals(newNode, oldNode))
                {
                    continue;
                }

                builder = ArrayBuilder<BoundNode>.GetInstance(nodes.Length);
                for (var previous = 0; previous < index; previous++)
                {
                    builder.Add(nodes[previous]);
                }
            }

            if (newNode != null)
            {
                builder.Add(newNode);
            }
        }

        return builder == null
            ? nodes
            : builder.ToImmutableAndFree();
    }

    protected virtual ImmutableArray<BoundExpression> VisitExpressionList(ImmutableArray<BoundExpression> nodes)
    {
        if (nodes.IsDefaultOrEmpty)
        {
            return nodes.IsDefault ? ImmutableArray<BoundExpression>.Empty : nodes;
        }

        ArrayBuilder<BoundExpression>? builder = null;

        for (var index = 0; index < nodes.Length; index++)
        {
            var oldNode = nodes[index];
            var newNode = (BoundExpression?)Visit(oldNode);

            if (builder == null)
            {
                if (ReferenceEquals(newNode, oldNode))
                {
                    continue;
                }

                builder = ArrayBuilder<BoundExpression>.GetInstance(nodes.Length);
                for (var previous = 0; previous < index; previous++)
                {
                    builder.Add(nodes[previous]);
                }
            }

            if (newNode != null)
            {
                builder.Add(newNode);
            }
        }

        return builder == null
            ? nodes
            : builder.ToImmutableAndFree();
    }
}

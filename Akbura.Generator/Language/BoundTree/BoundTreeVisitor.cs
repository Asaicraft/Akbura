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

    public virtual void VisitCSharpStatement(BoundCSharpStatement node) => VisitStatement(node);

    public virtual void VisitBadStatement(BoundBadStatement node) => VisitStatement(node);

    public virtual void VisitLocalDeclarationStatement(BoundLocalDeclarationStatement node) => VisitStatement(node);

    public virtual void VisitDeclaration(BoundDeclaration node) => DefaultVisit(node);

    public virtual void VisitComponentDeclaration(BoundComponentDeclaration node) => VisitDeclaration(node);

    public virtual void VisitStateDeclaration(BoundStateDeclaration node) => VisitDeclaration(node);

    public virtual void VisitParamDeclaration(BoundParamDeclaration node) => VisitDeclaration(node);

    public virtual void VisitInjectDeclaration(BoundInjectDeclaration node) => VisitDeclaration(node);

    public virtual void VisitCommandDeclaration(BoundCommandDeclaration node) => VisitDeclaration(node);

    public virtual void VisitUseEffectDeclaration(BoundUseEffectDeclaration node) => VisitDeclaration(node);

    public virtual void VisitStateInitializer(BoundStateInitializer node) => DefaultVisit(node);

    public virtual void VisitParamDefaultValue(BoundParamDefaultValue node) => DefaultVisit(node);

    public virtual void VisitUseEffectDependency(BoundUseEffectDependency node) => DefaultVisit(node);

    public virtual void VisitUseEffectBody(BoundUseEffectBody node) => DefaultVisit(node);

    public virtual void VisitMarkupRoot(BoundMarkupRoot node) => DefaultVisit(node);

    public virtual void VisitMarkupComponent(BoundMarkupComponent node) => DefaultVisit(node);

    public virtual void VisitMarkupContent(BoundMarkupContent node) => DefaultVisit(node);

    public virtual void VisitMarkupContentSetter(BoundMarkupContentSetter node) => DefaultVisit(node);

    public virtual void VisitAkcssModule(BoundAkcssModule node) => DefaultVisit(node);

    public virtual void VisitAkcssStyle(BoundAkcssStyle node) => DefaultVisit(node);

    public virtual void VisitAkcssUtility(BoundAkcssUtility node) => DefaultVisit(node);

    public virtual void VisitMarkupPropertySetter(BoundMarkupPropertySetter node) => DefaultVisit(node);

    public virtual void VisitMarkupCommandBinding(BoundMarkupCommandBinding node) => DefaultVisit(node);

    public virtual void VisitMarkupRoutedEventBinding(BoundMarkupRoutedEventBinding node) => DefaultVisit(node);

    public virtual void VisitTailwindUtilityAttribute(BoundTailwindUtilityAttribute node) => DefaultVisit(node);

    public virtual void VisitAkcssPropertySetter(BoundAkcssPropertySetter node) => DefaultVisit(node);

    public virtual void VisitAkcssIf(BoundAkcssIf node) => DefaultVisit(node);

    public virtual void VisitAkcssApply(BoundAkcssApply node) => DefaultVisit(node);

    public virtual void VisitAkcssIntercept(BoundAkcssIntercept node) => DefaultVisit(node);

    public virtual void VisitExpression(BoundExpression node) => DefaultVisit(node);

    public virtual void VisitCSharpExpression(BoundCSharpExpression node) => VisitExpression(node);

    public virtual void VisitConversionExpression(BoundConversionExpression node) => VisitExpression(node);

    public virtual void VisitLiteralExpression(BoundLiteralExpression node) => VisitExpression(node);

    public virtual void VisitBinaryExpression(BoundBinaryExpression node) => VisitExpression(node);

    public virtual void VisitCallExpression(BoundCallExpression node) => VisitExpression(node);

    public virtual void VisitBadExpression(BoundBadExpression node) => VisitExpression(node);

    public virtual void VisitErrorExpression(BoundErrorExpression node) => VisitBadExpression(node);
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

    public virtual TResult? VisitCSharpStatement(BoundCSharpStatement node) => VisitStatement(node);

    public virtual TResult? VisitBadStatement(BoundBadStatement node) => VisitStatement(node);

    public virtual TResult? VisitLocalDeclarationStatement(BoundLocalDeclarationStatement node) =>
        VisitStatement(node);

    public virtual TResult? VisitDeclaration(BoundDeclaration node) => DefaultVisit(node);

    public virtual TResult? VisitComponentDeclaration(BoundComponentDeclaration node) => VisitDeclaration(node);

    public virtual TResult? VisitStateDeclaration(BoundStateDeclaration node) => VisitDeclaration(node);

    public virtual TResult? VisitParamDeclaration(BoundParamDeclaration node) => VisitDeclaration(node);

    public virtual TResult? VisitInjectDeclaration(BoundInjectDeclaration node) => VisitDeclaration(node);

    public virtual TResult? VisitCommandDeclaration(BoundCommandDeclaration node) => VisitDeclaration(node);

    public virtual TResult? VisitUseEffectDeclaration(BoundUseEffectDeclaration node) => VisitDeclaration(node);

    public virtual TResult? VisitStateInitializer(BoundStateInitializer node) => DefaultVisit(node);

    public virtual TResult? VisitParamDefaultValue(BoundParamDefaultValue node) => DefaultVisit(node);

    public virtual TResult? VisitUseEffectDependency(BoundUseEffectDependency node) => DefaultVisit(node);

    public virtual TResult? VisitUseEffectBody(BoundUseEffectBody node) => DefaultVisit(node);

    public virtual TResult? VisitMarkupRoot(BoundMarkupRoot node) => DefaultVisit(node);

    public virtual TResult? VisitMarkupComponent(BoundMarkupComponent node) => DefaultVisit(node);

    public virtual TResult? VisitMarkupContent(BoundMarkupContent node) => DefaultVisit(node);

    public virtual TResult? VisitMarkupContentSetter(BoundMarkupContentSetter node) => DefaultVisit(node);

    public virtual TResult? VisitAkcssModule(BoundAkcssModule node) => DefaultVisit(node);

    public virtual TResult? VisitAkcssStyle(BoundAkcssStyle node) => DefaultVisit(node);

    public virtual TResult? VisitAkcssUtility(BoundAkcssUtility node) => DefaultVisit(node);

    public virtual TResult? VisitMarkupPropertySetter(BoundMarkupPropertySetter node) => DefaultVisit(node);

    public virtual TResult? VisitMarkupCommandBinding(BoundMarkupCommandBinding node) => DefaultVisit(node);

    public virtual TResult? VisitMarkupRoutedEventBinding(BoundMarkupRoutedEventBinding node) => DefaultVisit(node);

    public virtual TResult? VisitTailwindUtilityAttribute(BoundTailwindUtilityAttribute node) => DefaultVisit(node);

    public virtual TResult? VisitAkcssPropertySetter(BoundAkcssPropertySetter node) => DefaultVisit(node);

    public virtual TResult? VisitAkcssIf(BoundAkcssIf node) => DefaultVisit(node);

    public virtual TResult? VisitAkcssApply(BoundAkcssApply node) => DefaultVisit(node);

    public virtual TResult? VisitAkcssIntercept(BoundAkcssIntercept node) => DefaultVisit(node);

    public virtual TResult? VisitExpression(BoundExpression node) => DefaultVisit(node);

    public virtual TResult? VisitCSharpExpression(BoundCSharpExpression node) => VisitExpression(node);

    public virtual TResult? VisitConversionExpression(BoundConversionExpression node) => VisitExpression(node);

    public virtual TResult? VisitLiteralExpression(BoundLiteralExpression node) => VisitExpression(node);

    public virtual TResult? VisitBinaryExpression(BoundBinaryExpression node) => VisitExpression(node);

    public virtual TResult? VisitCallExpression(BoundCallExpression node) => VisitExpression(node);

    public virtual TResult? VisitBadExpression(BoundBadExpression node) => VisitExpression(node);

    public virtual TResult? VisitErrorExpression(BoundErrorExpression node) => VisitBadExpression(node);
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

    public virtual TResult? VisitCSharpStatement(BoundCSharpStatement node, TParameter parameter) =>
        VisitStatement(node, parameter);

    public virtual TResult? VisitBadStatement(BoundBadStatement node, TParameter parameter) =>
        VisitStatement(node, parameter);

    public virtual TResult? VisitLocalDeclarationStatement(
        BoundLocalDeclarationStatement node,
        TParameter parameter) =>
        VisitStatement(node, parameter);

    public virtual TResult? VisitDeclaration(BoundDeclaration node, TParameter parameter) =>
        DefaultVisit(node, parameter);

    public virtual TResult? VisitComponentDeclaration(BoundComponentDeclaration node, TParameter parameter) =>
        VisitDeclaration(node, parameter);

    public virtual TResult? VisitStateDeclaration(BoundStateDeclaration node, TParameter parameter) =>
        VisitDeclaration(node, parameter);

    public virtual TResult? VisitParamDeclaration(BoundParamDeclaration node, TParameter parameter) =>
        VisitDeclaration(node, parameter);

    public virtual TResult? VisitInjectDeclaration(BoundInjectDeclaration node, TParameter parameter) =>
        VisitDeclaration(node, parameter);

    public virtual TResult? VisitCommandDeclaration(BoundCommandDeclaration node, TParameter parameter) =>
        VisitDeclaration(node, parameter);

    public virtual TResult? VisitUseEffectDeclaration(BoundUseEffectDeclaration node, TParameter parameter) =>
        VisitDeclaration(node, parameter);

    public virtual TResult? VisitStateInitializer(BoundStateInitializer node, TParameter parameter) =>
        DefaultVisit(node, parameter);

    public virtual TResult? VisitParamDefaultValue(BoundParamDefaultValue node, TParameter parameter) =>
        DefaultVisit(node, parameter);

    public virtual TResult? VisitUseEffectDependency(BoundUseEffectDependency node, TParameter parameter) =>
        DefaultVisit(node, parameter);

    public virtual TResult? VisitUseEffectBody(BoundUseEffectBody node, TParameter parameter) =>
        DefaultVisit(node, parameter);

    public virtual TResult? VisitMarkupRoot(BoundMarkupRoot node, TParameter parameter) =>
        DefaultVisit(node, parameter);

    public virtual TResult? VisitMarkupComponent(BoundMarkupComponent node, TParameter parameter) =>
        DefaultVisit(node, parameter);

    public virtual TResult? VisitMarkupContent(BoundMarkupContent node, TParameter parameter) =>
        DefaultVisit(node, parameter);

    public virtual TResult? VisitMarkupContentSetter(BoundMarkupContentSetter node, TParameter parameter) =>
        DefaultVisit(node, parameter);

    public virtual TResult? VisitAkcssModule(BoundAkcssModule node, TParameter parameter) =>
        DefaultVisit(node, parameter);

    public virtual TResult? VisitAkcssStyle(BoundAkcssStyle node, TParameter parameter) =>
        DefaultVisit(node, parameter);

    public virtual TResult? VisitAkcssUtility(BoundAkcssUtility node, TParameter parameter) =>
        DefaultVisit(node, parameter);

    public virtual TResult? VisitMarkupPropertySetter(BoundMarkupPropertySetter node, TParameter parameter) =>
        DefaultVisit(node, parameter);

    public virtual TResult? VisitMarkupCommandBinding(BoundMarkupCommandBinding node, TParameter parameter) =>
        DefaultVisit(node, parameter);

    public virtual TResult? VisitMarkupRoutedEventBinding(BoundMarkupRoutedEventBinding node, TParameter parameter) =>
        DefaultVisit(node, parameter);

    public virtual TResult? VisitTailwindUtilityAttribute(BoundTailwindUtilityAttribute node, TParameter parameter) =>
        DefaultVisit(node, parameter);

    public virtual TResult? VisitAkcssPropertySetter(BoundAkcssPropertySetter node, TParameter parameter) =>
        DefaultVisit(node, parameter);

    public virtual TResult? VisitAkcssIf(BoundAkcssIf node, TParameter parameter) =>
        DefaultVisit(node, parameter);

    public virtual TResult? VisitAkcssApply(BoundAkcssApply node, TParameter parameter) =>
        DefaultVisit(node, parameter);

    public virtual TResult? VisitAkcssIntercept(BoundAkcssIntercept node, TParameter parameter) =>
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

    public virtual TResult? VisitBadExpression(BoundBadExpression node, TParameter parameter) =>
        VisitExpression(node, parameter);

    public virtual TResult? VisitErrorExpression(BoundErrorExpression node, TParameter parameter) =>
        VisitBadExpression(node, parameter);
}

using Akbura.Language.BoundTree;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using AkburaOperation = Akbura.Language.Operations.IOperation;
using AkburaSyntaxKind = Akbura.Language.Syntax.SyntaxKind;

namespace Akbura.Language;

internal sealed class AkcssOperationMaterializer
{
    private readonly AkburaSemanticModel _semanticModel;
    private readonly AkburaOperationFactory _operationFactory;
    private readonly AkcssBoundNodeFactory _boundNodes;

    public AkcssOperationMaterializer(
        AkburaSemanticModel semanticModel,
        AkburaOperationFactory operationFactory,
        AkcssBoundNodeFactory boundNodes)
    {
        _semanticModel = semanticModel ?? throw new System.ArgumentNullException(nameof(semanticModel));
        _operationFactory = operationFactory ?? throw new System.ArgumentNullException(nameof(operationFactory));
        _boundNodes = boundNodes ?? throw new System.ArgumentNullException(nameof(boundNodes));
    }

    public ImmutableArray<IAkcssOperation> CreateOperations(
        SyntaxList<AkcssBodyMemberSyntax> members,
        IAkcssSymbol containingSymbol)
    {
        using var interceptOperationsBuilder = ImmutableArrayBuilder<IAkcssOperation>.Rent();
        foreach (var member in members)
        {
            if (member.Kind != AkburaSyntaxKind.AkcssInterceptDirectiveSyntax)
            {
                continue;
            }

            var interceptDirective = Unsafe.As<AkcssInterceptDirectiveSyntax>(member);
            if (!_semanticModel.TryGetCachedOperation(interceptDirective, out var cachedInterceptOperation) ||
                cachedInterceptOperation is not IAkcssInterceptOperation interceptOperation)
            {
                var interceptBoundNode = _boundNodes.CreateIntercept(interceptDirective, containingSymbol);
                _semanticModel.SetCachedBoundNode(interceptDirective, interceptBoundNode);
                interceptOperation = (IAkcssInterceptOperation)_operationFactory.CreateOperation(interceptBoundNode)!;
                _semanticModel.SetCachedOperation(interceptDirective, interceptOperation);
            }

            interceptOperationsBuilder.Add(interceptOperation);
            if (!interceptOperation.InterceptType.IsDefault)
            {
                SetAkcssInterceptType(containingSymbol, interceptOperation.InterceptType);
            }
        }

        var interceptOperations = interceptOperationsBuilder.ToImmutable();
        if (containingSymbol.IsIntercepted)
        {
            foreach (var member in members)
            {
                if (member.Kind != AkburaSyntaxKind.AkcssInterceptDirectiveSyntax)
                {
                    _semanticModel.SetAkcssInterceptIgnoredDiagnostics(member, containingSymbol);
                }
            }

            return interceptOperations;
        }

        using var builder = ImmutableArrayBuilder<IAkcssOperation>.Rent();
        foreach (var member in members)
        {
            switch (member.Kind)
            {
                case AkburaSyntaxKind.AkcssAssignmentSyntax:
                {
                    builder.Add(GetOrCreateAkcssOperation(
                        Unsafe.As<AkcssAssignmentSyntax>(member),
                        containingSymbol,
                        static (boundNodes, syntax, symbol) =>
                            boundNodes.CreatePropertySetter(syntax, symbol)));
                    break;
                }

                case AkburaSyntaxKind.AkcssIfDirectiveSyntax:
                {
                    builder.Add(GetOrCreateAkcssOperation(
                        Unsafe.As<AkcssIfDirectiveSyntax>(member),
                        containingSymbol,
                        static (boundNodes, syntax, symbol) =>
                            boundNodes.CreateIf(syntax, symbol)));
                    break;
                }

                case AkburaSyntaxKind.AkcssApplyDirectiveSyntax:
                {
                    builder.Add(GetOrCreateAkcssOperation(
                        Unsafe.As<AkcssApplyDirectiveSyntax>(member),
                        containingSymbol,
                        static (boundNodes, syntax, symbol) =>
                            boundNodes.CreateApply(syntax, symbol)));
                    break;
                }

                case AkburaSyntaxKind.AkcssInterceptDirectiveSyntax:
                {
                    builder.Add(GetOrCreateAkcssOperation(
                        Unsafe.As<AkcssInterceptDirectiveSyntax>(member),
                        containingSymbol,
                        static (boundNodes, syntax, symbol) =>
                            boundNodes.CreateIntercept(syntax, symbol)));
                    break;
                }
            }
        }

        return builder.ToImmutable();
    }

    private IAkcssOperation GetOrCreateAkcssOperation<TSyntax>(
        TSyntax syntax,
        IAkcssSymbol containingSymbol,
        Func<AkcssBoundNodeFactory, TSyntax, IAkcssSymbol, BoundAkcssOperation> bind)
        where TSyntax : AkcssBodyMemberSyntax
    {
        if (_semanticModel.TryGetCachedOperation(syntax, out var cachedOperation) &&
            cachedOperation is IAkcssOperation operation)
        {
            return operation;
        }

        var boundNode = bind(_boundNodes, syntax, containingSymbol);
        _semanticModel.SetCachedBoundNode(syntax, boundNode);
        operation = (IAkcssOperation)_operationFactory.CreateOperation(boundNode)!;
        _semanticModel.SetCachedOperation(syntax, operation);
        return operation;
    }

    private static void SetAkcssInterceptType(
        IAkcssSymbol containingSymbol,
        CSharpSymbolDefinition interceptType)
    {
        switch (containingSymbol)
        {
            case AkcssStyleSymbol styleSymbol:
                styleSymbol.SetInterceptType(interceptType);
                break;
            case TailwindUtilitySymbol utilitySymbol:
                utilitySymbol.SetInterceptType(interceptType);
                break;
        }
    }
}

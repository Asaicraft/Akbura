using Akbura.Language.Binder;
using Akbura.Language.BoundTree;
using Akbura.Language.Symbols;
using Akbura.Pools;
using System;
using System.Collections.Immutable;
using RoslynSymbol = Microsoft.CodeAnalysis.ISymbol;

namespace Akbura.Language.Operations;

internal sealed class AkburaOperationFactory
{
    private readonly AkburaSemanticModel _semanticModel;

    public AkburaOperationFactory(AkburaSemanticModel semanticModel)
    {
        _semanticModel = semanticModel ?? throw new ArgumentNullException(nameof(semanticModel));
    }

    public IOperation? CreateOperation(BoundNode boundNode)
    {
        if (boundNode == null)
        {
            throw new ArgumentNullException(nameof(boundNode));
        }

        return boundNode.Kind switch
        {
            BoundKind.MarkupPropertySetter => CreateMarkupPropertySetterOperation((BoundMarkupPropertySetter)boundNode),
            BoundKind.MarkupCommandBinding => CreateMarkupCommandBindingOperation((BoundMarkupCommandBinding)boundNode),
            BoundKind.MarkupRoutedEventBinding => CreateMarkupRoutedEventBindingOperation((BoundMarkupRoutedEventBinding)boundNode),
            BoundKind.TailwindUtilityAttribute => CreateTailwindUtilityAttributeOperation((BoundTailwindUtilityAttribute)boundNode),
            BoundKind.AkcssPropertySetter => CreateAkcssPropertySetterOperation((BoundAkcssPropertySetter)boundNode),
            BoundKind.AkcssIf => CreateAkcssIfOperation((BoundAkcssIf)boundNode),
            BoundKind.AkcssApply => CreateAkcssApplyOperation((BoundAkcssApply)boundNode),
            BoundKind.AkcssIntercept => CreateAkcssInterceptOperation((BoundAkcssIntercept)boundNode),
            _ => null,
        };
    }

    private MarkupPropertySetterOperation CreateMarkupPropertySetterOperation(
        BoundMarkupPropertySetter boundNode)
    {
        return new MarkupPropertySetterOperation(
            boundNode.Syntax,
            boundNode.ContainingComponent,
            boundNode.Property,
            boundNode.ValueType,
            boundNode.ValueOperation,
            boundNode.BindingKind,
            boundNode.ValueKind,
            boundNode.ValueSyntax,
            boundNode.LiteralValue,
            boundNode.HasErrors,
            CreateCSharpOperationTree(
                boundNode.Syntax,
                boundNode.ValueOperation,
                _semanticModel.CreateCSharpOperationSymbolMapper()));
    }

    private MarkupCommandBindingOperation CreateMarkupCommandBindingOperation(
        BoundMarkupCommandBinding boundNode)
    {
        return new MarkupCommandBindingOperation(
            boundNode.Syntax,
            boundNode.ContainingComponent,
            boundNode.Property,
            boundNode.Command,
            boundNode.BindingKind,
            boundNode.ValueKind,
            boundNode.ValueSyntax,
            boundNode.HandlerKind,
            boundNode.ArgumentMode,
            boundNode.ResultMode,
            boundNode.HandlerParameterCount,
            boundNode.IsAsync,
            boundNode.ContainsAwait,
            boundNode.HandlerType,
            boundNode.HandlerResultType,
            boundNode.HandlerOperation,
            boundNode.HasErrors,
            CreateCSharpOperationTree(
                boundNode.Syntax,
                boundNode.HandlerOperation,
                _semanticModel.CreateCSharpOperationSymbolMapper()));
    }

    private MarkupRoutedEventBindingOperation CreateMarkupRoutedEventBindingOperation(
        BoundMarkupRoutedEventBinding boundNode)
    {
        return new MarkupRoutedEventBindingOperation(
            boundNode.Syntax,
            boundNode.ContainingComponent,
            boundNode.RoutedEvent,
            boundNode.BindingKind,
            boundNode.ValueKind,
            boundNode.ValueSyntax,
            boundNode.HandlerKind,
            boundNode.ArgumentMode,
            boundNode.HandlerParameterCount,
            boundNode.IsAsync,
            boundNode.ContainsAwait,
            boundNode.HandlerOperation,
            boundNode.HasErrors,
            CreateCSharpOperationTree(
                boundNode.Syntax,
                boundNode.HandlerOperation,
                _semanticModel.CreateCSharpOperationSymbolMapper()));
    }

    private TailwindUtilityAttributeOperation CreateTailwindUtilityAttributeOperation(
        BoundTailwindUtilityAttribute boundNode)
    {
        var mapper = _semanticModel.CreateCSharpOperationSymbolMapper();
        return new TailwindUtilityAttributeOperation(
            boundNode.Syntax,
            boundNode.ContainingComponent,
            boundNode.UtilityName,
            boundNode.Utility,
            CreateTailwindArguments(boundNode.Arguments, mapper),
            boundNode.HasCondition,
            boundNode.ConditionText,
            boundNode.ConditionType,
            boundNode.ConditionOperation,
            boundNode.HasErrors,
            CreateCSharpOperationTree(
                boundNode.Syntax,
                boundNode.ConditionOperation,
                mapper));
    }

    private AkcssPropertySetterOperation CreateAkcssPropertySetterOperation(
        BoundAkcssPropertySetter boundNode)
    {
        return new AkcssPropertySetterOperation(
            boundNode.Syntax,
            boundNode.ContainingAkcssSymbol,
            boundNode.Property,
            boundNode.ValueType,
            boundNode.ValueOperation,
            boundNode.ValueKind,
            boundNode.RequiresBrushConversion,
            boundNode.ConvertedValue,
            boundNode.HasErrors,
            CreateCSharpOperationTree(
                boundNode.Syntax,
                boundNode.ValueOperation,
                _semanticModel.CreateCSharpOperationSymbolMapper(boundNode.ContainingAkcssSymbol)));
    }

    private AkcssIfOperation CreateAkcssIfOperation(BoundAkcssIf boundNode)
    {
        using var builder = ImmutableArrayBuilder<IAkcssOperation>.Rent(boundNode.Operations.Length);
        foreach (var operationNode in boundNode.Operations)
        {
            if (CreateOperation(operationNode) is IAkcssOperation operation)
            {
                builder.Add(operation);
            }
        }

        return new AkcssIfOperation(
            boundNode.Syntax,
            boundNode.ContainingAkcssSymbol,
            boundNode.ConditionType,
            boundNode.ConditionOperation,
            builder.ToImmutable(),
            boundNode.HasErrors,
            CreateCSharpOperationTree(
                boundNode.Syntax,
                boundNode.ConditionOperation,
                _semanticModel.CreateCSharpOperationSymbolMapper(boundNode.ContainingAkcssSymbol)));
    }

    private static AkcssApplyOperation CreateAkcssApplyOperation(BoundAkcssApply boundNode)
    {
        return new AkcssApplyOperation(
            boundNode.Syntax,
            boundNode.ContainingAkcssSymbol,
            boundNode.Items,
            boundNode.AppliedSymbols,
            boundNode.HasErrors);
    }

    private static AkcssInterceptOperation CreateAkcssInterceptOperation(BoundAkcssIntercept boundNode)
    {
        return new AkcssInterceptOperation(
            boundNode.Syntax,
            boundNode.ContainingAkcssSymbol,
            boundNode.InterceptType,
            boundNode.HasErrors);
    }

    private static ImmutableArray<TailwindUtilityArgument> CreateTailwindArguments(
        ImmutableArray<BoundTailwindUtilityArgument> arguments,
        Func<RoslynSymbol, ISymbol?> mapper)
    {
        if (arguments.IsDefaultOrEmpty)
        {
            return ImmutableArray<TailwindUtilityArgument>.Empty;
        }

        var builder = ArrayBuilder<TailwindUtilityArgument>.GetInstance(arguments.Length);
        foreach (var argument in arguments)
        {
            builder.Add(new TailwindUtilityArgument(
                argument.Syntax,
                argument.Text,
                argument.Type,
                argument.ValueOperation,
                argument.ConstantValue,
                CreateCSharpOperationTree(
                    argument.Syntax,
                    argument.ValueOperation,
                    mapper)));
        }

        return builder.ToImmutableAndFree();
    }

    private static ICSharpOperation? CreateCSharpOperationTree(
        Akbura.Language.Syntax.AkburaSyntax syntax,
        CSharpOperationDefinition operationDefinition,
        Func<RoslynSymbol, ISymbol?>? mapper)
    {
        return CSharpOperationTreeBuilder.Create(
            syntax,
            operationDefinition.Operation,
            parent: null,
            mapper);
    }
}

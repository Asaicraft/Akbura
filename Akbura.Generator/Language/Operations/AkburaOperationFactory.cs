using Akbura.Language.Binder;
using Akbura.Language.BoundTree;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using System;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using AkburaSyntaxKind = Akbura.Language.Syntax.SyntaxKind;
using RoslynSymbol = Microsoft.CodeAnalysis.ISymbol;

namespace Akbura.Language.Operations;

internal sealed class AkburaOperationFactory : IOperationFactory
{
    private readonly Func<AkburaSyntax, IAkcssSymbol?, Func<RoslynSymbol, ISymbol?>> _createCSharpOperationSymbolMapper;

    public AkburaOperationFactory(
        Func<AkburaSyntax, IAkcssSymbol?, Func<RoslynSymbol, ISymbol?>> createCSharpOperationSymbolMapper)
    {
        _createCSharpOperationSymbolMapper = createCSharpOperationSymbolMapper ??
            throw new ArgumentNullException(nameof(createCSharpOperationSymbolMapper));
    }

    public IOperation? CreateOperation(BoundNode boundNode)
    {
        if (boundNode == null)
        {
            throw new ArgumentNullException(nameof(boundNode));
        }

        return boundNode.Kind switch
        {
            BoundKind.MarkupComponent => CreateMarkupContentOperation((BoundMarkupComponent)boundNode),
            BoundKind.MarkupContentSetter => CreateMarkupContentOperation((BoundMarkupContentSetter)boundNode),
            BoundKind.MarkupNameAssignment => CreateMarkupNameAssignmentOperation((BoundMarkupNameAssignment)boundNode),
            BoundKind.MarkupPropertySetter => CreateMarkupPropertySetterOperation((BoundMarkupPropertySetter)boundNode),
            BoundKind.MarkupCommandBinding => CreateMarkupCommandBindingOperation((BoundMarkupCommandBinding)boundNode),
            BoundKind.MarkupRoutedEventBinding => CreateMarkupRoutedEventBindingOperation((BoundMarkupRoutedEventBinding)boundNode),
            BoundKind.TailwindUtilityAttribute => CreateTailwindUtilityAttributeOperation((BoundTailwindUtilityAttribute)boundNode),
            BoundKind.AkcssPropertySetter => CreateAkcssPropertySetterOperation((BoundAkcssPropertySetter)boundNode),
            BoundKind.AkcssIf => CreateAkcssIfOperation((BoundAkcssIf)boundNode),
            BoundKind.AkcssApply => CreateAkcssApplyOperation((BoundAkcssApply)boundNode),
            BoundKind.AkcssIntercept => CreateAkcssInterceptOperation((BoundAkcssIntercept)boundNode),
            BoundKind.UseHookInvocation => CreateUseHookOperation((BoundUseHookInvocation)boundNode),
            BoundKind.UseHookStatement => CreateUseHookOperation(((BoundUseHookStatement)boundNode).Invocation),
            BoundKind.StateInitializer => CreateStateInitializerOperation((BoundStateInitializer)boundNode),
            BoundKind.CSharpStatement => CreateCSharpStatementOperation((BoundCSharpStatement)boundNode),
            BoundKind.LocalDeclarationStatement => CreateCSharpStatementOperation((BoundLocalDeclarationStatement)boundNode),
            _ => null,
        };
    }

    public ImmutableArray<IAkcssOperation> CreateAkcssOperations(
        SyntaxList<AkcssBodyMemberSyntax> members,
        IAkcssSymbol containingSymbol,
        IOperationFactoryContext context)
    {
        if (containingSymbol == null)
        {
            throw new ArgumentNullException(nameof(containingSymbol));
        }

        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        using var interceptOperationsBuilder = ImmutableArrayBuilder<IAkcssOperation>.Rent();
        foreach (var member in members)
        {
            if (member.Kind != AkburaSyntaxKind.AkcssInterceptDirectiveSyntax)
            {
                continue;
            }

            var interceptDirective = Unsafe.As<AkcssInterceptDirectiveSyntax>(member);
            if (!context.TryGetCachedOperation(interceptDirective, out var cachedInterceptOperation) ||
                cachedInterceptOperation is not IAkcssInterceptOperation interceptOperation)
            {
                var interceptBoundNode = BindAkcssOperation(context, interceptDirective, containingSymbol);
                context.SetCachedBoundNode(interceptDirective, interceptBoundNode);
                interceptOperation = (IAkcssInterceptOperation)CreateOperation(interceptBoundNode)!;
                context.SetCachedOperation(interceptDirective, interceptOperation);
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
                    context.SetAkcssInterceptIgnoredDiagnostics(member, containingSymbol);
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
                    builder.Add(GetOrCreateAkcssOperation(
                        context,
                        Unsafe.As<AkcssAssignmentSyntax>(member),
                        containingSymbol));
                    break;

                case AkburaSyntaxKind.AkcssIfDirectiveSyntax:
                    builder.Add(GetOrCreateAkcssOperation(
                        context,
                        Unsafe.As<AkcssIfDirectiveSyntax>(member),
                        containingSymbol));
                    break;

                case AkburaSyntaxKind.AkcssApplyDirectiveSyntax:
                    builder.Add(GetOrCreateAkcssOperation(
                        context,
                        Unsafe.As<AkcssApplyDirectiveSyntax>(member),
                        containingSymbol));
                    break;

                case AkburaSyntaxKind.AkcssInterceptDirectiveSyntax:
                    builder.Add(GetOrCreateAkcssOperation(
                        context,
                        Unsafe.As<AkcssInterceptDirectiveSyntax>(member),
                        containingSymbol));
                    break;
            }
        }

        return builder.ToImmutable();
    }

    private IAkcssOperation GetOrCreateAkcssOperation<TSyntax>(
        IOperationFactoryContext context,
        TSyntax syntax,
        IAkcssSymbol containingSymbol)
        where TSyntax : AkcssBodyMemberSyntax
    {
        if (context.TryGetCachedOperation(syntax, out var cachedOperation) &&
            cachedOperation is IAkcssOperation operation)
        {
            return operation;
        }

        var boundNode = BindAkcssOperation(context, syntax, containingSymbol);
        context.SetCachedBoundNode(syntax, boundNode);
        operation = (IAkcssOperation)CreateOperation(boundNode)!;
        context.SetCachedOperation(syntax, operation);
        return operation;
    }

    private static BoundAkcssOperation BindAkcssOperation(
        IOperationFactoryContext context,
        AkcssBodyMemberSyntax syntax,
        IAkcssSymbol containingSymbol)
    {
        if (context.GetBinder(containingSymbol.DeclarationSyntax, BinderUsage.Akcss) is AkcssStyleBinder binder)
        {
            return binder.BindAkcssOperation(syntax, containingSymbol);
        }

        throw new InvalidOperationException(
            $"AKCSS operation binding requires {nameof(AkcssStyleBinder)}.");
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

    private MarkupContentOperation? CreateMarkupContentOperation(
        BoundMarkupComponent boundNode)
    {
        foreach (var child in boundNode.Children)
        {
            if (child.Kind == BoundKind.MarkupContentSetter)
            {
                return CreateMarkupContentOperation((BoundMarkupContentSetter)child);
            }
        }

        return null;
    }

    private MarkupContentOperation CreateMarkupContentOperation(
        BoundMarkupContentSetter boundNode)
    {
        return new MarkupContentOperation(
            boundNode.Syntax,
            boundNode.ContainingComponent,
            boundNode.Property,
            boundNode.ContentModel,
            boundNode.Content,
            boundNode.ValueType,
            boundNode.ValueOperation,
            boundNode.ValueConversion,
            boundNode.LiteralValue,
            boundNode.IsSynthesizedString,
            boundNode.HasErrors,
            CreateCSharpOperationTree(
                boundNode.Syntax,
                boundNode.ValueOperation,
                CreateCSharpOperationSymbolMapper(boundNode.Syntax, containingAkcssSymbol: null)));
    }

    private static MarkupNameAssignmentOperation CreateMarkupNameAssignmentOperation(
        BoundMarkupNameAssignment boundNode)
    {
        return new MarkupNameAssignmentOperation(
            boundNode.Syntax,
            boundNode.ContainingComponent,
            boundNode.NameSymbol,
            boundNode.HasErrors);
    }

    private MarkupPropertySetterOperation CreateMarkupPropertySetterOperation(
        BoundMarkupPropertySetter boundNode)
    {
        return new MarkupPropertySetterOperation(
            boundNode.Syntax,
            boundNode.ContainingComponent,
            boundNode.Property,
            boundNode.AppliedAkcssSymbols,
            boundNode.ValueType,
            boundNode.ValueOperation,
            boundNode.ValueConversion,
            boundNode.BindingKind,
            boundNode.ValueKind,
            boundNode.ValueSyntax,
            boundNode.LiteralValue,
            boundNode.ConvertedValue,
            boundNode.HasErrors,
            CreateCSharpOperationTree(
                boundNode.Syntax,
                boundNode.ValueOperation,
                CreateCSharpOperationSymbolMapper(boundNode.Syntax, containingAkcssSymbol: null)));
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
                CreateCSharpOperationSymbolMapper(boundNode.Syntax, containingAkcssSymbol: null)));
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
                CreateCSharpOperationSymbolMapper(boundNode.Syntax, containingAkcssSymbol: null)));
    }

    private TailwindUtilityAttributeOperation CreateTailwindUtilityAttributeOperation(
        BoundTailwindUtilityAttribute boundNode)
    {
        var mapper = CreateCSharpOperationSymbolMapper(boundNode.Syntax, containingAkcssSymbol: null);
        return new TailwindUtilityAttributeOperation(
            boundNode.Syntax,
            boundNode.ContainingComponent,
            boundNode.UtilityName,
            boundNode.Utility,
            boundNode.Utilities,
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
            boundNode.ValueConversion,
            boundNode.ValueKind,
            boundNode.RequiresBrushConversion,
            boundNode.ConvertedValue,
            boundNode.HasErrors,
            CreateCSharpOperationTree(
                boundNode.Syntax,
                boundNode.ValueOperation,
                CreateCSharpOperationSymbolMapper(boundNode.Syntax, boundNode.ContainingAkcssSymbol)));
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
                CreateCSharpOperationSymbolMapper(boundNode.Syntax, boundNode.ContainingAkcssSymbol)));
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

    private Func<RoslynSymbol, ISymbol?> CreateCSharpOperationSymbolMapper(
        AkburaSyntax syntax,
        IAkcssSymbol? containingAkcssSymbol)
    {
        return _createCSharpOperationSymbolMapper(syntax, containingAkcssSymbol);
    }

    private IUseHookOperation? CreateStateInitializerOperation(BoundStateInitializer boundNode)
    {
        return boundNode.UseHookInvocation == null
            ? null
            : CreateUseHookOperation(boundNode.UseHookInvocation);
    }

    private IUseHookOperation CreateUseHookOperation(BoundUseHookInvocation boundNode)
    {
        var csharpOperation = CreateCSharpOperationTree(
            boundNode.Syntax,
            boundNode.BindingResult.OperationDefinition,
            CreateCSharpOperationSymbolMapper(
                boundNode.Syntax,
                containingAkcssSymbol: null));
        return new UseHookOperation(
            boundNode.Syntax,
            boundNode.Hook,
            boundNode.OriginalInvocation,
            boundNode.EffectiveInvocation,
            boundNode.BindingResult.OperationDefinition,
            boundNode.HasSyntheticSelf,
            boundNode.HasPropertyArgumentSubstitution,
            boundNode.HasErrors,
            csharpOperation);
    }

    private ICSharpOperation? CreateCSharpStatementOperation(BoundCSharpStatement boundNode)
    {
        return CreateCSharpOperationTree(
            boundNode.Syntax,
            boundNode.BindingResult.OperationDefinition,
            CreateCSharpOperationSymbolMapper(boundNode.Syntax, containingAkcssSymbol: null));
    }

    private ICSharpOperation? CreateCSharpStatementOperation(BoundLocalDeclarationStatement boundNode)
    {
        return CreateCSharpOperationTree(
            boundNode.Syntax,
            boundNode.BindingResult.OperationDefinition,
            CreateCSharpOperationSymbolMapper(boundNode.Syntax, containingAkcssSymbol: null));
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

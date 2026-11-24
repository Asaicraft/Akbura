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

    public IOperation? CreateOperation(BoundNode boundNode) => CreateOperation(boundNode, includeLegacyAkcssStructure: false);

    private IOperation? CreateOperation(BoundNode boundNode, bool includeLegacyAkcssStructure)
    {
        if (boundNode == null)
        {
            throw new ArgumentNullException(nameof(boundNode));
        }

        if (boundNode is BoundAkcssContextual contextual)
        {
            return CreateAkcssContextualOperation(contextual);
        }

        return boundNode.Kind switch
        {
            BoundKind.MarkupComponent when AkcssSemanticFacts.FindTemplate(boundNode.Syntax) is not null =>
                CreateAkcssStructure(boundNode, OperationKind.MarkupElement),
            BoundKind.MarkupContent when AkcssSemanticFacts.FindTemplate(boundNode.Syntax) is not null =>
                CreateAkcssStructure(boundNode, OperationKind.MarkupContent),
            BoundKind.MarkupComponent => CreateMarkupContentOperation((BoundMarkupComponent)boundNode),
            BoundKind.MarkupIf => CreateMarkupIfOperation((BoundMarkupIfStatement)boundNode),
            BoundKind.MarkupForeach => CreateMarkupForeachOperation((BoundMarkupForeachStatement)boundNode),
            BoundKind.MarkupForeachKey => CreateMarkupForeachKeyOperation((BoundMarkupForeachKey)boundNode),
            BoundKind.MarkupContentSetter => CreateMarkupContentOperation((BoundMarkupContentSetter)boundNode),
            BoundKind.MarkupNameAssignment => CreateMarkupNameAssignmentOperation((BoundMarkupNameAssignment)boundNode),
            BoundKind.MarkupDictionaryKey => CreateMarkupDictionaryKeyOperation((BoundMarkupDictionaryKey)boundNode),
            BoundKind.MarkupPropertySetter => CreateMarkupPropertySetterOperation((BoundMarkupPropertySetter)boundNode),
            BoundKind.MarkupCommandBinding => CreateMarkupCommandBindingOperation((BoundMarkupCommandBinding)boundNode),
            BoundKind.MarkupRoutedEventBinding => CreateMarkupRoutedEventBindingOperation((BoundMarkupRoutedEventBinding)boundNode),
            BoundKind.MarkupWhitespaceDirective => CreateMarkupWhitespaceDirectiveOperation((BoundMarkupWhitespaceDirective)boundNode),
            BoundKind.TailwindUtilityAttribute => CreateTailwindUtilityAttributeOperation((BoundTailwindUtilityAttribute)boundNode),
            BoundKind.AkcssModule when AkcssSemanticFacts.FindContextualModuleMember(boundNode.Syntax) is not null =>
                CreateAkcssStructure(boundNode, OperationKind.AkcssBlock),
            BoundKind.AkcssStyle when includeLegacyAkcssStructure || AkcssSemanticFacts.HasContextualBody(boundNode.Syntax) =>
                CreateAkcssStructure(boundNode, OperationKind.AkcssStyleRule, ((BoundAkcssStyle)boundNode).Context),
            BoundKind.AkcssUtility when includeLegacyAkcssStructure || AkcssSemanticFacts.HasContextualBody(boundNode.Syntax) =>
                CreateAkcssStructure(boundNode, OperationKind.AkcssUtilityDeclaration, ((BoundAkcssUtility)boundNode).Context),
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
            if (member is not AkcssInterceptDirectiveSyntax interceptDirective)
            {
                continue;
            }

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
                if (member is not AkcssInterceptDirectiveSyntax)
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
        if (containingSymbol.DeclarationSyntax is { } declarationSyntax &&
            context.GetBinder(declarationSyntax, BinderUsage.Akcss) is AkcssStyleBinder binder)
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

    internal static MarkupContentOperation? CreateMarkupContentOperationForTemplate(BoundMarkupComponent component, IOperationFactory factory) =>
        ((AkburaOperationFactory)factory).CreateMarkupContentOperation(component);

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

    private MarkupForeachOperation CreateMarkupForeachOperation(BoundMarkupForeachStatement node)
    {
        using var children = ImmutableArrayBuilder<IOperation>.Rent();
        AddLoopCSharpOperation(node.Syntax.Header, node.Source, children);
        if (node.Syntax.KeyClause is { } key)
        {
            AddLoopCSharpOperation(key.Expression, node.Key, children);
        }
        var structuralChildren = AkcssSemanticFacts.FindTemplate(node.Syntax) is not null
            ? node.Children.Where(static child => child is BoundMarkupContent or BoundMarkupIfStatement or BoundAkcssContextual)
                .ToDictionary(static child => child.Syntax)
            : null;
        var body = CreateMarkupForeachBody(node.Body, children, structuralChildren);
        return new(node.Syntax, node.Source, node.IterationType, node.OutputType, node.Key, node.KeySyntax, body,
            children.ToImmutable(), node.HasErrors);
    }

    private ImmutableArray<MarkupForeachBodyItem> CreateMarkupForeachBody(
        ImmutableArray<BoundMarkupForeachBodyItem> items, ImmutableArrayBuilder<IOperation> children,
        Dictionary<AkburaSyntax, BoundNode>? structuralChildren)
    {
        using var body = ImmutableArrayBuilder<MarkupForeachBodyItem>.Rent(items.Length);
        foreach (var item in items)
        {
            AddLoopCSharpOperation(item.Syntax, item.Code, children);
            var nestedBody = CreateMarkupForeachBody(item.Body, children, structuralChildren);
            var alternative = CreateMarkupForeachBody(item.ElseBody, children, structuralChildren);
            IMarkupForeachOperation? loop = null;
            if (item.ForeachStatement != null)
            {
                loop = CreateMarkupForeachOperation(item.ForeachStatement);
                children.Add(loop);
            }
            if (structuralChildren != null && structuralChildren.TryGetValue(item.Syntax, out var bound) &&
                CreateOperation(bound) is { } operation)
            {
                children.Add(operation);
            }
            else
            {
                foreach (var child in item.Content)
                {
                    if (((IOperation?)child.ConditionalOperation ?? child.ForeachOperation) is { } controlFlow)
                    {
                        children.Add(controlFlow);
                    }
                }
            }
            body.Add(new(item.Syntax, item.Code, item.Content, nestedBody, alternative, loop));
        }
        return body.ToImmutable();
    }

    private void AddLoopCSharpOperation(AkburaSyntax syntax, CSharpOperationDefinition definition,
        ImmutableArrayBuilder<IOperation> children)
    {
        var operation = CreateCSharpOperationTree(syntax, definition,
            CreateCSharpOperationSymbolMapper(syntax, containingAkcssSymbol: null));
        if (operation != null)
        {
            children.Add(operation);
        }
    }

    private MarkupIfOperation CreateMarkupIfOperation(BoundMarkupIfStatement node)
    {
        var inAkcssTemplate = AkcssSemanticFacts.FindTemplate(node.Syntax) is not null;
        using var branches = ImmutableArrayBuilder<MarkupConditionalBranch>.Rent(node.Branches.Length);
        using var children = ImmutableArrayBuilder<IOperation>.Rent();
        foreach (var branch in node.Branches)
        {
            branches.Add(new(branch.Syntax, branch.ConditionSyntax, branch.Condition, branch.Content,
                branch.ValueOperation, branch.LiteralValue, branch.IsSynthesizedString));
            var condition = CreateCSharpOperationTree(branch.ConditionSyntax ?? branch.Syntax,
                branch.Condition, CreateCSharpOperationSymbolMapper(branch.Syntax, containingAkcssSymbol: null));
            if (condition != null)
            {
                children.Add(condition);
            }

            if (inAkcssTemplate)
            {
                foreach (var child in branch.Children)
                {
                    if (CreateOperation(child) is { } branchOperation)
                    {
                        children.Add(branchOperation);
                    }
                }
            }
            else
            {
                foreach (var child in branch.Content)
                {
                    if (child.ConditionalOperation is { } conditional)
                    {
                        children.Add(conditional);
                    }
                }
            }
        }

        var operation = new MarkupIfOperation(node.Syntax, branches.ToImmutable(), children.ToImmutable(), node.HasErrors);
        foreach (var child in operation.Children)
        {
            switch (child)
            {
                case CSharpOperation csharp:
                    csharp.SetParent(operation);
                    break;
                case AkcssContextualOperation contextual:
                    contextual.SetParent(operation);
                    break;
                case MarkupIfOperation conditional when inAkcssTemplate:
                    conditional.SetParent(operation);
                    break;
                case MarkupForeachOperation loop when inAkcssTemplate:
                    loop.SetParent(operation);
                    break;
            }
        }

        return operation;
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

    private MarkupDictionaryKeyOperation CreateMarkupDictionaryKeyOperation(BoundMarkupDictionaryKey node) =>
        new(node.Syntax, node.ContainingComponent, node.DictionaryShape, node.Binding, node.LiteralValue,
            node.HasErrors, CreateCSharpOperationTree(node.Syntax, node.Binding.OperationDefinition,
                CreateCSharpOperationSymbolMapper(node.Syntax, containingAkcssSymbol: null)));

    private MarkupForeachKeyOperation CreateMarkupForeachKeyOperation(BoundMarkupForeachKey node) =>
        new(node.Syntax, node.ContainingComponent, node.Binding, node.IsIterationKey, node.HasErrors,
            CreateCSharpOperationTree(node.Syntax, node.Binding.OperationDefinition,
                CreateCSharpOperationSymbolMapper(node.Syntax, containingAkcssSymbol: null)));

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
            boundNode.AssignsCollectionSource,
            boundNode.HasErrors,
            CreateCSharpOperationTree(
                boundNode.Syntax,
                boundNode.ValueOperation,
                CreateCSharpOperationSymbolMapper(boundNode.Syntax, containingAkcssSymbol: null)),
            boundNode.ExpressionBindingPlan);
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
                CreateCSharpOperationSymbolMapper(boundNode.Syntax, containingAkcssSymbol: null)),
            boundNode.TargetKind,
            boundNode.ParameterTypes,
            boundNode.ReturnType,
            boundNode.ResultType);
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

    private static MarkupWhitespaceDirectiveOperation CreateMarkupWhitespaceDirectiveOperation(BoundMarkupWhitespaceDirective boundNode)
    {
        return new MarkupWhitespaceDirectiveOperation(
            boundNode.Syntax,
            boundNode.ContainingComponent,
            boundNode.SymbolInfo.Symbol as
                Akbura.Language.Symbols.IPropertySymbol,
            boundNode.RawValue,
            boundNode.DeclaredMode,
            boundNode.EffectiveMode,
            boundNode.HasErrors);
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
            boundNode.ConditionMarkupExtension,
            boundNode.Variant,
            boundNode.BindingPriority,
            boundNode.HasErrors,
            CreateCSharpOperationTree(
                boundNode.Syntax,
                boundNode.ConditionOperation,
                mapper),
            boundNode.ConditionExpressionBinding);
    }

    private AkcssContextualOperation CreateAkcssContextualOperation(BoundAkcssContextual node)
    {
        var kind = node.Kind switch
        {
            BoundKind.AkcssSelectorRule => OperationKind.AkcssSelectorRule,
            BoundKind.AkcssNestedRule => OperationKind.AkcssNestedRule,
            BoundKind.AkcssTheme => OperationKind.AkcssTheme,
            BoundKind.AkcssMixin => OperationKind.AkcssMixin,
            BoundKind.AkcssMixinParameter => OperationKind.AkcssMixinParameter,
            BoundKind.AkcssMixinArgument => OperationKind.AkcssMixinArgument,
            BoundKind.AkcssContextualSetter => OperationKind.AkcssAssignment,
            BoundKind.AkcssContextualIf => OperationKind.AkcssIf,
            BoundKind.AkcssContextualApply => OperationKind.AkcssApply,
            BoundKind.AkcssInclude => OperationKind.AkcssInclude,
            BoundKind.AkcssTemplate => OperationKind.AkcssTemplate,
            BoundKind.AkcssTemplateBinding => OperationKind.AkcssTemplateBinding,
            BoundKind.AkcssBasedOn => OperationKind.AkcssBasedOn,
            _ => OperationKind.Error,
        };
        var definition = node.Data switch
        {
            AkcssSetterData setter => setter.Value,
            AkcssConditionData condition => condition.Condition.OperationDefinition,
            AkcssMixinParameterData { Default: { } value } => value.Binding.OperationDefinition,
            AkcssMixinArgumentData value => value.Argument.Binding.OperationDefinition,
            AkcssBaseThemeData baseTheme => baseTheme.ExternalTheme.OperationDefinition,
            _ => default,
        };
        return CreateAkcssStructure(node, kind, node.Context, node.Data, definition);
    }

    private AkcssContextualOperation CreateAkcssStructure(
        BoundNode node, OperationKind kind, AkcssBindingContext? context = null, AkcssBoundData? data = null,
        CSharpOperationDefinition definition = default)
    {
        using var children = ImmutableArrayBuilder<IOperation>.Rent();
        if (context is not null && CreateCSharpOperationTree(node.Syntax, definition, CreateAkcssContextMapper(context)) is { } expression)
        {
            children.Add(expression);
        }

        foreach (var child in node.Children)
        {
            if (CreateOperation(child, includeLegacyAkcssStructure: node is BoundAkcssModule) is { } operation)
            {
                children.Add(operation);
            }
        }

        return new AkcssContextualOperation(kind, node.Syntax, context, data, node.SymbolInfo.Symbol, definition,
            children.ToImmutable(), node.Diagnostics, node.HasErrors);
    }

    private static Func<RoslynSymbol, ISymbol?> CreateAkcssContextMapper(AkcssBindingContext context)
    {
        var fallback = context.DefinitionModel.CreateCSharpOperationSymbolMapper(context.Definition, context.Owner as IAkcssSymbol);
        return symbol =>
        {
            if (context.Owner is AkcssMixinSymbol mixin && symbol is Microsoft.CodeAnalysis.IParameterSymbol
                { ContainingSymbol.Name: "__AkburaSemanticProbe" } parameter)
            {
                foreach (var candidate in mixin.Parameters)
                {
                    if (candidate.Name == parameter.Name)
                    {
                        return candidate;
                    }
                }
            }

            return fallback(symbol);
        };
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
                    mapper),
                argument.MarkupExtension));
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
        var symbolMapper = CreateCSharpOperationSymbolMapper(
            boundNode.Syntax,
            containingAkcssSymbol: null);
        var csharpOperation = CreateCSharpOperationTree(
            boundNode.Syntax,
            boundNode.BindingResult.OperationDefinition,
            symbol =>
            {
                foreach (var stateArgument in boundNode.StateArguments)
                {
                    if (boundNode.EffectiveArguments[stateArgument.ArgumentIndex] is
                            BoundCSharpExpression expression &&
                        Microsoft.CodeAnalysis.SymbolEqualityComparer.Default.Equals(
                            symbol,
                            expression.BindingResult.Symbol))
                    {
                        return stateArgument.State;
                    }
                }

                if (symbol is Microsoft.CodeAnalysis.IParameterSymbol
                    {
                        ContainingSymbol: Microsoft.CodeAnalysis.IMethodSymbol
                        {
                            MethodKind: Microsoft.CodeAnalysis.MethodKind.AnonymousFunction or
                                Microsoft.CodeAnalysis.MethodKind.LocalFunction,
                        },
                    })
                {
                    return null;
                }

                return symbolMapper(symbol);
            });
        return new UseHookOperation(
            boundNode.Syntax,
            boundNode.Hook,
            boundNode.OriginalInvocation,
            boundNode.EffectiveInvocation,
            boundNode.BindingResult.OperationDefinition,
            boundNode.HasSyntheticSelf,
            boundNode.HasPropertyArgumentSubstitution,
            boundNode.HasErrors,
            csharpOperation,
            boundNode.StateArguments);
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

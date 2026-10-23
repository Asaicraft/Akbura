using Akbura.Collections;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using AkburaSymbol = Akbura.Language.Symbols.ISymbol;
using RoslynOperation = Microsoft.CodeAnalysis.IOperation;

namespace Akbura.Language.Binder;

internal sealed class ExpressionBindingDependencyAnalyzer
{
    private readonly MarkupExpressionBindingSyntax _syntax;
    private readonly RoslynOperation _root;
    private readonly Func<ISymbol, AkburaSymbol?> _mapSymbol;
    private readonly List<ExpressionBindingDependency> _dependencies = new();
    private readonly List<AkburaSemanticDiagnostic> _diagnostics = new();
    private bool _unknown;

    public ExpressionBindingDependencyAnalyzer(MarkupExpressionBindingSyntax syntax,
        RoslynOperation root, Func<ISymbol, AkburaSymbol?> mapSymbol)
    {
        _syntax = syntax;
        _root = root;
        _mapSymbol = mapSymbol;
    }

    public ExpressionBindingPlan Analyze(CSharpBindingResult binding, ITypeSymbol? targetType,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics)
    {
        var evaluation = Visit(_root, ImmutableArray<ExpressionBindingCondition>.Empty, null);
        return new ExpressionBindingPlan(binding.OperationDefinition,
            _root.Type is { TypeKind: not TypeKind.Error } resultType ? resultType : null, targetType, binding.Conversion,
            evaluation, _dependencies.ToImmutableArray(), diagnostics.AddRange(_diagnostics), _unknown);
    }

    private ExpressionBindingEvaluation Visit(RoslynOperation operation,
        ImmutableArray<ExpressionBindingCondition> conditions, ExpressionBindingEvaluation? conditionalReceiver)
    {
        var children = ArrayBuilder<ExpressionBindingEvaluation>.GetInstance();
        try
        {
            ExpressionBindingEvaluation Read(RoslynOperation child, ImmutableArray<ExpressionBindingCondition>? path = null)
            {
                var result = Visit(child, path ?? conditions, conditionalReceiver);
                children.Add(result);
                return result;
            }
            ImmutableArray<ExpressionBindingCondition> Branch(RoslynOperation test, ExpressionBindingBranch branch, int arm = -1) =>
                conditions.Add(new ExpressionBindingCondition(test, branch, arm));

            ExpressionBindingDependency? receiver = null;
            ISymbol? member = null;
            AkburaSymbol? source = null;
            ITypeSymbol? receiverType = null;
            var kind = ExpressionBindingDependencyKind.Computed;
            var observation = ExpressionBindingObservation.None;
            var classification = operation.ConstantValue.HasValue
                ? ExpressionBindingValueClassification.Constant : ExpressionBindingValueClassification.Computed;
            var indices = ImmutableArray<ExpressionBindingEvaluation>.Empty;
            ISymbol? notification = null;
            var deduplicate = false;

            switch (operation)
            {
                case IConditionalAccessInstanceOperation:
                    return new ExpressionBindingEvaluation(operation, conditionalReceiver?.Dependency, conditions, children.ToImmutable());
                case IConditionalAccessOperation access:
                    var accessReceiver = Read(access.Operation);
                    children.Add(Visit(access.WhenNotNull,
                        Branch(access.Operation, ExpressionBindingBranch.WhenNotNull), accessReceiver));
                    break;
                case IConditionalOperation conditional:
                    Read(conditional.Condition);
                    Read(conditional.WhenTrue, Branch(conditional.Condition, ExpressionBindingBranch.WhenTrue));
                    if (conditional.WhenFalse != null)
                        Read(conditional.WhenFalse, Branch(conditional.Condition, ExpressionBindingBranch.WhenFalse));
                    break;
                case IBinaryOperation binary when binary.OperatorKind is BinaryOperatorKind.ConditionalAnd or BinaryOperatorKind.ConditionalOr:
                    Read(binary.LeftOperand);
                    Read(binary.RightOperand, Branch(binary.LeftOperand,
                        binary.OperatorKind == BinaryOperatorKind.ConditionalAnd ? ExpressionBindingBranch.WhenTrue : ExpressionBindingBranch.WhenFalse));
                    if (binary.OperatorMethod != null) Unsupported(operation, "user-defined short-circuit operators");
                    break;
                case ICoalesceOperation coalesce:
                    Read(coalesce.Value);
                    Read(coalesce.WhenNull, Branch(coalesce.Value, ExpressionBindingBranch.WhenNull));
                    break;
                case ISwitchExpressionOperation switchExpression:
                    Read(switchExpression.Value);
                    for (var i = 0; i < switchExpression.Arms.Length; i++)
                        Read(switchExpression.Arms[i], Branch(switchExpression.Value, ExpressionBindingBranch.SwitchArm, i));
                    break;
                case ISwitchExpressionArmOperation arm:
                    Read(arm.Pattern);
                    var matched = Branch(arm.Pattern, ExpressionBindingBranch.WhenPatternMatches);
                    if (arm.Guard != null)
                    {
                        Read(arm.Guard, matched);
                        matched = matched.Add(new ExpressionBindingCondition(arm.Guard, ExpressionBindingBranch.WhenGuardTrue));
                    }
                    Read(arm.Value, matched);
                    break;
                case IPropertyReferenceOperation property:
                    receiver = property.Instance == null ? null : Read(property.Instance).Dependency;
                    receiverType = property.Instance?.Type;
                    member = property.Property;
                    kind = property.Property.IsIndexer ? ExpressionBindingDependencyKind.Indexer : ExpressionBindingDependencyKind.Property;
                    indices = property.Arguments.SelectAsArray(argument => Read(argument));
                    observation = PropertyObservation(property.Property, receiverType, out notification);
                    if (property.Property.IsIndexer && Implements(receiverType, "System.Collections.Specialized.INotifyCollectionChanged"))
                        observation |= ExpressionBindingObservation.CollectionChanged;
                    if (observation != ExpressionBindingObservation.None && receiverType?.IsValueType == true)
                        Unsupported(operation, "observable value-type receivers would require copying or boxing the receiver");
                    classification = observation != ExpressionBindingObservation.None ? ExpressionBindingValueClassification.Observable :
                        receiverType?.SpecialType == SpecialType.System_String ? ExpressionBindingValueClassification.Stable : ExpressionBindingValueClassification.Unobservable;
                    deduplicate = !property.Property.IsIndexer;
                    break;
                case IFieldReferenceOperation field:
                    receiver = field.Instance == null ? null : Read(field.Instance).Dependency;
                    receiverType = field.Instance?.Type;
                    member = field.Field;
                    // Only projected root fields can represent Akbura declarations;
                    // an unrelated nested CLR field with the same name cannot.
                    source = field.Instance is IInstanceReferenceOperation ? MapSource(field.Field) : null;
                    kind = ExpressionBindingDependencyKind.Field;
                    observation = SourceObservation(source);
                    classification = field.Field.HasConstantValue ? ExpressionBindingValueClassification.Constant :
                        observation != ExpressionBindingObservation.None ? ExpressionBindingValueClassification.Observable :
                        field.Field.IsReadOnly ? ExpressionBindingValueClassification.Stable : ExpressionBindingValueClassification.Unobservable;
                    deduplicate = true;
                    break;
                case ILocalReferenceOperation local:
                    member = local.Local; source = MapSource(local.Local);
                    kind = ExpressionBindingDependencyKind.Local;
                    observation = SourceObservation(source);
                    classification = local.Local.IsConst ? ExpressionBindingValueClassification.Constant :
                        observation != ExpressionBindingObservation.None ? ExpressionBindingValueClassification.Observable : ExpressionBindingValueClassification.Unobservable;
                    deduplicate = true;
                    break;
                case IParameterReferenceOperation parameter:
                    member = parameter.Parameter; source = MapSource(parameter.Parameter);
                    kind = ExpressionBindingDependencyKind.Parameter;
                    observation = SourceObservation(source);
                    classification = observation != ExpressionBindingObservation.None ? ExpressionBindingValueClassification.Observable : ExpressionBindingValueClassification.Unobservable;
                    deduplicate = true;
                    break;
                case IInstanceReferenceOperation:
                    kind = ExpressionBindingDependencyKind.Instance;
                    classification = ExpressionBindingValueClassification.Stable;
                    deduplicate = true;
                    break;
                case IArrayElementReferenceOperation array:
                    receiver = Read(array.ArrayReference).Dependency;
                    receiverType = array.ArrayReference.Type;
                    indices = array.Indices.SelectAsArray(index => Read(index));
                    kind = ExpressionBindingDependencyKind.ArrayElement;
                    classification = ExpressionBindingValueClassification.Unobservable;
                    break;
                case IConversionOperation conversion:
                    var operand = Read(conversion.Operand);
                    if (conversion.OperatorMethod == null)
                        return new ExpressionBindingEvaluation(operation, operand.Dependency, conditions, children.ToImmutable());
                    HiddenCall(operation, conversion.OperatorMethod);
                    break;
                case IInvocationOperation invocation:
                    if (invocation.Instance != null) Read(invocation.Instance);
                    foreach (var argument in invocation.Arguments) Read(argument);
                    if (!IsFormattingMethod(invocation.TargetMethod)) HiddenCall(operation, invocation.TargetMethod);
                    break;
                case INameOfOperation:
                case ITypeOfOperation:
                case ISizeOfOperation:
                    // These operands are not runtime reads.
                    classification = ExpressionBindingValueClassification.Constant;
                    break;
                case IAnonymousFunctionOperation:
                case ILocalFunctionOperation:
                case IAssignmentOperation:
                case IIncrementOrDecrementOperation:
                case IAwaitOperation:
                case IDynamicInvocationOperation:
                case IDynamicMemberReferenceOperation:
                case IObjectCreationOperation:
                case IThrowOperation:
                case IRecursivePatternOperation:
                case IListPatternOperation:
                case IMethodReferenceOperation:
                case IEventReferenceOperation:
                case IFunctionPointerInvocationOperation:
                    // Deferred bodies, mutation, dynamic dispatch and allocation
                    // need an explicit future evaluation/observation contract.
                    kind = ExpressionBindingDependencyKind.Unsupported;
                    classification = ExpressionBindingValueClassification.Unsupported;
                    Unsupported(operation, operation.Kind.ToString());
                    break;
                default:
                    foreach (var child in operation.ChildOperations) Read(child);
                    if (operation.Kind == OperationKind.Invalid)
                    {
                        kind = ExpressionBindingDependencyKind.Unsupported;
                        classification = ExpressionBindingValueClassification.Unsupported;
                        _unknown = true;
                    }
                    else if (!IsSupportedComputedOperation(operation))
                    {
                        kind = ExpressionBindingDependencyKind.Unsupported;
                        classification = ExpressionBindingValueClassification.Unsupported;
                        Unsupported(operation, operation.Kind.ToString());
                    }
                    if (operation is IBinaryOperation { OperatorMethod: { } binaryMethod }) HiddenCall(operation, binaryMethod);
                    if (operation is IUnaryOperation { OperatorMethod: { } unaryMethod }) HiddenCall(operation, unaryMethod);
                    break;
            }

            if (classification is ExpressionBindingValueClassification.Unobservable or ExpressionBindingValueClassification.Unsupported)
                _unknown = true;
            ExpressionBindingDependency? dependency = null;
            if (deduplicate)
            {
                dependency = _dependencies.FirstOrDefault(existing => existing.Kind == kind &&
                    ReferenceEquals(existing.Receiver, receiver) && SymbolEqualityComparer.Default.Equals(existing.Member, member) &&
                    SymbolEqualityComparer.Default.Equals(existing.ResultType, operation.Type) && SameConditions(existing.Conditions, conditions));
            }
            if (dependency == null)
            {
                dependency = new ExpressionBindingDependency(_dependencies.Count, kind, receiver, member, source,
                    receiverType, operation.Type, observation, classification, conditions, indices, notification);
                _dependencies.Add(dependency);
            }
            return new ExpressionBindingEvaluation(operation, dependency, conditions, children.ToImmutable());
        }
        finally
        {
            children.Free();
        }
    }

    private static bool SameConditions(ImmutableArray<ExpressionBindingCondition> left, ImmutableArray<ExpressionBindingCondition> right) =>
        left.Length == right.Length && left.Zip(right, (a, b) => ReferenceEquals(a.Operation, b.Operation) && a.Branch == b.Branch && a.Arm == b.Arm).All(same => same);

    private static bool IsSupportedComputedOperation(RoslynOperation operation) => operation is
        ILiteralOperation or IArgumentOperation or IBinaryOperation or IUnaryOperation or
        IParenthesizedOperation or IInterpolatedStringOperation or IInterpolatedStringTextOperation or
        IInterpolationOperation or ITupleOperation or IIsTypeOperation or IIsPatternOperation or
        IConstantPatternOperation or IRelationalPatternOperation or IDeclarationPatternOperation or
        IDiscardPatternOperation or INegatedPatternOperation or IBinaryPatternOperation or
        IArrayCreationOperation or IArrayInitializerOperation or IDefaultValueOperation or IDelegateCreationOperation;

    private static ExpressionBindingObservation SourceObservation(AkburaSymbol? source) =>
        source is Akbura.Language.Symbols.IStateSymbol or Akbura.Language.Symbols.IParamSymbol
            ? ExpressionBindingObservation.AkburaValue : ExpressionBindingObservation.None;

    private AkburaSymbol? MapSource(ISymbol symbol)
    {
        var source = _mapSymbol(symbol);
        // A same-named C# local or instance field is not automatically the
        // projected state/parameter. Require the probe's declaration identity.
        if (source is Akbura.Language.Symbols.IStateSymbol or Akbura.Language.Symbols.IParamSymbol &&
            !symbol.DeclaringSyntaxReferences.Any(reference =>
                reference.GetSyntax().HasAnnotations(CSharpProbeBinder.ProjectedSymbolAnnotationKind)))
            return null;
        return source;
    }

    private static bool Implements(ITypeSymbol? type, string metadataName) =>
        type != null && (type.ToDisplayString() == metadataName || type.AllInterfaces.Any(contract => contract.ToDisplayString() == metadataName));

    private static ExpressionBindingObservation PropertyObservation(IPropertySymbol property, ITypeSymbol? receiverType, out ISymbol? notification)
    {
        notification = null;
        if (property.IsStatic) return ExpressionBindingObservation.None;
        var observation = Implements(receiverType, "System.ComponentModel.INotifyPropertyChanged")
            ? ExpressionBindingObservation.PropertyChanged : ExpressionBindingObservation.None;
        var avalonia = false;
        for (var type = receiverType as INamedTypeSymbol; type != null; type = type.BaseType)
            avalonia |= type.ToDisplayString() == "Avalonia.AvaloniaObject";
        if (avalonia && CSharpProbeBinder.TryGetAvaloniaPropertyField(property, out var field) &&
            field.DeclaredAccessibility == Accessibility.Public)
        {
            for (var fieldType = field.Type as INamedTypeSymbol; fieldType != null; fieldType = fieldType.BaseType)
            {
                if (fieldType.OriginalDefinition.ToDisplayString() == "Avalonia.AvaloniaProperty<TValue>" &&
                    SymbolEqualityComparer.Default.Equals(fieldType.TypeArguments[0], property.Type))
                { notification = field; return ExpressionBindingObservation.AvaloniaProperty; }
            }
        }
        return observation;
    }

    private static bool IsFormattingMethod(IMethodSymbol method) =>
        !method.IsVirtual && (method.ContainingType.SpecialType == SpecialType.System_String ||
        method.Name == "ToString" && method.ContainingType.SpecialType is >= SpecialType.System_Boolean and <= SpecialType.System_Decimal);

    private void HiddenCall(RoslynOperation operation, IMethodSymbol method)
    {
        _unknown = true;
        _diagnostics.Add(new AkburaSemanticDiagnostic(_syntax.Expression,
            ErrorCodes.AKBURA_SEMANTIC_ExpressionBindingHiddenDependencies, [method.ToDisplayString()],
            AkburaDiagnosticSeverity.Warning, MapSpan(operation.Syntax.Span)));
    }

    private void Unsupported(RoslynOperation operation, string form)
    {
        _unknown = true;
        _diagnostics.Add(new AkburaSemanticDiagnostic(_syntax.Expression,
            ErrorCodes.AKBURA_SEMANTIC_ExpressionBindingUnsupportedExpression, [form],
            span: MapSpan(operation.Syntax.Span)));
    }

    private TextSpan MapSpan(TextSpan span)
    {
        var start = Math.Max(0, Math.Min(_syntax.Expression.FullWidth, span.Start - _root.Syntax.FullSpan.Start));
        return new TextSpan(_syntax.Expression.Position + start,
            Math.Min(span.Length, _syntax.Expression.FullWidth - start));
    }
}

using Akbura.Language.Binder;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;

namespace Akbura.Language.CodeGeneration;

internal enum ExpressionBindingInitializationKind : byte
{
    RequiresRenderRefresh,
    InitialOnly,
}

internal static class ExpressionBindingInitializationClassifier
{
    public static ExpressionBindingInitializationKind Classify(ExpressionBindingPlan plan,
        ISet<TextSpan> stableNamedReceivers)
    {
        // Partial plans can include projected named controls. Prove each read
        // below rather than treating Partial (or absence of locals) as stable.
        return plan.Evaluation != null && !plan.Conversion.IsUserDefined &&
            IsStable(plan.Evaluation, stableNamedReceivers)
                ? ExpressionBindingInitializationKind.InitialOnly
                : ExpressionBindingInitializationKind.RequiresRenderRefresh;
    }

    private static bool IsStable(ExpressionBindingEvaluation evaluation, ISet<TextSpan> named)
    {
        var operation = evaluation.Operation;
        if (operation.ConstantValue.HasValue || operation is INameOfOperation or ITypeOfOperation or ISizeOfOperation)
            return true;

        switch (operation)
        {
            case IInstanceReferenceOperation { Syntax: Microsoft.CodeAnalysis.CSharp.Syntax.BaseExpressionSyntax }:
                return false;
            case ILocalReferenceOperation local:
                return local.Local.IsConst || IsObservableSource(evaluation) || named.Contains(operation.Syntax.Span);
            case IParameterReferenceOperation:
                return IsObservableSource(evaluation) || named.Contains(operation.Syntax.Span);
            case IFieldReferenceOperation field:
                if (IsObservableSource(evaluation) || named.Contains(operation.Syntax.Span)) return true;
                if (!field.Field.IsReadOnly && !field.Field.IsConst) return false;
                break;
            case IPropertyReferenceOperation property:
                if (property.Syntax is Microsoft.CodeAnalysis.CSharp.Syntax.IdentifierNameSyntax &&
                    !ReferenceEquals(CSharpProbeBuilder.GetMarkupLoopCodeGenerationSyntax(new CSharpOperationDefinition(property)), property.Syntax))
                    return false; // The observable probe index becomes a render-local index in generated code.
                if (IsObservableSource(evaluation)) return true;
                // AvaloniaObject's INPC contract covers Avalonia properties,
                // not arbitrary component CLR getters without a descriptor.
                if (property.Instance is IInstanceReferenceOperation &&
                    evaluation.Dependency is not { Observation: ExpressionBindingObservation.AvaloniaProperty })
                    return false;
                if (evaluation.Dependency is not { Observation: not ExpressionBindingObservation.None } &&
                    evaluation.Dependency?.Classification != ExpressionBindingValueClassification.Stable)
                    return false;
                break;
            case IInvocationOperation:
            case IArrayElementReferenceOperation:
            case IArrayCreationOperation:
            case IDeclarationPatternOperation:
                // Calls may use ambient inputs; array mutation is unobservable.
                // Pattern locals need a separate flow/lifetime proof.
                return false;
            case IConversionOperation { OperatorMethod: not null }:
            case IBinaryOperation { OperatorMethod: not null }:
            case IUnaryOperation { OperatorMethod: not null }:
                return false;
        }

        if (evaluation.Dependency?.Classification == ExpressionBindingValueClassification.Unsupported)
            return false;
        foreach (var child in evaluation.Children)
            if (!IsStable(child, named)) return false;
        return true;
    }

    private static bool IsObservableSource(ExpressionBindingEvaluation evaluation) =>
        evaluation.Dependency?.SourceSymbol is Akbura.Language.Symbols.IInjectSymbol ||
        evaluation.Dependency is { Observation: ExpressionBindingObservation.AkburaValue,
            SourceSymbol: Akbura.Language.Symbols.IStateSymbol or Akbura.Language.Symbols.IParamSymbol };
}

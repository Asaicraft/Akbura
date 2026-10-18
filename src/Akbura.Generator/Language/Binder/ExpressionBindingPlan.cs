using Akbura.Language.BoundTree;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Immutable;
using System.Linq;
using RoslynOperation = Microsoft.CodeAnalysis.IOperation;
using AkburaSymbol = Akbura.Language.Symbols.ISymbol;

namespace Akbura.Language.Binder;

[Flags]
internal enum ExpressionBindingObservation
{
    None = 0,
    AkburaValue = 1,
    PropertyChanged = 2,
    AvaloniaProperty = 4,
    CollectionChanged = 8,
}

internal enum ExpressionBindingDependencyKind
{
    Instance, Local, Parameter, Field, Property, Indexer, ArrayElement, Computed, Unsupported,
}

internal enum ExpressionBindingValueClassification
{
    Constant, Stable, Observable, Unobservable, Computed, Unsupported,
}

internal enum ExpressionBindingObservability
{
    Constant, Observable, Partial, Unsupported,
}

internal enum ExpressionBindingBranch
{
    WhenTrue, WhenFalse, WhenNull, WhenNotNull, SwitchArm, WhenPatternMatches, WhenGuardTrue,
}

// Conditions identify semantic operations, not source strings. The ordered path
// is a conjunction; switch-arm ordinals also retain first-matching-arm semantics.
internal sealed class ExpressionBindingCondition
{
    public ExpressionBindingCondition(RoslynOperation operation, ExpressionBindingBranch branch, int arm = -1)
    {
        Operation = operation;
        Branch = branch;
        Arm = arm;
    }

    public RoslynOperation Operation { get; }
    public ExpressionBindingBranch Branch { get; }
    public int Arm { get; }
}

internal sealed class ExpressionBindingDependency
{
    public ExpressionBindingDependency(int id, ExpressionBindingDependencyKind kind,
        ExpressionBindingDependency? receiver, ISymbol? member, AkburaSymbol? sourceSymbol,
        ITypeSymbol? receiverType, ITypeSymbol? resultType, ExpressionBindingObservation observation,
        ExpressionBindingValueClassification classification,
        ImmutableArray<ExpressionBindingCondition> conditions,
        ImmutableArray<ExpressionBindingEvaluation> indices, ISymbol? notificationMember = null)
    {
        Id = id;
        Kind = kind;
        Receiver = receiver;
        Member = member;
        SourceSymbol = sourceSymbol;
        ReceiverType = receiverType;
        ResultType = resultType;
        Observation = observation;
        Classification = classification;
        Conditions = conditions;
        Indices = indices;
        NotificationMember = notificationMember;
    }
    public int Id { get; }
    public ExpressionBindingDependencyKind Kind { get; }
    public ExpressionBindingDependency? Receiver { get; }
    public ISymbol? Member { get; }
    public AkburaSymbol? SourceSymbol { get; }
    public ITypeSymbol? ReceiverType { get; }
    public ITypeSymbol? ResultType { get; }
    public ExpressionBindingObservation Observation { get; }
    public ExpressionBindingValueClassification Classification { get; }
    public ImmutableArray<ExpressionBindingCondition> Conditions { get; }
    public ImmutableArray<ExpressionBindingEvaluation> Indices { get; }
    public ISymbol? NotificationMember { get; }
}

// Evaluation occurrences are never deduplicated. Two reads can share a planned
// observation, but retain their distinct operations and C# evaluation order.
internal sealed class ExpressionBindingEvaluation
{
    public ExpressionBindingEvaluation(RoslynOperation operation, ExpressionBindingDependency? dependency,
        ImmutableArray<ExpressionBindingCondition> conditions, ImmutableArray<ExpressionBindingEvaluation> children)
    {
        Operation = operation;
        Dependency = dependency;
        Conditions = conditions;
        Children = children;
    }

    public RoslynOperation Operation { get; }
    public ExpressionBindingDependency? Dependency { get; }
    public ImmutableArray<ExpressionBindingCondition> Conditions { get; }
    public ImmutableArray<ExpressionBindingEvaluation> Children { get; }
}

// Owned by the current bound node/semantic model, just like ValueOperation.
// No global cache, runtime object, or previous snapshot is retained here.
internal sealed class ExpressionBindingPlan
{
    public ExpressionBindingPlan(CSharpOperationDefinition expression, ITypeSymbol? resultType,
        ITypeSymbol? targetType, AkburaConversion conversion, ExpressionBindingEvaluation? evaluation,
        ImmutableArray<ExpressionBindingDependency> dependencies,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics, bool hasUnknownDependencies)
    {
        Expression = expression;
        ResultType = resultType;
        TargetType = targetType;
        Conversion = conversion;
        Evaluation = evaluation;
        Dependencies = dependencies;
        Diagnostics = diagnostics;
        HasUnknownDependencies = hasUnknownDependencies;
    }
    public CSharpOperationDefinition Expression { get; }
    public ITypeSymbol? ResultType { get; }
    public ITypeSymbol? TargetType { get; }
    public AkburaConversion Conversion { get; }
    public ExpressionBindingEvaluation? Evaluation { get; }
    public ImmutableArray<ExpressionBindingDependency> Dependencies { get; }
    public ImmutableArray<AkburaSemanticDiagnostic> Diagnostics { get; }
    public bool HasUnknownDependencies { get; }
    public ExpressionBindingObservability Observability =>
        Diagnostics.Any(d => d.Severity == Syntax.AkburaDiagnosticSeverity.Error) || Evaluation == null
            ? ExpressionBindingObservability.Unsupported
            : HasUnknownDependencies ? ExpressionBindingObservability.Partial
            : Dependencies.Any(d => d.Observation != ExpressionBindingObservation.None)
                ? ExpressionBindingObservability.Observable : ExpressionBindingObservability.Constant;
}

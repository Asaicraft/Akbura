using Akbura.Language.Binder;
using Akbura.Language.BoundTree;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace Akbura.Language.Operations;

internal sealed class MarkupNameAssignmentOperation : IMarkupNameAssignmentOperation
{
    public MarkupNameAssignmentOperation(
        MarkupAttachedPropertyAttributeSyntax syntax,
        IMarkupComponentSymbol? containingComponent,
        IMarkupNameSymbol? nameSymbol,
        bool hasErrors)
    {
        Syntax = syntax ?? throw new ArgumentNullException(nameof(syntax));
        ContainingComponent = containingComponent;
        NameSymbol = nameSymbol;
        HasErrors = hasErrors;
    }

    public OperationKind Kind => OperationKind.MarkupNameAssignment;

    public OperationLanguage Language => OperationLanguage.Markup;

    AkburaSyntax IOperation.Syntax => Syntax;

    public MarkupAttachedPropertyAttributeSyntax Syntax { get; }

    MarkupAttributeSyntax IMarkupAttributeOperation.Syntax => Syntax;

    public IOperation? Parent => null;

    public ImmutableArray<IOperation> Children => ImmutableArray<IOperation>.Empty;

    public ISymbol? TargetSymbol => NameSymbol;

    public ISymbol? TypeSymbol => NameSymbol;

    public CSharpOperationDefinition CSharpDefinition => default;

    public bool IsImplicit => false;

    public bool HasErrors { get; }

    public object? ConstantValue => NameSymbol?.Name;

    public IMarkupComponentSymbol? ContainingComponent { get; }

    public IMarkupNameSymbol? NameSymbol { get; }

    public bool IsAssignedDuringFirstUpdate => true;

    public void Accept(OperationVisitor visitor)
    {
        visitor.VisitMarkupNameAssignment(this);
    }

    public TResult? Accept<TParameter, TResult>(
        OperationVisitor<TParameter, TResult> visitor,
        TParameter parameter)
    {
        return visitor.VisitMarkupNameAssignment(this, parameter);
    }

    public bool Equals(IOperation? other)
    {
        return ReferenceEquals(this, other);
    }

    public override bool Equals(object? obj)
    {
        return obj is IOperation operation && Equals(operation);
    }

    public override int GetHashCode()
    {
        return RuntimeHelpers.GetHashCode(this);
    }

    public string ToDisplayString()
    {
        return NameSymbol == null
            ? Syntax.ToFullString()
            : $"x.Name={NameSymbol.IdentifierText}";
    }

    public override string ToString()
    {
        return ToDisplayString();
    }
}

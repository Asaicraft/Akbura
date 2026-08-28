using Akbura.Language.Binder;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text;

namespace Akbura.Language.Operations;

internal sealed class MarkupWhitespaceDirectiveOperation : IMarkupWhitespaceDirectiveOperation
{
    public MarkupWhitespaceDirectiveOperation(
        MarkupAttachedPropertyAttributeSyntax syntax,
        IMarkupComponentSymbol? containingComponent,
        IPropertySymbol? property,
        string rawValue,
        MarkupWhitespaceMode? declaredMode,
        MarkupWhitespaceMode effectiveMode,
        bool hasErrors)
    {
        Syntax = syntax ??
            throw new ArgumentNullException(nameof(syntax));

        ContainingComponent = containingComponent;
        Property = property;
        RawValue = rawValue ?? string.Empty;
        DeclaredMode = declaredMode;
        EffectiveMode = effectiveMode;
        HasErrors = hasErrors;
    }

    public OperationKind Kind => OperationKind.MarkupWhitespaceDirective;

    public OperationLanguage Language => OperationLanguage.Markup;

    AkburaSyntax IOperation.Syntax => Syntax;

    public MarkupAttachedPropertyAttributeSyntax Syntax { get; }

    MarkupAttributeSyntax IMarkupAttributeOperation.Syntax => Syntax;

    public IOperation? Parent => null;

    public ImmutableArray<IOperation> Children => [];

    public ISymbol? TargetSymbol => Property;

    public ISymbol? TypeSymbol => Property;

    public CSharpOperationDefinition CSharpDefinition => default;

    public bool IsImplicit => false;

    public bool HasErrors { get; }

    public object? ConstantValue => DeclaredMode;

    public IMarkupComponentSymbol? ContainingComponent { get; }

    public IPropertySymbol? Property { get; }

    public string RawValue { get; }

    public MarkupWhitespaceMode? DeclaredMode { get; }

    public MarkupWhitespaceMode EffectiveMode { get; }

    public void Accept(OperationVisitor visitor)
    {
        visitor.VisitMarkupWhitespaceDirective(this);
    }

    public TResult? Accept<TParameter, TResult>(
        OperationVisitor<TParameter, TResult> visitor,
        TParameter parameter)
    {
        return visitor.VisitMarkupWhitespaceDirective(
            this,
            parameter);
    }

    public bool Equals(IOperation? other)
    {
        return ReferenceEquals(this, other);
    }

    public override bool Equals(object? obj)
    {
        return obj is IOperation operation &&
               Equals(operation);
    }

    public override int GetHashCode()
    {
        return RuntimeHelpers.GetHashCode(this);
    }

    public string ToDisplayString()
    {
        return $"xml.space=\"{RawValue}\"";
    }

    public override string ToString()
    {
        return ToDisplayString();
    }
}
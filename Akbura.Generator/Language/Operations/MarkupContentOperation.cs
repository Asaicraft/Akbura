using Akbura.Language.Binder;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace Akbura.Language.Operations;

internal sealed class MarkupContentOperation : IMarkupContentOperation
{
    public MarkupContentOperation(
        MarkupElementSyntax syntax,
        IMarkupComponentSymbol? containingComponent,
        IPropertySymbol? property,
        MarkupContentModel contentModel,
        ImmutableArray<MarkupChildContent> content,
        CSharpSymbolDefinition valueType,
        CSharpOperationDefinition valueOperation,
        string? literalValue,
        bool isSynthesizedString,
        bool hasErrors,
        ICSharpOperation? valueOperationTree = null)
    {
        Syntax = syntax ?? throw new ArgumentNullException(nameof(syntax));
        ContainingComponent = containingComponent;
        Property = property;
        ContentModel = contentModel;
        Content = content.IsDefault
            ? ImmutableArray<MarkupChildContent>.Empty
            : content;
        ValueType = valueType;
        ValueOperation = valueOperation;
        LiteralValue = literalValue;
        IsSynthesizedString = isSynthesizedString;
        HasErrors = hasErrors;
        ValueOperationTree = valueOperationTree;
        AdoptCSharpOperationTree(ValueOperationTree);
        Children = ValueOperationTree == null
            ? ImmutableArray<IOperation>.Empty
            : ImmutableArray.Create<IOperation>(ValueOperationTree);
    }

    public OperationKind Kind => OperationKind.MarkupContent;

    public OperationLanguage Language => OperationLanguage.Markup;

    AkburaSyntax IOperation.Syntax => Syntax;

    public MarkupElementSyntax Syntax { get; }

    public IOperation? Parent => null;

    public ImmutableArray<IOperation> Children { get; }

    public ISymbol? TargetSymbol => Property;

    public ISymbol? TypeSymbol => Property;

    public CSharpOperationDefinition CSharpDefinition => ValueOperation;

    public bool IsImplicit => false;

    public bool HasErrors { get; }

    public object? ConstantValue => LiteralValue;

    public IMarkupComponentSymbol? ContainingComponent { get; }

    public IPropertySymbol? Property { get; }

    public MarkupContentModel ContentModel { get; }

    public ImmutableArray<MarkupChildContent> Content { get; }

    public CSharpSymbolDefinition ValueType { get; }

    public CSharpOperationDefinition ValueOperation { get; }

    public ICSharpOperation? ValueOperationTree { get; }

    public string? LiteralValue { get; }

    public bool IsSynthesizedString { get; }

    public void Accept(OperationVisitor visitor)
    {
        visitor.VisitMarkupContent(this);
    }

    public TResult? Accept<TParameter, TResult>(
        OperationVisitor<TParameter, TResult> visitor,
        TParameter parameter)
    {
        return visitor.VisitMarkupContent(this, parameter);
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
        return Property == null
            ? Syntax.ToFullString()
            : $"{Property.Name}={Syntax.Body.ToFullString().Trim()}";
    }

    public override string ToString()
    {
        return ToDisplayString();
    }

    private void AdoptCSharpOperationTree(ICSharpOperation? operation)
    {
        if (operation is CSharpOperation csharpOperation)
        {
            csharpOperation.SetParent(this);
        }
    }
}

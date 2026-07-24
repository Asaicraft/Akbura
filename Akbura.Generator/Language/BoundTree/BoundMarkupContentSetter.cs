using Akbura.Language.Binder;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System.Collections.Immutable;
using BinderType = Akbura.Language.Binder.Binder;

namespace Akbura.Language.BoundTree;

internal sealed class BoundMarkupContentSetter : BoundNode
{
    public BoundMarkupContentSetter(
        MarkupElementSyntax syntax,
        BinderType binder,
        IMarkupComponentSymbol? containingComponent,
        IPropertySymbol? property,
        MarkupContentModel contentModel,
        ImmutableArray<MarkupChildContent> content,
        CSharpSymbolDefinition valueType,
        CSharpOperationDefinition valueOperation,
        AkburaConversion valueConversion,
        MarkupWhitespaceMode markupWhitespaceMode,
        string? literalValue,
        bool isSynthesizedString,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics = default,
        bool hasErrors = false)
        : base(
            BoundKind.MarkupContentSetter,
            syntax,
            binder,
            property == null ? AkburaSymbolInfo.None(CandidateReason.NotFound) : AkburaSymbolInfo.Success(property),
            diagnostics,
            hasErrors: hasErrors)
    {
        ContainingComponent = containingComponent;
        Property = property;
        ContentModel = contentModel;
        Content = content.IsDefault
            ? ImmutableArray<MarkupChildContent>.Empty
            : content;
        ValueType = valueType;
        ValueOperation = valueOperation;
        ValueConversion = valueConversion;
        LiteralValue = literalValue;
        IsSynthesizedString = isSynthesizedString;
        WhitespaceMode = markupWhitespaceMode;
    }

    public new MarkupElementSyntax Syntax => (MarkupElementSyntax)base.Syntax;

    public IMarkupComponentSymbol? ContainingComponent { get; }

    public IPropertySymbol? Property { get; }

    public MarkupContentModel ContentModel { get; }

    public ImmutableArray<MarkupChildContent> Content { get; }

    public CSharpSymbolDefinition ValueType { get; }

    public CSharpOperationDefinition ValueOperation { get; }

    public AkburaConversion ValueConversion { get; }

    public MarkupWhitespaceMode WhitespaceMode { get; }

    public string? LiteralValue { get; }

    public bool IsSynthesizedString { get; }

    public BoundMarkupContentSetter Update(
        IMarkupComponentSymbol? containingComponent,
        IPropertySymbol? property,
        MarkupContentModel contentModel,
        ImmutableArray<MarkupChildContent> content,
        CSharpSymbolDefinition valueType,
        CSharpOperationDefinition valueOperation,
        AkburaConversion valueConversion,
        MarkupWhitespaceMode whitespaceMode,
        string? literalValue,
        bool isSynthesizedString)
    {
        if (ReferenceEquals(containingComponent, ContainingComponent) &&
            ReferenceEquals(property, Property) &&
            contentModel.Equals(ContentModel) &&
            content == Content &&
            valueType.Equals(ValueType) &&
            valueOperation.Equals(ValueOperation) &&
            valueConversion.Equals(ValueConversion) &&
            whitespaceMode.Equals(WhitespaceMode) &&
            literalValue == LiteralValue &&
            isSynthesizedString == IsSynthesizedString)
        {
            return this;
        }

        return new BoundMarkupContentSetter(
            Syntax,
            Binder,
            containingComponent,
            property,
            contentModel,
            content,
            valueType,
            valueOperation,
            valueConversion,
            whitespaceMode,
            literalValue,
            isSynthesizedString,
            Diagnostics,
            HasErrors);
    }

    public override void Accept(BoundTreeVisitor visitor) => visitor.VisitMarkupContentSetter(this);

    public override TResult? Accept<TResult>(BoundTreeVisitor<TResult> visitor)
        where TResult : default =>
        visitor.VisitMarkupContentSetter(this);

    public override TResult? Accept<TParameter, TResult>(
        BoundTreeVisitor<TParameter, TResult> visitor,
        TParameter parameter)
        where TResult : default =>
        visitor.VisitMarkupContentSetter(this, parameter);
}

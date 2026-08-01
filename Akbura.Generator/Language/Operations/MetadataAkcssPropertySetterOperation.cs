using Akbura.Language.Binder;
using Akbura.Language.BoundTree;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using AkburaPropertySymbol = Akbura.Language.Symbols.IPropertySymbol;
using AkburaSymbol = Akbura.Language.Symbols.ISymbol;

namespace Akbura.Language.Operations;

internal sealed class MetadataAkcssPropertySetterOperation
    : MetadataAkcssOperation, IAkcssPropertySetterOperation
{
    public MetadataAkcssPropertySetterOperation(
        IMetadataAkcssSymbol containingSymbol,
        MetadataAkcssOperationData data,
        AkburaConversion valueConversion)
        : base(containingSymbol, data)
    {
        Property = MetadataPropertySymbol.Create(
            containingSymbol,
            data.Property,
            data.PropertyType,
            data.PropertyAccessKind,
            data.AvaloniaProperty,
            data.AttachedGetter,
            data.AttachedSetter,
            data.PropertyOwnerType,
            data.AttachedTargetType,
            data.CanRead,
            data.CanWrite,
            containingSymbol.MetadataCarrierType.Locations);
        ValueType = data.ExpressionType == null
            ? default
            : new CSharpSymbolDefinition(data.ExpressionType);
        ValueConversion = valueConversion;
        ConvertedValue = MetadataAkcssConstantValue.Parse(
            data.ConstantValue,
            data.ConstantValueType);
    }

    public override OperationKind Kind => OperationKind.AkcssAssignment;

    AkcssAssignmentSyntax? IAkcssPropertySetterOperation.Syntax => null;

    public override AkburaSymbol? TargetSymbol => Property;

    public override bool HasErrors => base.HasErrors ||
        Property == null ||
        !Property.CanWrite;

    public override object? ConstantValue => ConvertedValue;

    public AkburaPropertySymbol? Property { get; }

    public CSharpSymbolDefinition ValueType { get; }

    public CSharpOperationDefinition ValueOperation => default;

    public AkburaConversion ValueConversion { get; }

    public ICSharpOperation? ValueOperationTree => null;

    public AkcssPropertyValueKind ValueKind => Data.ValueKind;

    public bool RequiresBrushConversion => Data.RequiresBrushConversion;

    public object? ConvertedValue { get; }

    public override void Accept(OperationVisitor visitor)
    {
        visitor.VisitAkcssPropertySetter(this);
    }

    public override TResult Accept<TParameter, TResult>(
        OperationVisitor<TParameter, TResult> visitor,
        TParameter parameter)
    {
        return visitor.VisitAkcssPropertySetter(this, parameter)!;
    }

    public override string ToDisplayString()
    {
        return $"{Property?.Name ?? Data.Property ?? "<property>"}: " +
            (Expression ?? "default");
    }
}

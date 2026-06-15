using Akbura.Language.Syntax;

namespace Akbura.Language.Operations;

internal interface IAkcssPropertySetterOperation : IPropertySetterOperation, IAkcssOperation
{
    new AkcssAssignmentSyntax Syntax { get; }

    AkcssPropertyValueKind ValueKind { get; }

    bool RequiresBrushConversion { get; }

    object? ConvertedValue { get; }
}

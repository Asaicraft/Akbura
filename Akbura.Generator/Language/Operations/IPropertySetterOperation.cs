using Akbura.Language.Symbols;

namespace Akbura.Language.Operations;

internal interface IPropertySetterOperation : IOperation
{
    IPropertySymbol? Property { get; }

    CSharpSymbolDefinition ValueType { get; }

    CSharpOperationDefinition ValueOperation { get; }
}

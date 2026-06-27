using Akbura.Language.Binder;
using Akbura.Language.Symbols;

namespace Akbura.Language.Operations;

internal interface IPropertySetterOperation : IOperation
{
    IPropertySymbol? Property { get; }

    CSharpSymbolDefinition ValueType { get; }

    CSharpOperationDefinition ValueOperation { get; }

    ICSharpOperation? ValueOperationTree { get; }
}

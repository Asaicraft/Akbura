using Microsoft.CodeAnalysis;
using RoslynIOperation = Microsoft.CodeAnalysis.IOperation;
using RoslynOperationKind = Microsoft.CodeAnalysis.OperationKind;

namespace Akbura.Language.Operations;

internal readonly struct CSharpOperationDefinition
{
    public CSharpOperationDefinition(RoslynIOperation operation)
    {
        Operation = operation ?? throw new System.ArgumentNullException(nameof(operation));
    }

    public RoslynIOperation? Operation { get; }

    public bool IsDefault => Operation == null;

    public RoslynOperationKind? Kind => Operation?.Kind;

    public ITypeSymbol? Type => Operation?.Type;

    public Optional<object?> ConstantValue => Operation?.ConstantValue ?? default;

    public SyntaxNode? Syntax => Operation?.Syntax;

    public string ToDisplayString()
    {
        return Operation?.Syntax.ToString() ?? string.Empty;
    }
}

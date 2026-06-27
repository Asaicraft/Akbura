using Akbura.Language.Binder;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using System;
using AkburaSymbol = Akbura.Language.Symbols.ISymbol;
using RoslynIOperation = Microsoft.CodeAnalysis.IOperation;
using RoslynSymbol = Microsoft.CodeAnalysis.ISymbol;

namespace Akbura.Language.Operations;

internal static class CSharpOperationTreeBuilder
{
    public static ICSharpOperation? Create(
        AkburaSyntax syntax,
        RoslynIOperation? operation,
        IOperation? parent = null,
        Func<RoslynSymbol, AkburaSymbol?>? symbolMapper = null)
    {
        if (operation == null)
        {
            return null;
        }

        var targetSymbol = GetTargetCSharpSymbol(operation);
        var mappedTarget = targetSymbol == null
            ? null
            : symbolMapper?.Invoke(targetSymbol);
        var node = new CSharpOperation(
            syntax,
            parent,
            new CSharpOperationDefinition(operation),
            targetSymbol == null ? default : new CSharpSymbolDefinition(targetSymbol),
            operation.Type == null ? default : new CSharpSymbolDefinition(operation.Type),
            mappedTarget,
            operation.Kind == Microsoft.CodeAnalysis.OperationKind.Invalid);

        var children = ArrayBuilder<IOperation>.GetInstance();
        foreach (var childOperation in operation.ChildOperations)
        {
            var child = Create(
                syntax,
                childOperation,
                node,
                symbolMapper);
            if (child != null)
            {
                children.Add(child);
            }
        }

        node.SetChildren(children.ToImmutableAndFree());
        return node;
    }

    private static RoslynSymbol? GetTargetCSharpSymbol(RoslynIOperation operation)
    {
        return operation switch
        {
            IInvocationOperation invocation => invocation.TargetMethod,
            IMethodReferenceOperation methodReference => methodReference.Method,
            IPropertyReferenceOperation propertyReference => propertyReference.Property,
            IFieldReferenceOperation fieldReference => fieldReference.Field,
            IEventReferenceOperation eventReference => eventReference.Event,
            ILocalReferenceOperation localReference => localReference.Local,
            IParameterReferenceOperation parameterReference => parameterReference.Parameter,
            IObjectCreationOperation objectCreation => objectCreation.Constructor,
            IVariableDeclaratorOperation variableDeclarator => variableDeclarator.Symbol,
            IConversionOperation conversion => conversion.OperatorMethod,
            IUnaryOperation unary => unary.OperatorMethod,
            IBinaryOperation binary => binary.OperatorMethod,
            _ => null,
        };
    }
}

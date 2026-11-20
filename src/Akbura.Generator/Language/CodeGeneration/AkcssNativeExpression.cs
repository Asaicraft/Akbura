using Akbura.Language.Binder;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Immutable;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using NativeOperation = Akbura.Language.Operations.IOperation;

namespace Akbura.Language.CodeGeneration;

internal static class AkcssNativeExpression
{
    public static bool TryFormat(AkcssContextualOperation owner, CSharpOperationDefinition definition,
        ImmutableDictionary<AkcssMixinParameterSymbol, string> locals, out string expression, out string error)
    {
        expression = string.Empty;
        error = string.Empty;
        if (definition.Operation is not { SemanticModel: { } model, Syntax: CSharp.ExpressionSyntax syntax } operation)
        {
            error = "No bound C# value is available for native generation.";
            return false;
        }

        var scope = new Dictionary<IParameterSymbol, string>(SymbolEqualityComparer.Default);
        foreach (var child in Enumerate(owner.Children))
        {
            if (child is CSharpOperation { TargetSymbol: AkcssMixinParameterSymbol parameter,
                    CSharpDefinition.Operation: IParameterReferenceOperation reference } &&
                locals.TryGetValue(parameter, out var local))
            {
                scope[reference.Parameter] = local;
            }
        }

        foreach (var part in operation.DescendantsAndSelf())
        {
            if (part is IParameterReferenceOperation parameter &&
                parameter.Parameter.ContainingSymbol.Name == "__AkburaSemanticProbe" &&
                !scope.ContainsKey(parameter.Parameter))
            {
                error = "This receiver-dependent value cannot be evaluated when a native style or configuration is constructed.";
                return false;
            }

            if (part is IInvocationOperation invocation && IsAmx(invocation.TargetMethod, model.Compilation))
            {
                error = "This Amx invocation has no supported native lowering in this expression.";
                return false;
            }
        }

        expression = Format(syntax, model, scope);
        return true;
    }

    public static string Format(CSharp.ExpressionSyntax expression, SemanticModel model,
        IReadOnlyDictionary<IParameterSymbol, string>? parameters = null)
    {
        var rewriter = AkcssFullyQualifiedExpressionRewriter.GetInstance(model, parameters);
        try
        {
            return rewriter.Visit(expression)!.WithoutTrivia().NormalizeWhitespace().ToFullString();
        }
        finally
        {
            rewriter.Free();
        }
    }

    internal static bool IsAmx(IMethodSymbol method, Microsoft.CodeAnalysis.Compilation compilation) =>
        compilation.GetTypeByMetadataName("Akbura.Amx") is { } amx &&
        amx.ContainingAssembly.Name == "Akbura" &&
        SymbolEqualityComparer.Default.Equals(method.ContainingType, amx);

    public static IEnumerable<NativeOperation> Enumerate(ImmutableArray<NativeOperation> operations)
    {
        foreach (var operation in operations)
        {
            yield return operation;
            foreach (var child in Enumerate(operation.Children))
            {
                yield return child;
            }
        }
    }
}

using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Immutable;
using System.Linq;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Akbura.Language;

internal abstract partial class AkburaSemanticModel
{
    private static ImmutableArray<IMethodSymbol> GetMarkupContentAddMethods(INamedTypeSymbol type)
    {
        using var methods = ImmutableArrayBuilder<IMethodSymbol>.Rent();
        for (var current = type; current != null; current = current.BaseType)
        {
            foreach (var method in current.GetMembers("Add").OfType<IMethodSymbol>())
            {
                if (!method.IsStatic && method.DeclaredAccessibility == Accessibility.Public &&
                    method.MethodKind == MethodKind.Ordinary && method.Arity == 0 &&
                    method.Parameters.Length == 1 && method.Parameters[0].RefKind == RefKind.None)
                {
                    methods.Add(method);
                }
            }
        }

        return methods.ToImmutable();
    }

    private ImmutableArray<MarkupChildContent> BindMarkupContentInsertions(
        MarkupContentModel model,
        INamedTypeSymbol? ownerType,
        ImmutableArray<MarkupChildContent> children,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnostics)
    {
        if (model.AddMethods.IsDefaultOrEmpty || ownerType == null)
        {
            return children;
        }

        using var result = ImmutableArrayBuilder<MarkupChildContent>.Rent();
        foreach (var child in children)
        {
            if (child.Kind == MarkupChildKind.Conditional)
            {
                // Each alternative has already selected overloads for its actual child types.
                result.Add(child);
                continue;
            }

            var ambiguous = false;
            var method = child.Type.Symbol is ITypeSymbol childType
                ? ResolveMarkupContentAddMethod(ownerType, childType, out ambiguous)
                : null;
            if (method != null)
            {
                result.Add(child.WithInsertionMethod(method));
                continue;
            }

            // Keep invalid children inspectable; code generation skips errorful content.
            result.Add(child);
            diagnostics.Add(new AkburaSemanticDiagnostic(
                child.Syntax,
                ambiguous ? ErrorCodes.AKBURA_SEMANTIC_MarkupContentAddMethodAmbiguous :
                    ErrorCodes.AKBURA_SEMANTIC_InvalidMarkupChild,
                [child.Type.ToDisplayString(), "an unambiguous public Add overload"]));
        }

        return result.ToImmutable();
    }

    private IMethodSymbol? ResolveMarkupContentAddMethod(
        INamedTypeSymbol ownerType,
        ITypeSymbol childType,
        out bool ambiguous)
    {
        // Ask ordinary C# overload resolution, including user-defined implicit conversions.
        var source = "sealed class __AkburaContentAddProbe { void __Probe(" +
            ownerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + " target, " +
            childType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
            " value) { target.Add(value); } }";
        var parseOptions = Compilation.CSharpCompilation.SyntaxTrees.FirstOrDefault()?.Options as CSharpParseOptions ??
            CSharpParseOptions.Default;
        var tree = CSharpSyntaxTree.ParseText(source, parseOptions);
        var compilation = Compilation.CSharpCompilation.AddSyntaxTrees(tree);
        var semantic = compilation.GetSemanticModel(tree);
        var invocation = tree.GetRoot().DescendantNodes().OfType<CSharp.InvocationExpressionSyntax>().Single();
        var symbol = semantic.GetSymbolInfo(invocation);
        ambiguous = symbol.CandidateReason == Microsoft.CodeAnalysis.CandidateReason.OverloadResolutionFailure &&
            semantic.GetDiagnostics(invocation.Span).Any(diagnostic => diagnostic.Id == "CS0121");
        return symbol.Symbol is IMethodSymbol { IsStatic: false, DeclaredAccessibility: Accessibility.Public } method &&
            method.Parameters.Length == 1 && method.Arity == 0 ? method : null;
    }
}

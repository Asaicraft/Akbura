using Akbura.Language.BoundTree;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;
using System.Linq;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using CSharpSyntaxKind = Microsoft.CodeAnalysis.CSharp.SyntaxKind;

namespace Akbura.Language.Binder;

internal sealed partial class CSharpProbeBinder
{
    internal UseHookProbeResult BindUseHookInvocation(
        AkburaSyntax syntax,
        CSharp.InvocationExpressionSyntax invocation,
        ImmutableArray<INamedTypeSymbol> hookTypes,
        bool injectSelf,
        bool rewritePropertyArguments)
    {
        var effectiveInvocation = injectSelf
            ? AddSyntheticSelf(invocation)
            : invocation;
        var probe = CreateUseHookProbe(syntax, effectiveInvocation, hookTypes);

        var hasPropertyArgumentSubstitution = false;
        if (rewritePropertyArguments)
        {
            var rewritten = RewritePropertyArguments(
                probe.SemanticModel,
                probe.Invocation);
            if (!rewritten.IsEquivalentTo(probe.Invocation))
            {
                hasPropertyArgumentSubstitution = true;
                effectiveInvocation = rewritten;
                probe = CreateUseHookProbe(syntax, effectiveInvocation, hookTypes);
            }
        }

        var bindingResult = BindExpression(
            probe.SemanticModel,
            probe.Invocation,
            isBindingPath: false);
        return new UseHookProbeResult(
            bindingResult,
            effectiveInvocation,
            BindInvocationArguments(
                syntax,
                probe.SemanticModel,
                probe.Invocation,
                isBindingPath: false),
            injectSelf,
            hasPropertyArgumentSubstitution);
    }

    private UseHookProbe CreateUseHookProbe(
        AkburaSyntax syntax,
        CSharp.InvocationExpressionSyntax invocation,
        ImmutableArray<INamedTypeSymbol> hookTypes)
    {
        var probeScope = CreateProbeScope(
            syntax,
            invocation,
            GetInvocationTargetIdentifierNames(invocation));
        var statement = CSharpSyntaxFactory.ExpressionStatement(invocation);
        var method = CSharpSyntaxFactory.MethodDeclaration(
                CSharpSyntaxFactory.PredefinedType(
                    CSharpSyntaxFactory.Token(CSharpSyntaxKind.VoidKeyword)),
                "__akbura_use_hook_probe")
            .WithBody(CreateProbeBlock(probeScope.LocalStatements, statement));
        var compilationUnit = CreateComponentProbeCompilationUnit(
            AddProbeMethod(probeScope.MemberDeclarations, method),
            "__AkburaUseHookProbe",
            CreateUseHookUsingDirectives(hookTypes));
        var syntaxTree = CreateSyntaxTree(compilationUnit);
        var semanticModel = CreateSemanticModel(syntaxTree);
        var probeInvocation = syntaxTree
            .GetCompilationUnitRoot()
            .DescendantNodes()
            .OfType<CSharp.MethodDeclarationSyntax>()
            .Single(candidate => candidate.Identifier.ValueText == "__akbura_use_hook_probe")
            .Body!
            .Statements
            .Last()
            .DescendantNodesAndSelf()
            .OfType<CSharp.InvocationExpressionSyntax>()
            .First();
        return new UseHookProbe(semanticModel, probeInvocation);
    }

    private static ImmutableArray<string> GetInvocationTargetIdentifierNames(
        CSharp.InvocationExpressionSyntax invocation)
    {
        using var builder = ImmutableArrayBuilder<string>.Rent();
        foreach (var identifier in invocation.Expression
                     .DescendantNodesAndSelf()
                     .OfType<CSharp.IdentifierNameSyntax>())
        {
            var name = identifier.Identifier.ValueText;
            if (name.Length != 0)
            {
                builder.Add(name);
            }
        }

        return builder.ToImmutable();
    }

    private ImmutableArray<CSharp.UsingDirectiveSyntax> CreateUseHookUsingDirectives(
        ImmutableArray<INamedTypeSymbol> hookTypes)
    {
        using var builder = ImmutableArrayBuilder<CSharp.UsingDirectiveSyntax>.Rent();
        builder.AddRange(SemanticModel.GetCSharpUsingDirectives());
        foreach (var hookType in hookTypes)
        {
            builder.Add(CSharpSyntaxFactory.UsingDirective(
                    CSharpSyntaxFactory.ParseName(hookType.ToDisplayString(
                        SymbolDisplayFormat.FullyQualifiedFormat)))
                .WithStaticKeyword(CSharpSyntaxFactory.Token(CSharpSyntaxKind.StaticKeyword)));
        }

        return builder.ToImmutable();
    }

    private static CSharp.InvocationExpressionSyntax AddSyntheticSelf(
        CSharp.InvocationExpressionSyntax invocation)
    {
        var arguments = invocation.ArgumentList.Arguments.Insert(
            0,
            CSharpSyntaxFactory.Argument(CSharpSyntaxFactory.ThisExpression()));
        return invocation.WithArgumentList(
            invocation.ArgumentList.WithArguments(arguments));
    }

    private static CSharp.InvocationExpressionSyntax RewritePropertyArguments(
        Microsoft.CodeAnalysis.SemanticModel semanticModel,
        CSharp.InvocationExpressionSyntax invocation)
    {
        var arguments = invocation.ArgumentList.Arguments;
        var rewritten = arguments;
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            var rewrittenExpression = RewritePropertyArgument(
                semanticModel,
                argument.Expression);
            if (rewrittenExpression.IsEquivalentTo(argument.Expression))
            {
                continue;
            }

            rewritten = rewritten.Replace(
                argument,
                argument.WithExpression(rewrittenExpression));
        }

        return rewritten == arguments
            ? invocation
            : invocation.WithArgumentList(invocation.ArgumentList.WithArguments(rewritten));
    }

    private static CSharp.ExpressionSyntax RewritePropertyArgument(
        Microsoft.CodeAnalysis.SemanticModel semanticModel,
        CSharp.ExpressionSyntax expression)
    {
        if (TryRewritePropertyExpression(
                semanticModel,
                expression,
                out var rewrittenExpression))
        {
            return rewrittenExpression;
        }

        if (expression is CSharp.CollectionExpressionSyntax collection)
        {
            return (CSharp.ExpressionSyntax)new AvaloniaPropertyCollectionRewriter(
                semanticModel).Visit(collection)!;
        }

        return expression;
    }

    private static bool TryRewritePropertyExpression(
        Microsoft.CodeAnalysis.SemanticModel semanticModel,
        CSharp.ExpressionSyntax expression,
        out CSharp.ExpressionSyntax rewrittenExpression)
    {
        if (semanticModel.GetSymbolInfo(expression).Symbol is IPropertySymbol property &&
            TryGetAvaloniaPropertyField(property, out var field))
        {
            rewrittenExpression = CSharpSyntaxFactory.ParseExpression(
                    field.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                    "." +
                    field.Name)
                .WithTriviaFrom(expression);
            return true;
        }

        rewrittenExpression = expression;
        return false;
    }

    private sealed class AvaloniaPropertyCollectionRewriter : CSharpSyntaxRewriter
    {
        private readonly Microsoft.CodeAnalysis.SemanticModel _semanticModel;

        public AvaloniaPropertyCollectionRewriter(
            Microsoft.CodeAnalysis.SemanticModel semanticModel)
        {
            _semanticModel = semanticModel;
        }

        public override SyntaxNode? VisitIdentifierName(CSharp.IdentifierNameSyntax node)
        {
            return TryRewritePropertyExpression(_semanticModel, node, out var rewritten)
                ? rewritten
                : base.VisitIdentifierName(node);
        }

        public override SyntaxNode? VisitMemberAccessExpression(
            CSharp.MemberAccessExpressionSyntax node)
        {
            return TryRewritePropertyExpression(_semanticModel, node, out var rewritten)
                ? rewritten
                : base.VisitMemberAccessExpression(node);
        }
    }

    private static bool TryGetAvaloniaPropertyField(
        IPropertySymbol property,
        out IFieldSymbol field)
    {
        for (var type = property.ContainingType; type != null; type = type.BaseType)
        {
            foreach (var member in type.GetMembers(property.Name + "Property"))
            {
                if (member is IFieldSymbol candidate &&
                    candidate.IsStatic &&
                    IsAvaloniaPropertyType(candidate.Type))
                {
                    field = candidate;
                    return true;
                }
            }
        }

        field = null!;
        return false;
    }

    private static bool IsAvaloniaPropertyType(ITypeSymbol type)
    {
        for (var current = type as INamedTypeSymbol; current != null; current = current.BaseType)
        {
            if (current.Name == "AvaloniaProperty" &&
                current.ContainingNamespace.ToDisplayString() == "Avalonia")
            {
                return true;
            }
        }

        return false;
    }

    private readonly struct UseHookProbe
    {
        public UseHookProbe(
            Microsoft.CodeAnalysis.SemanticModel semanticModel,
            CSharp.InvocationExpressionSyntax invocation)
        {
            SemanticModel = semanticModel;
            Invocation = invocation;
        }

        public Microsoft.CodeAnalysis.SemanticModel SemanticModel { get; }

        public CSharp.InvocationExpressionSyntax Invocation { get; }
    }
}

internal readonly struct UseHookProbeResult
{
    public UseHookProbeResult(
        CSharpBindingResult bindingResult,
        CSharp.InvocationExpressionSyntax effectiveInvocation,
        ImmutableArray<BoundExpression> effectiveArguments,
        bool hasSyntheticSelf,
        bool hasPropertyArgumentSubstitution)
    {
        BindingResult = bindingResult;
        EffectiveInvocation = effectiveInvocation;
        EffectiveArguments = effectiveArguments.IsDefault
            ? ImmutableArray<BoundExpression>.Empty
            : effectiveArguments;
        HasSyntheticSelf = hasSyntheticSelf;
        HasPropertyArgumentSubstitution = hasPropertyArgumentSubstitution;
    }

    public CSharpBindingResult BindingResult { get; }

    public CSharp.InvocationExpressionSyntax EffectiveInvocation { get; }

    public ImmutableArray<BoundExpression> EffectiveArguments { get; }

    public bool HasSyntheticSelf { get; }

    public bool HasPropertyArgumentSubstitution { get; }

    public IMethodSymbol? Method => BindingResult.Symbol as IMethodSymbol;
}

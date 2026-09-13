using Akbura.Language.BoundTree;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using CSharpSyntaxKind = Microsoft.CodeAnalysis.CSharp.SyntaxKind;
using IPropertySymbol = Microsoft.CodeAnalysis.IPropertySymbol;
#if STATS
using Akbura.Language.CodeGeneration;
#endif

namespace Akbura.Language.Binder;

internal sealed partial class CSharpProbeBinder
{
    internal UseHookProbeResult BindUseHookInvocation(
        AkburaSyntax syntax,
        CSharp.InvocationExpressionSyntax invocation,
        ImmutableArray<INamedTypeSymbol> hookTypes,
        bool injectSelf,
        bool rewritePropertyArguments,
        bool rewriteStateArguments = false)
    {
#if STATS
        using var measurement = GenerationStatistics.Measure(GenerationStatisticStage.CSharpProbeBinding);
#endif
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
        var stateArguments = ImmutableArray<UseHookStateArgument>.Empty;
        if (rewriteStateArguments && bindingResult.Symbol == null)
        {
            TryBindStateArguments(
                syntax,
                effectiveInvocation,
                hookTypes,
                ref probe,
                ref bindingResult,
                out stateArguments);
        }

        return new UseHookProbeResult(
            bindingResult,
            effectiveInvocation,
            BindInvocationArguments(
                syntax,
                probe.SemanticModel,
                probe.Invocation,
                isBindingPath: false),
            injectSelf,
            hasPropertyArgumentSubstitution,
            stateArguments);
    }

    private UseHookProbe CreateUseHookProbe(
        AkburaSyntax syntax,
        CSharp.InvocationExpressionSyntax invocation,
        ImmutableArray<INamedTypeSymbol> hookTypes,
        ImmutableArray<UseHookStateArgument> stateArguments = default)
    {
        var sourceInvocation = invocation;
        var probeScope = CreateUseHookProbeScope(
            syntax,
            invocation);
        if (!stateArguments.IsDefaultOrEmpty)
        {
            var stateType = Compilation.CSharpCompilation.GetTypeByMetadataName(
                "Akbura.ComponentTree.State`1")!;
            using var locals = ImmutableArrayBuilder<CSharp.StatementSyntax>.Rent();
            locals.AddRange(probeScope.LocalStatements);
            var arguments = invocation.ArgumentList.Arguments;
            foreach (var stateArgument in stateArguments)
            {
                var name = GetStateArgumentProbeName(stateArgument.ArgumentIndex);
                AddProbeLocal(
                    locals,
                    name,
                    new CSharpSymbolDefinition(stateType.Construct(
                        (ITypeSymbol)stateArgument.State.Type.Symbol!)),
                    stateArgument.State,
                    typeDisplayFormat: s_stateTypeDisplayFormat);
                var argument = arguments[stateArgument.ArgumentIndex];
                arguments = arguments.Replace(
                    argument,
                    argument.WithExpression(CSharpSyntaxFactory.IdentifierName(name)
                        .WithTriviaFrom(argument.Expression)));
            }

            invocation = invocation.WithArgumentList(
                invocation.ArgumentList.WithArguments(arguments));
            probeScope = new CSharpProbeScope(
                probeScope.MemberDeclarations,
                locals.ToImmutable());
        }

        var statement = CSharpSyntaxFactory.ExpressionStatement(invocation);
        var method = CSharpSyntaxFactory.MethodDeclaration(
                CSharpSyntaxFactory.PredefinedType(
                    CSharpSyntaxFactory.Token(CSharpSyntaxKind.VoidKeyword)),
                "__akbura_use_hook_probe")
            .WithBody(CreateProbeBlock(probeScope.LocalStatements, statement));
        var imports = CreateUseHookImports(sourceInvocation, hookTypes);
        var members = AddProbeMethod(probeScope.MemberDeclarations, method);
        var usingDirectives = CreateUseHookUsingDirectives(hookTypes);
        if (!imports.Types.IsDefaultOrEmpty)
        {
            members = members.AddRange(imports.Types);
            usingDirectives = usingDirectives.AddRange(imports.UsingDirectives);
        }

        var compilationUnit = CreateComponentProbeCompilationUnit(
            members,
            "__AkburaUseHookProbe",
            usingDirectives);
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
        var probe = new UseHookProbe(semanticModel, probeInvocation);
        return TryGetImportedHookInvocation(probe, sourceInvocation, imports.Methods, out var importedInvocation)
            ? CreateUseHookProbe(syntax, importedInvocation, hookTypes, stateArguments)
            : probe;
    }

    private CSharpProbeScope CreateUseHookProbeScope(
        AkburaSyntax syntax,
        CSharp.InvocationExpressionSyntax invocation)
    {
        var excludedNames = GetInvocationTargetIdentifierNames(invocation);
        var scope = CreateProbeScope(syntax, invocation, excludedNames);
        using var members = ImmutableArrayBuilder<CSharp.MemberDeclarationSyntax>.Rent();
        using var locals = ImmutableArrayBuilder<CSharp.StatementSyntax>.Rent();
        members.AddRange(scope.MemberDeclarations);
        foreach (var statement in scope.LocalStatements)
        {
            if (!statement.DescendantNodes().Any(node =>
                    node.HasAnnotations(StateCompletionAnnotationKind)))
            {
                locals.Add(statement);
            }
        }

        // Component state is a member. A lambda parameter may legally shadow it;
        // projecting state as a local would both hide the source and cause CS0136.
        var names = new HashSet<string>(excludedNames, StringComparer.Ordinal);
        var diagnostics = BindingDiagnosticBag.GetInstance();
        try
        {
            foreach (var identifier in invocation.DescendantNodes()
                         .OfType<CSharp.IdentifierNameSyntax>())
            {
                if (!names.Add(identifier.Identifier.ValueText) ||
                    Next?.LookupSymbol(
                        identifier.Identifier.ValueText,
                        BinderLookupOptions.None,
                        syntax,
                        diagnostics).Symbol is not IStateSymbol state ||
                    state.Type.Symbol is not ITypeSymbol type)
                {
                    continue;
                }

                members.Add(CreateProbeField(
                    CSharpSyntaxFactory.ParseTypeName(type.ToDisplayString(
                        s_stateTypeDisplayFormat)),
                    state.Name,
                    state));
            }
        }
        finally
        {
            diagnostics.Free();
        }

        return new CSharpProbeScope(members.ToImmutable(), locals.ToImmutable());
    }

    private void TryBindStateArguments(
        AkburaSyntax syntax,
        CSharp.InvocationExpressionSyntax invocation,
        ImmutableArray<INamedTypeSymbol> hookTypes,
        ref UseHookProbe probe,
        ref CSharpBindingResult bindingResult,
        out ImmutableArray<UseHookStateArgument> stateArguments)
    {
        stateArguments = ImmutableArray<UseHookStateArgument>.Empty;
        var originalProbe = probe;
        var hasValidCandidate = false;
        var seenArgumentSets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in originalProbe.SemanticModel
                     .GetMemberGroup(originalProbe.Invocation.Expression)
                     .OfType<IMethodSymbol>())
        {
            using var substitutions = ImmutableArrayBuilder<UseHookStateArgument>.Rent();
            var arguments = originalProbe.Invocation.ArgumentList.Arguments;
            for (var index = 0; index < arguments.Count; index++)
            {
                var argument = arguments[index];
                var parameter = argument.NameColon is { } named
                    ? candidate.Parameters.FirstOrDefault(parameter =>
                        parameter.Name == named.Name.Identifier.ValueText)
                    : index < candidate.Parameters.Length
                        ? candidate.Parameters[index]
                        : null;
                if (parameter is { RefKind: RefKind.None } &&
                    argument.RefKindKeyword.RawKind == 0 &&
                    parameter.Type is INamedTypeSymbol
                    {
                        Name: "State",
                        Arity: 1,
                    } parameterType &&
                    parameterType.ContainingNamespace.ToDisplayString() ==
                        "Akbura.ComponentTree" &&
                    TryGetStateArgument(
                        originalProbe.SemanticModel,
                        argument.Expression,
                        out var state))
                {
                    substitutions.Add(new UseHookStateArgument(index, state));
                }
            }

            var candidateArguments = substitutions.ToImmutable();
            if (candidateArguments.IsEmpty ||
                !seenArgumentSets.Add(string.Join(",", candidateArguments.Select(
                    static argument => argument.ArgumentIndex))))
            {
                continue;
            }

            var candidateProbe = CreateUseHookProbe(
                syntax,
                invocation,
                hookTypes,
                candidateArguments);
            var candidateBinding = BindExpression(
                candidateProbe.SemanticModel,
                candidateProbe.Invocation,
                isBindingPath: false);
            if (candidateBinding.Symbol is not IMethodSymbol)
            {
                if (!hasValidCandidate &&
                    candidateBinding.CandidateSymbols.Length != 0)
                {
                    probe = candidateProbe;
                    bindingResult = candidateBinding;
                    stateArguments = candidateArguments;
                }

                continue;
            }

            if (!candidateBinding.Diagnostics.IsEmpty)
            {
                if (!hasValidCandidate)
                {
                    probe = candidateProbe;
                    bindingResult = candidateBinding;
                    stateArguments = candidateArguments;
                }

                continue;
            }

            if (hasValidCandidate)
            {
                bindingResult = new CSharpBindingResult(
                    typeSymbol: null,
                    symbol: null,
                    receiverType: null,
                    isBindingPath: false,
                    [bindingResult.Symbol!, candidateBinding.Symbol],
                    Symbols.CandidateReason.Ambiguous,
                    operationDefinition: default);
                stateArguments = ImmutableArray<UseHookStateArgument>.Empty;
                return;
            }

            probe = candidateProbe;
            bindingResult = candidateBinding;
            stateArguments = candidateArguments;
            hasValidCandidate = true;
        }
    }

    private bool TryGetStateArgument(
        Microsoft.CodeAnalysis.SemanticModel semanticModel,
        CSharp.ExpressionSyntax expression,
        out IStateSymbol state)
    {
        while (expression is CSharp.ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }

        if (expression is CSharp.IdentifierNameSyntax &&
            semanticModel.GetSymbolInfo(expression).Symbol is IFieldSymbol field)
        {
            foreach (var reference in field.DeclaringSyntaxReferences)
            {
                foreach (var annotation in reference.GetSyntax()
                             .GetAnnotations(ProjectedSymbolAnnotationKind))
                {
                    if (!CSharpProbeSymbolOrigin.TryParse(annotation.Data, out var origin) ||
                        origin.Kind != Symbols.SymbolKind.State)
                    {
                        continue;
                    }

                    foreach (var declaration in SemanticModel.SyntaxTree.GetRoot()
                                 .Members.OfType<StateDeclarationSyntax>())
                    {
                        if (declaration.Name.Span == origin.DeclarationSpan &&
                            SemanticModel.GetDeclaredSymbol(declaration) is IStateSymbol source)
                        {
                            state = source;
                            return true;
                        }
                    }
                }
            }
        }

        state = null!;
        return false;
    }

    private static string GetStateArgumentProbeName(int index) =>
        "__akbura_state_argument_" + index.ToString(
            System.Globalization.CultureInfo.InvariantCulture);

    private static readonly SymbolDisplayFormat s_stateTypeDisplayFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions |
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

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
        bool hasPropertyArgumentSubstitution,
        ImmutableArray<UseHookStateArgument> stateArguments = default)
    {
        BindingResult = bindingResult;
        EffectiveInvocation = effectiveInvocation;
        EffectiveArguments = effectiveArguments.IsDefault
            ? ImmutableArray<BoundExpression>.Empty
            : effectiveArguments;
        HasSyntheticSelf = hasSyntheticSelf;
        HasPropertyArgumentSubstitution = hasPropertyArgumentSubstitution;
        StateArguments = stateArguments.IsDefault
            ? ImmutableArray<UseHookStateArgument>.Empty
            : stateArguments;
    }

    public CSharpBindingResult BindingResult { get; }

    public CSharp.InvocationExpressionSyntax EffectiveInvocation { get; }

    public ImmutableArray<BoundExpression> EffectiveArguments { get; }

    public bool HasSyntheticSelf { get; }

    public bool HasPropertyArgumentSubstitution { get; }

    public ImmutableArray<UseHookStateArgument> StateArguments { get; }

    public IMethodSymbol? Method => BindingResult.Symbol as IMethodSymbol;
}

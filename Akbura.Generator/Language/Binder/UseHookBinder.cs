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
using AkburaSyntaxKind = Akbura.Language.Syntax.SyntaxKind;

namespace Akbura.Language.Binder;

internal sealed class UseHookBinder : Binder
{
    private const string UseHookAttributeMetadataName =
        "Akbura.CompilerAnotations.UseHookAttribute";
    private const string SelfAttributeMetadataName =
        "Akbura.CompilerAnotations.SelfAttribute";

    private readonly CSharpProbeBinder _probeBinder;

    public UseHookBinder(
        AkburaSemanticModel semanticModel,
        Binder next,
        AkburaBinderFlags flags = AkburaBinderFlags.None)
        : base(
            semanticModel,
            next,
            declaration: null,
            next.ScopeDesignator,
            flags)
    {
        _probeBinder = new CSharpProbeBinder(semanticModel, this, flags);
    }

    public BoundStatement BindStatement(
        CSharpStatementSyntax syntax,
        CSharp.StatementSyntax statement)
    {
        if (statement is CSharp.ExpressionStatementSyntax
            {
                Expression: CSharp.InvocationExpressionSyntax invocation,
            })
        {
            if (syntax.Parent?.Kind == AkburaSyntaxKind.CSharpBlockSyntax &&
                IsUseHookInvocation(syntax, invocation, out var nestedInvocationName))
            {
                return ReplaceDiagnostics(
                    _probeBinder.BindStatement(syntax, statement),
                    ImmutableArray.Create(CreateMustBeTopLevelDiagnostic(
                        syntax,
                        nestedInvocationName)));
            }

            var wasRecognized = TryBindInvocation(
                syntax,
                invocation,
                UseHookContext.Render,
                out var boundInvocation,
                out _,
                out var diagnostics);
            if (wasRecognized)
            {
                return boundInvocation != null
                    ? new BoundUseHookStatement(
                        syntax,
                        this,
                        boundInvocation,
                        diagnostics)
                    : ReplaceDiagnostics(
                        _probeBinder.BindStatement(syntax, statement),
                        diagnostics);
            }
        }

        var boundStatement = _probeBinder.BindStatement(syntax, statement);
        var nestedDiagnostic = FindNestedUseHookDiagnostic(syntax, statement);
        return nestedDiagnostic == null
            ? boundStatement
            : ReplaceDiagnostics(boundStatement, ImmutableArray.Create(nestedDiagnostic));
    }

    public UseHookInitializerBinding BindStateInitializer(
        StateDeclarationSyntax declaration,
        CSharp.ExpressionSyntax expression)
    {
        if (expression is CSharp.InvocationExpressionSyntax invocation)
        {
            var wasRecognized = TryBindInvocation(
                declaration.Initializer,
                invocation,
                UseHookContext.StateInitializer,
                out var boundInvocation,
                out var stateType,
                out var diagnostics);
            if (wasRecognized)
            {
                return new UseHookInitializerBinding(
                    wasRecognized: true,
                    boundInvocation,
                    stateType,
                    diagnostics);
            }
        }

        var nestedDiagnostic = FindNestedUseHookDiagnostic(
            declaration.Initializer,
            expression);
        return nestedDiagnostic == null
            ? UseHookInitializerBinding.None
            : new UseHookInitializerBinding(
                wasRecognized: true,
                invocation: null,
                stateType: null,
                ImmutableArray.Create(nestedDiagnostic));
    }

    private bool TryBindInvocation(
        AkburaSyntax syntax,
        CSharp.InvocationExpressionSyntax invocation,
        UseHookContext context,
        out BoundUseHookInvocation? boundInvocation,
        out ITypeSymbol? stateType,
        out ImmutableArray<AkburaSemanticDiagnostic> diagnostics)
    {
        boundInvocation = null;
        stateType = null;
        diagnostics = ImmutableArray<AkburaSemanticDiagnostic>.Empty;

        if (!TryGetInvocationName(invocation, out var invocationName))
        {
            return false;
        }

        var hookTypes = GetVisibleHookTypes(invocationName);
        var canBindQualifiedInvocation = invocation.Expression is CSharp.MemberAccessExpressionSyntax;
        if (hookTypes.IsDefaultOrEmpty && !canBindQualifiedInvocation)
        {
            return false;
        }

        UseHookProbeResult bestProbe = default;
        var bestProbeScore = int.MinValue;
        var hasBestProbe = false;
        foreach (var attempt in s_attempts)
        {
            var probe = _probeBinder.BindUseHookInvocation(
                syntax,
                invocation,
                hookTypes,
                attempt.InjectSelf,
                attempt.RewritePropertyArguments);
            var probeScore = GetProbeScore(
                hookTypes,
                invocationName,
                invocation.ArgumentList.Arguments.Count,
                attempt,
                probe);
            if (!hasBestProbe || probeScore > bestProbeScore)
            {
                bestProbe = probe;
                bestProbeScore = probeScore;
                hasBestProbe = true;
            }

            var method = probe.Method;
            if (method == null)
            {
                continue;
            }

            if (!HasAttribute(method, UseHookAttributeMetadataName))
            {
                if (!attempt.InjectSelf && !attempt.RewritePropertyArguments)
                {
                    return false;
                }

                continue;
            }

            if (!TryValidateHookMethod(method, out var selfParameter, out var failure))
            {
                diagnostics = ImmutableArray.Create(CreateInvalidDeclarationDiagnostic(
                    syntax,
                    invocationName,
                    failure));
                return true;
            }

            if (attempt.InjectSelf && selfParameter == null)
            {
                continue;
            }

            if (!attempt.InjectSelf &&
                selfParameter != null &&
                invocation.ArgumentList.Arguments.Count == method.Parameters.Length - 1)
            {
                continue;
            }

            if (!TryValidateContext(method, context, out stateType))
            {
                diagnostics = ImmutableArray.Create(CreateInvalidContextDiagnostic(
                    syntax,
                    invocationName));
                return true;
            }

            var selfKind = selfParameter == null
                ? UseHookSelfKind.None
                : attempt.InjectSelf
                    ? UseHookSelfKind.Implicit
                    : UseHookSelfKind.Explicit;
            var hook = new UseHookSymbol(
                invocationName,
                method,
                selfParameter,
                selfKind);
            diagnostics = CreateProbeDiagnostics(syntax, invocation, probe.BindingResult);
            var nestedDiagnostics = CreateNestedUseHookDiagnostics(syntax, invocation);
            if (!nestedDiagnostics.IsDefaultOrEmpty)
            {
                diagnostics = diagnostics.AddRange(nestedDiagnostics);
            }
            boundInvocation = new BoundUseHookInvocation(
                syntax,
                this,
                hook,
                invocation,
                probe.EffectiveInvocation,
                probe.BindingResult,
                probe.EffectiveArguments,
                probe.HasSyntheticSelf,
                probe.HasPropertyArgumentSubstitution,
                diagnostics);
            return true;
        }

        if (!hasBestProbe || hookTypes.IsDefaultOrEmpty)
        {
            return false;
        }

        if (TryGetInvalidHookDeclaration(
                hookTypes,
                invocationName,
                out var invalidDeclarationFailure))
        {
            diagnostics = ImmutableArray.Create(CreateInvalidDeclarationDiagnostic(
                syntax,
                invocationName,
                invalidDeclarationFailure));
            return true;
        }

        diagnostics = CreateProbeDiagnostics(syntax, invocation, bestProbe.BindingResult);
        return true;
    }

    private static int GetProbeScore(
        ImmutableArray<INamedTypeSymbol> hookTypes,
        string invocationName,
        int sourceArgumentCount,
        UseHookBindingAttempt attempt,
        UseHookProbeResult probe)
    {
        var effectiveArgumentCount = sourceArgumentCount + (attempt.InjectSelf ? 1 : 0);
        var matchesCallShape = false;
        foreach (var hookType in hookTypes)
        {
            foreach (var member in hookType.GetMembers(invocationName))
            {
                if (member is not IMethodSymbol method ||
                    !HasAttribute(method, UseHookAttributeMetadataName))
                {
                    continue;
                }

                var hasSelf = method.Parameters.Any(parameter =>
                    HasAttribute(parameter, SelfAttributeMetadataName));
                if (attempt.InjectSelf != hasSelf ||
                    !CanAcceptArgumentCount(method, effectiveArgumentCount))
                {
                    continue;
                }

                matchesCallShape = true;
                break;
            }

            if (matchesCallShape)
            {
                break;
            }
        }

        var score = matchesCallShape ? 100 : 0;
        score += Math.Min(probe.BindingResult.CandidateSymbols.Length, 5) * 10;
        if (probe.HasPropertyArgumentSubstitution)
        {
            score++;
        }

        return score;
    }

    private static bool CanAcceptArgumentCount(IMethodSymbol method, int argumentCount)
    {
        var requiredCount = 0;
        foreach (var parameter in method.Parameters)
        {
            if (!parameter.IsOptional && !parameter.IsParams)
            {
                requiredCount++;
            }
        }

        if (argumentCount < requiredCount)
        {
            return false;
        }

        return method.Parameters.LastOrDefault()?.IsParams == true ||
            argumentCount <= method.Parameters.Length;
    }

    private static bool TryGetInvalidHookDeclaration(
        ImmutableArray<INamedTypeSymbol> hookTypes,
        string invocationName,
        out string failure)
    {
        failure = string.Empty;
        var hasAttributedMethod = false;
        foreach (var hookType in hookTypes)
        {
            foreach (var member in hookType.GetMembers(invocationName))
            {
                if (member is not IMethodSymbol method ||
                    !HasAttribute(method, UseHookAttributeMetadataName))
                {
                    continue;
                }

                hasAttributedMethod = true;
                if (TryValidateHookMethod(method, out _, out var candidateFailure))
                {
                    return false;
                }

                if (failure.Length == 0)
                {
                    failure = candidateFailure;
                }
            }
        }

        return hasAttributedMethod;
    }

    private ImmutableArray<INamedTypeSymbol> GetVisibleHookTypes(string methodName)
    {
        using var builder = ImmutableArrayBuilder<INamedTypeSymbol>.Rent();
        var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        var currentNamespace = SemanticModel.GetAkburaNamespaceText(
            SemanticModel.SyntaxTree.GetRoot(),
            SemanticModel.SyntaxTree);
        AddNamespaceHookTypes(currentNamespace, methodName, seen, builder);

        foreach (var usingDirective in SemanticModel.GetCSharpUsingDirectives())
        {
            AddUsingHookTypes(usingDirective, methodName, seen, builder);
        }

        foreach (var syntaxTree in Compilation.CSharpCompilation.SyntaxTrees)
        {
            foreach (var usingDirective in syntaxTree.GetCompilationUnitRoot().Usings)
            {
                if (usingDirective.GlobalKeyword.RawKind != 0)
                {
                    AddUsingHookTypes(usingDirective, methodName, seen, builder);
                }
            }
        }

        return builder.ToImmutable();
    }

    private void AddUsingHookTypes(
        CSharp.UsingDirectiveSyntax usingDirective,
        string methodName,
        HashSet<INamedTypeSymbol> seen,
        ImmutableArrayBuilder<INamedTypeSymbol> builder)
    {
        if (usingDirective.Alias != null || usingDirective.Name == null)
        {
            return;
        }

        var name = NormalizeQualifiedName(usingDirective.Name.ToString());
        if (usingDirective.StaticKeyword.RawKind != 0)
        {
            var type = Compilation.CSharpCompilation.GetTypeByMetadataName(name);
            if (type != null)
            {
                AddTypeIfItContainsHook(type, methodName, seen, builder);
            }

            return;
        }

        AddNamespaceHookTypes(name, methodName, seen, builder);
    }

    private static void AddNamespaceHookTypes(
        INamespaceSymbol? namespaceSymbol,
        string methodName,
        HashSet<INamedTypeSymbol> seen,
        ImmutableArrayBuilder<INamedTypeSymbol> builder)
    {
        if (namespaceSymbol == null)
        {
            return;
        }

        foreach (var type in namespaceSymbol.GetTypeMembers())
        {
            AddTypeIfItContainsHook(type, methodName, seen, builder);
        }
    }

    private static void AddTypeIfItContainsHook(
        INamedTypeSymbol type,
        string methodName,
        HashSet<INamedTypeSymbol> seen,
        ImmutableArrayBuilder<INamedTypeSymbol> builder)
    {
        foreach (var member in type.GetMembers(methodName))
        {
            if (member is IMethodSymbol method &&
                HasAttribute(method, UseHookAttributeMetadataName))
            {
                if (seen.Add(type))
                {
                    builder.Add(type);
                }

                break;
            }
        }

        foreach (var nestedType in type.GetTypeMembers())
        {
            AddTypeIfItContainsHook(nestedType, methodName, seen, builder);
        }
    }

    private void AddNamespaceHookTypes(
        string namespaceName,
        string methodName,
        HashSet<INamedTypeSymbol> seen,
        ImmutableArrayBuilder<INamedTypeSymbol> builder)
    {
        var csharpCompilation = Compilation.CSharpCompilation;
        AddNamespaceHookTypes(
            ResolveNamespace(csharpCompilation.GlobalNamespace, namespaceName),
            methodName,
            seen,
            builder);
        AddNamespaceHookTypes(
            ResolveNamespace(csharpCompilation.Assembly.GlobalNamespace, namespaceName),
            methodName,
            seen,
            builder);

        foreach (var reference in csharpCompilation.References)
        {
            var globalNamespace = csharpCompilation.GetAssemblyOrModuleSymbol(reference) switch
            {
                IAssemblySymbol assembly => assembly.GlobalNamespace,
                IModuleSymbol module => module.GlobalNamespace,
                _ => null,
            };
            if (globalNamespace == null)
            {
                continue;
            }

            AddNamespaceHookTypes(
                ResolveNamespace(globalNamespace, namespaceName),
                methodName,
                seen,
                builder);
        }
    }

    private static INamespaceSymbol? ResolveNamespace(
        INamespaceSymbol rootNamespace,
        string namespaceName)
    {
        if (string.IsNullOrWhiteSpace(namespaceName))
        {
            return rootNamespace;
        }

        var current = rootNamespace;
        foreach (var segment in NormalizeQualifiedName(namespaceName)
                     .Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries))
        {
            INamespaceSymbol? nextNamespace = null;
            foreach (var candidate in current.GetNamespaceMembers())
            {
                if (candidate.Name == segment)
                {
                    nextNamespace = candidate;
                    break;
                }
            }

            if (nextNamespace == null)
            {
                return null;
            }

            current = nextNamespace;
        }

        return current;
    }

    private AkburaSemanticDiagnostic? FindNestedUseHookDiagnostic(
        AkburaSyntax syntax,
        SyntaxNode node)
    {
        foreach (var invocation in node.DescendantNodesAndSelf()
                     .OfType<CSharp.InvocationExpressionSyntax>())
        {
            if (!IsUseHookInvocation(syntax, invocation, out var invocationName))
            {
                continue;
            }

            return CreateMustBeTopLevelDiagnostic(syntax, invocationName);
        }

        return null;
    }

    private ImmutableArray<AkburaSemanticDiagnostic> CreateNestedUseHookDiagnostics(
        AkburaSyntax syntax,
        CSharp.InvocationExpressionSyntax rootInvocation)
    {
        foreach (var invocation in rootInvocation.DescendantNodes()
                     .OfType<CSharp.InvocationExpressionSyntax>())
        {
            if (IsUseHookInvocation(syntax, invocation, out var invocationName))
            {
                return ImmutableArray.Create(
                    CreateMustBeTopLevelDiagnostic(syntax, invocationName));
            }
        }

        return ImmutableArray<AkburaSemanticDiagnostic>.Empty;
    }

    private bool IsUseHookInvocation(
        AkburaSyntax syntax,
        CSharp.InvocationExpressionSyntax invocation,
        out string invocationName)
    {
        if (!TryGetInvocationName(invocation, out invocationName))
        {
            return false;
        }

        var hookTypes = GetVisibleHookTypes(invocationName);
        if (!hookTypes.IsDefaultOrEmpty)
        {
            return true;
        }

        if (invocation.Expression is not CSharp.MemberAccessExpressionSyntax)
        {
            return false;
        }

        foreach (var attempt in s_attempts)
        {
            var probe = _probeBinder.BindUseHookInvocation(
                syntax,
                invocation,
                hookTypes,
                attempt.InjectSelf,
                attempt.RewritePropertyArguments);
            if (probe.Method != null &&
                HasAttribute(probe.Method, UseHookAttributeMetadataName))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryValidateHookMethod(
        IMethodSymbol method,
        out IParameterSymbol? selfParameter,
        out string failure)
    {
        selfParameter = null;
        failure = string.Empty;
        if (!method.IsStatic || method.DeclaredAccessibility != Accessibility.Public)
        {
            failure = "the method must be public and static";
            return false;
        }

        for (var index = 0; index < method.Parameters.Length; index++)
        {
            var parameter = method.Parameters[index];
            if (!HasAttribute(parameter, SelfAttributeMetadataName))
            {
                continue;
            }

            if (selfParameter != null)
            {
                failure = "only one parameter may be annotated with [Self]";
                return false;
            }

            if (index != 0)
            {
                failure = "the [Self] parameter must be first";
                return false;
            }

            if (parameter.RefKind != RefKind.None)
            {
                failure = "the [Self] parameter cannot be ref, in, or out";
                return false;
            }

            selfParameter = parameter;
        }

        return true;
    }

    private static bool TryValidateContext(
        IMethodSymbol method,
        UseHookContext context,
        out ITypeSymbol? stateType)
    {
        stateType = null;
        if (context == UseHookContext.Render)
        {
            return method.ReturnsVoid;
        }

        if (method.ReturnType is not INamedTypeSymbol
            {
                Name: "State",
                Arity: 1,
            } state ||
            state.ContainingNamespace.ToDisplayString() != "Akbura.ComponentTree")
        {
            return false;
        }

        stateType = state.TypeArguments[0];
        return true;
    }

    private static bool TryGetInvocationName(
        CSharp.InvocationExpressionSyntax invocation,
        out string name)
    {
        CSharp.SimpleNameSyntax? simpleName = invocation.Expression switch
        {
            CSharp.SimpleNameSyntax simple => simple,
            CSharp.MemberAccessExpressionSyntax memberAccess => memberAccess.Name,
            _ => null,
        };
        name = simpleName?.Identifier.ValueText ?? string.Empty;
        return name.Length != 0;
    }

    private static bool HasAttribute(
        Microsoft.CodeAnalysis.ISymbol symbol,
        string metadataName)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() == metadataName)
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeQualifiedName(string name)
    {
        name = name.Trim();
        return name.StartsWith("global::", StringComparison.Ordinal)
            ? name["global::".Length..]
            : name;
    }

    private static ImmutableArray<AkburaSemanticDiagnostic> CreateProbeDiagnostics(
        AkburaSyntax syntax,
        CSharp.InvocationExpressionSyntax invocation,
        CSharpBindingResult bindingResult)
    {
        using var builder = ImmutableArrayBuilder<AkburaSemanticDiagnostic>.Rent();
        AkburaSemanticModel.AddCSharpBindingDiagnostics(
            syntax,
            invocation.ToFullString().Trim(),
            bindingResult,
            builder);
        return builder.ToImmutable();
    }

    private static AkburaSemanticDiagnostic CreateMustBeTopLevelDiagnostic(
        AkburaSyntax syntax,
        string invocationName)
    {
        return new AkburaSemanticDiagnostic(
            syntax,
            ErrorCodes.AKBURA_SEMANTIC_UseHookMustBeTopLevel,
            [invocationName]);
    }

    private static AkburaSemanticDiagnostic CreateInvalidDeclarationDiagnostic(
        AkburaSyntax syntax,
        string invocationName,
        string failure)
    {
        return new AkburaSemanticDiagnostic(
            syntax,
            ErrorCodes.AKBURA_SEMANTIC_UseHookInvalidDeclaration,
            [invocationName, failure]);
    }

    private static AkburaSemanticDiagnostic CreateInvalidContextDiagnostic(
        AkburaSyntax syntax,
        string invocationName)
    {
        return new AkburaSemanticDiagnostic(
            syntax,
            ErrorCodes.AKBURA_SEMANTIC_UseHookInvalidContext,
            [invocationName]);
    }

    private static BoundStatement AddDiagnostics(
        BoundStatement statement,
        ImmutableArray<AkburaSemanticDiagnostic> additionalDiagnostics)
    {
        if (additionalDiagnostics.IsDefaultOrEmpty)
        {
            return statement;
        }

        var diagnostics = statement.Diagnostics.AddRange(additionalDiagnostics);
        return statement switch
        {
            BoundCSharpStatement csharpStatement => new BoundCSharpStatement(
                csharpStatement.Syntax,
                csharpStatement.Binder,
                csharpStatement.BindingResult,
                diagnostics,
                csharpStatement.Children),
            BoundLocalDeclarationStatement localDeclaration => new BoundLocalDeclarationStatement(
                localDeclaration.Syntax,
                localDeclaration.Binder,
                localDeclaration.BindingResult,
                localDeclaration.Locals,
                localDeclaration.Initializers,
                diagnostics),
            _ => statement,
        };
    }

    private static BoundStatement ReplaceDiagnostics(
        BoundStatement statement,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics)
    {
        return statement switch
        {
            BoundCSharpStatement csharpStatement => new BoundCSharpStatement(
                csharpStatement.Syntax,
                csharpStatement.Binder,
                csharpStatement.BindingResult,
                diagnostics,
                csharpStatement.Children),
            BoundLocalDeclarationStatement localDeclaration => new BoundLocalDeclarationStatement(
                localDeclaration.Syntax,
                localDeclaration.Binder,
                localDeclaration.BindingResult,
                localDeclaration.Locals,
                localDeclaration.Initializers,
                diagnostics),
            _ => statement,
        };
    }

    private static readonly ImmutableArray<UseHookBindingAttempt> s_attempts =
    [
        new(InjectSelf: false, RewritePropertyArguments: false),
        new(InjectSelf: false, RewritePropertyArguments: true),
        new(InjectSelf: true, RewritePropertyArguments: false),
        new(InjectSelf: true, RewritePropertyArguments: true),
    ];

    private enum UseHookContext : byte
    {
        Render,
        StateInitializer,
    }

    private readonly record struct UseHookBindingAttempt(
        bool InjectSelf,
        bool RewritePropertyArguments);
}

internal readonly struct UseHookInitializerBinding
{
    public static UseHookInitializerBinding None { get; } = new(
        wasRecognized: false,
        invocation: null,
        stateType: null,
        diagnostics: ImmutableArray<AkburaSemanticDiagnostic>.Empty);

    public UseHookInitializerBinding(
        bool wasRecognized,
        BoundUseHookInvocation? invocation,
        ITypeSymbol? stateType,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics)
    {
        WasRecognized = wasRecognized;
        Invocation = invocation;
        StateType = stateType;
        Diagnostics = diagnostics.IsDefault
            ? ImmutableArray<AkburaSemanticDiagnostic>.Empty
            : diagnostics;
    }

    public bool WasRecognized { get; }

    public BoundUseHookInvocation? Invocation { get; }

    public IUseHookSymbol? Symbol => Invocation?.Hook;

    public ITypeSymbol? StateType { get; }

    public ImmutableArray<AkburaSemanticDiagnostic> Diagnostics { get; }
}

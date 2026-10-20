using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
#if STATS
using Akbura.Language.CodeGeneration;
using System.Security.Cryptography;
using System.Text;
#endif

namespace Akbura.Language.Binder;

/// <summary>
/// Preparation owned by one document semantic model, never by reusable snapshot
/// state. Only type probes with exactly matching text and imports share binding
/// results. Markup expression results require the complete prepared probe (including
/// scopes and imports) and the exact target type. No cache crosses document owners.
/// </summary>
internal sealed class CSharpProbeContext(AkburaSemanticModel semanticModel)
{
    private readonly ConcurrentDictionary<TypeProbeKey, CSharpBindingResult> _types = new();
    private readonly ConcurrentDictionary<ExpressionProbeKey, CSharpBindingResult> _expressions = new();
    private readonly ConcurrentDictionary<string, CSharp.CompilationUnitSyntax> _componentTemplates = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Symbols.ISymbol, CSharpProbeScope> _symbols = new(SymbolReferenceComparer.Instance);
    private readonly ConditionalWeakTable<SemanticModel, CSharpProbeDiagnostics> _diagnostics = new();
    private readonly ConcurrentDictionary<CSharpStatementSyntax, MethodSyntax> _methodSignatures = new();
    private readonly ConcurrentDictionary<CSharpStatementSyntax, MethodSyntax> _methodBodies = new();
    private readonly ConcurrentDictionary<CSharpStatementSyntax, MethodSyntax> _capturedFunctions = new();
    private ImmutableArray<CSharp.UsingDirectiveSyntax> _defaultUsings;
    private ImmutableArray<ImportKey> _defaultImportKeys;

    internal ImmutableArray<CSharp.UsingDirectiveSyntax> DefaultUsings
    {
        get
        {
            if (_defaultUsings.IsDefault)
            {
                ImmutableInterlocked.InterlockedInitialize(ref _defaultUsings, semanticModel.GetCSharpUsingDirectivesCore());
            }

            return _defaultUsings;
        }
    }

    internal CSharpBindingResult BindType(CSharp.TypeSyntax type, ImmutableArray<CSharp.UsingDirectiveSyntax> usings,
        Func<CSharpBindingResult> bind)
    {
        var effectiveUsings = usings.IsDefault ? DefaultUsings : usings;
        ImmutableArray<ImportKey> imports;
        if (!_defaultUsings.IsDefault && effectiveUsings == _defaultUsings)
        {
            if (_defaultImportKeys.IsDefault)
            {
                ImmutableInterlocked.InterlockedInitialize(ref _defaultImportKeys,
                    effectiveUsings.Select(static directive => new ImportKey(directive, directive.ToFullString())).ToImmutableArray());
            }

            imports = _defaultImportKeys;
        }
        else
        {
            imports = effectiveUsings.Select(static directive => new ImportKey(directive, directive.ToFullString())).ToImmutableArray();
        }

        var key = new TypeProbeKey(type, type.ToFullString(), imports);
        if (_types.TryGetValue(key, out var result))
        {
#if STATS
            GenerationStatistics.Increment(GenerationStatisticCounter.ProbeRequest);
            GenerationStatistics.Increment(GenerationStatisticCounter.ProbeBindingResultReused);
#endif
            return result;
        }

        // Compute before publication: exceptions and cancellation never leave an entry.
        result = bind();
        return _types.GetOrAdd(key, result);
    }

    internal CSharpBindingResult BindMarkupExpression(CSharp.CompilationUnitSyntax projection, ITypeSymbol? targetType,
        Func<CSharpBindingResult> bind)
    {
#if STATS
        GenerationStatistics.Increment(GenerationStatisticCounter.MarkupExpressionRequest);
#endif
        // Recovery and directives can carry information not represented by ordinary
        // syntax equivalence. Keep their original binding path, including diagnostics.
        if (projection.ContainsDiagnostics || projection.ContainsSkippedText || projection.ContainsDirectives)
        {
#if STATS
            GenerationStatistics.Increment(GenerationStatisticCounter.MarkupExpressionCacheDisallowed);
            RecordExpressionReuse(projection, "Disallowed", "SyntaxRecoveryOrDirectives");
#endif
            return bind();
        }

        var query = CSharpProbeBuilder.GetReturnProbeExpression(projection);
        var key = new ExpressionProbeKey(projection, projection.ToFullString(), targetType, query?.Span ?? default);
        if (_expressions.TryGetValue(key, out var result))
        {
#if STATS
            GenerationStatistics.Increment(GenerationStatisticCounter.MarkupExpressionCacheHit);
            RecordExpressionReuse(projection, "Reused", "ExactPreparedProjectionAndTarget");
#endif
            return result;
        }
#if STATS
        GenerationStatistics.Increment(GenerationStatisticCounter.MarkupExpressionCacheMiss);
        RecordExpressionReuse(projection, "Miss", "CachedResultUnavailable");
#endif
        // A failed or canceled factory publishes nothing. Raw Roslyn diagnostic
        // locations are probe-relative; presentation is remapped by each caller.
        result = bind();
        return result.Diagnostics.IsEmpty ? _expressions.GetOrAdd(key, result) : result;
    }

#if STATS
    private void RecordExpressionReuse(CSharp.CompilationUnitSyntax projection, string decision, string reason)
    {
        if (!GenerationStatistics.IsInvalidationTrackingEnabled)
        {
            return;
        }
        using var hash = SHA256.Create();
        var digest = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(projection.ToFullString()))).Replace("-", "");
        GenerationStatistics.RecordInvalidation(new GenerationInvalidationEvent("MarkupExpressionReuse", semanticModel.SyntaxTree.FilePath,
            decision, [reason], ImmutableDictionary<string, string>.Empty.Add("ProjectionSha256", digest)));
    }
#endif

    internal CSharp.CompilationUnitSyntax CreateComponentCompilationUnit(CSharpProbeBinder binder,
        ImmutableArray<CSharp.MemberDeclarationSyntax> members, string fallbackTypeName,
        ImmutableArray<CSharp.UsingDirectiveSyntax> usings)
    {
        if (!_componentTemplates.TryGetValue(fallbackTypeName, out var template))
        {
            template = binder.CreateComponentProbeCompilationUnitCore(
                ImmutableArray<CSharp.MemberDeclarationSyntax>.Empty, fallbackTypeName, ImmutableArray<CSharp.UsingDirectiveSyntax>.Empty);
            template = _componentTemplates.GetOrAdd(fallbackTypeName, template);
        }
        else
        {
#if STATS
            GenerationStatistics.Increment(GenerationStatisticCounter.ProbeContextReused);
#endif
        }

        var type = template.DescendantNodes().OfType<CSharp.ClassDeclarationSyntax>().Single();
        var result = template.ReplaceNode(type, type.WithMembers(Microsoft.CodeAnalysis.CSharp.SyntaxFactory.List(members)));
        return result.WithUsings(Microsoft.CodeAnalysis.CSharp.SyntaxFactory.List(usings.IsDefault ? DefaultUsings : usings));
    }

    internal CSharpProbeScope GetSymbolScope(Symbols.ISymbol symbol)
    {
        if (_symbols.TryGetValue(symbol, out var scope))
        {
#if STATS
            GenerationStatistics.Increment(GenerationStatisticCounter.ProbeContextReused);
#endif
            return scope;
        }

        scope = CSharpProbeBinder.CreateSymbolProbeScope(symbol);
        return _symbols.GetOrAdd(symbol, scope);
    }

    internal ImmutableArray<Diagnostic> GetDiagnostics(SemanticModel model, Microsoft.CodeAnalysis.Text.TextSpan span) =>
        _diagnostics.GetValue(model, static value => new CSharpProbeDiagnostics(value)).GetDiagnostics(span);

    internal CSharp.MethodDeclarationSyntax? GetMethodSignature(CSharpStatementSyntax syntax)
    {
        if (_methodSignatures.TryGetValue(syntax, out var cached))
        {
#if STATS
            GenerationStatistics.Increment(GenerationStatisticCounter.ProbeContextReused);
#endif
            return cached.Node as CSharp.MethodDeclarationSyntax;
        }

        CSharpProbeBinder.TryCreateComponentMethodProbe(syntax, out var method);
        return _methodSignatures.GetOrAdd(syntax, new MethodSyntax(method)).Node as CSharp.MethodDeclarationSyntax;
    }

    internal CSharp.MethodDeclarationSyntax? GetMethodBody(CSharpStatementSyntax syntax) =>
        _methodBodies.GetOrAdd(syntax, static statement =>
        {
            CSharp.MethodDeclarationSyntax? method = null;
            try
            {
                method = Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseMemberDeclaration(statement.ToFullString())
                    as CSharp.MethodDeclarationSyntax;
            }
            catch (ArgumentException)
            {
            }

            return new MethodSyntax(method);
        }).Node as CSharp.MethodDeclarationSyntax;

    internal CSharp.LocalFunctionStatementSyntax? GetCapturedFunction(CSharpStatementSyntax syntax) =>
        _capturedFunctions.GetOrAdd(syntax, static statement => new MethodSyntax(
            Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseStatement(statement.ToFullString())
                as CSharp.LocalFunctionStatementSyntax)).Node as CSharp.LocalFunctionStatementSyntax;

    private sealed record MethodSyntax(SyntaxNode? Node);

    private readonly record struct ExpressionProbeKey(CSharp.CompilationUnitSyntax Projection, string Text, ITypeSymbol? TargetType,
        Microsoft.CodeAnalysis.Text.TextSpan QuerySpan)
    {
        public bool Equals(ExpressionProbeKey other) => ReferenceEquals(TargetType, other.TargetType) && QuerySpan == other.QuerySpan && Text == other.Text &&
            Microsoft.CodeAnalysis.CSharp.SyntaxFactory.AreEquivalent(Projection, other.Projection);

        public override int GetHashCode() => unchecked((StringComparer.Ordinal.GetHashCode(Text) * 397) ^
            (TargetType == null ? 0 : RuntimeHelpers.GetHashCode(TargetType)) ^ QuerySpan.GetHashCode());
    }

    private readonly record struct ImportKey(CSharp.UsingDirectiveSyntax Syntax, string Text)
    {
        public bool Equals(ImportKey other) => Text == other.Text && Microsoft.CodeAnalysis.CSharp.SyntaxFactory.AreEquivalent(Syntax, other.Syntax);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Text);
    }

    private readonly record struct TypeProbeKey(CSharp.TypeSyntax Type, string Text, ImmutableArray<ImportKey> Usings)
    {
        public bool Equals(TypeProbeKey other) => Text == other.Text &&
            Microsoft.CodeAnalysis.CSharp.SyntaxFactory.AreEquivalent(Type, other.Type) &&
            (Usings == other.Usings || Usings.SequenceEqual(other.Usings));

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = StringComparer.Ordinal.GetHashCode(Text);
                foreach (var directive in Usings)
                {
                    hash = (hash * 397) ^ directive.GetHashCode();
                }

                return hash;
            }
        }
    }

    private sealed class SymbolReferenceComparer : IEqualityComparer<Symbols.ISymbol>
    {
        internal static readonly SymbolReferenceComparer Instance = new();
        public bool Equals(Symbols.ISymbol? left, Symbols.ISymbol? right) => ReferenceEquals(left, right);
        public int GetHashCode(Symbols.ISymbol symbol) => RuntimeHelpers.GetHashCode(symbol);
    }
}

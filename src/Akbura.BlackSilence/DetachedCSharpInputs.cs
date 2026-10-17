using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;

namespace Akbura.BlackSilence;

/// <summary>
/// Exact scalar inputs, not a signature hash. Only the explicitly isolated leaf
/// declaration subset may differ. All other tree contents remain exact inputs.
/// </summary>
internal sealed record DetachedCSharpInputs(
    string AssemblyName,
    long OptionsIdentity,
    ImmutableArray<long> References,
    ImmutableArray<DetachedCSharpTree> Trees,
    bool Supported)
{
    internal static DetachedCSharpInputs Create(CSharpCompilation compilation, ArtifactIdentitySession identities, CancellationToken cancellationToken)
    {
        var options = compilation.Options;
        var supported = compilation.ScriptCompilationInfo == null && options.SyntaxTreeOptionsProvider == null &&
            options.MetadataReferenceResolver == null && options.SourceReferenceResolver == null &&
            options.XmlReferenceResolver == null && options.StrongNameProvider == null &&
            ReferenceEquals(options.AssemblyIdentityComparer, AssemblyIdentityComparer.Default);
        var references = ImmutableArray.CreateBuilder<long>();
        foreach (var reference in compilation.References)
        {
            cancellationToken.ThrowIfCancellationRequested();
            supported &= reference is PortableExecutableReference;
            references.Add(identities.GetIdentity(reference));
        }
        var trees = ImmutableArray.CreateBuilder<DetachedCSharpTree>();
        foreach (var tree in compilation.SyntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var root = tree.GetRoot(cancellationToken);
            supported &= !root.ContainsAnnotations && tree.Options.Kind == SourceCodeKind.Regular;
            trees.Add(DetachedCSharpTree.Create(tree, root, identities, cancellationToken));
        }
        return new DetachedCSharpInputs(compilation.AssemblyName ?? string.Empty, identities.GetIdentity(options),
            references.ToImmutable(), trees.ToImmutable(), supported);
    }

    internal ArtifactValidation Validate(DetachedCSharpInputs other)
    {
        if (!Supported || !other.Supported)
        {
            return new(ArtifactReuseDecision.UnsupportedDependency, "UnsupportedCompilerInput", [], []);
        }
        if (AssemblyName != other.AssemblyName || OptionsIdentity != other.OptionsIdentity ||
            !References.SequenceEqual(other.References) || Trees.Length != other.Trees.Length)
        {
            return new(ArtifactReuseDecision.RequiresRebinding, "CompilerOptionsReferencesOrTreeSetChanged", [], []);
        }
        var names = ImmutableArray.CreateBuilder<string>();
        var paths = ImmutableArray.CreateBuilder<string>();
        for (var index = 0; index < Trees.Length; index++)
        {
            var old = Trees[index];
            var current = other.Trees[index];
            if (old.Path != current.Path || old.ParseIdentity != current.ParseIdentity || old.EncodingIdentity != current.EncodingIdentity)
            {
                return new(ArtifactReuseDecision.RequiresRebinding, "TreePathOrderOrParseOptionsChanged", [], []);
            }
            if (old.Source == current.Source)
            {
                continue;
            }
            if (old.IsolatedType.Length == 0 || old.IsolatedType != current.IsolatedType ||
                !old.DeclarationTokens.SequenceEqual(current.DeclarationTokens, StringComparer.Ordinal))
            {
                return new(ArtifactReuseDecision.UnsupportedDependency, "ChangedDeclarationOutsideSupportedSubset", [], []);
            }
            // Include negative/candidate lookup space: no other source tree may
            // mention this type, through aliases, partials, using-static or calls.
            if (Trees.Where((_, position) => position != index).Any(tree => tree.Identifiers.Contains(old.IsolatedType)) ||
                other.Trees.Where((_, position) => position != index).Any(tree => tree.Identifiers.Contains(old.IsolatedType)))
            {
                return new(ArtifactReuseDecision.UnsupportedDependency, "TypeReferencedByOtherCSharpInput", [], []);
            }
            names.Add(current.IsolatedType);
            paths.Add(current.Path);
        }
        return new(ArtifactReuseDecision.ValidatedEquivalent, "IsolatedLeafInputsEquivalent", names.ToImmutable(), paths.ToImmutable());
    }
}

internal readonly record struct ArtifactValidation(
    ArtifactReuseDecision Decision,
    string Reason,
    ImmutableArray<string> ChangedTypes,
    ImmutableArray<string> ChangedPaths);

internal sealed record DetachedCSharpTree(
    string Path,
    string Source,
    long ParseIdentity,
    long EncodingIdentity,
    string IsolatedType,
    ImmutableArray<string> DeclarationTokens,
    ImmutableArray<string> Identifiers)
{
    internal static DetachedCSharpTree Create(SyntaxTree tree, SyntaxNode root, ArtifactIdentitySession identities, CancellationToken cancellationToken)
    {
        var name = string.Empty;
        var declaration = ImmutableArray<string>.Empty;
        if (TryGetLeaf(root, out var type, out var method))
        {
            name = type!.Identifier.ValueText;
            var body = (SyntaxNode?)method!.Body ?? method.ExpressionBody!;
            declaration = root.DescendantTokens().Where(token => !body.FullSpan.Contains(token.Span))
                .Select(static token => token.RawKind + ":" + token.Text).ToImmutableArray();
        }
        var identifiers = root.DescendantTokens().Where(static token => token.IsKind(SyntaxKind.IdentifierToken))
            .Select(static token => token.ValueText).Distinct(StringComparer.Ordinal).ToImmutableArray();
        return new DetachedCSharpTree(tree.FilePath, tree.GetText(cancellationToken).ToString(),
            identities.GetIdentity(tree.Options), identities.GetIdentity(tree.Encoding), name, declaration, identifiers);
    }

    private static bool TryGetLeaf(SyntaxNode root, out ClassDeclarationSyntax? type, out MethodDeclarationSyntax? method)
    {
        type = null;
        method = null;
        if (root is not CompilationUnitSyntax unit || unit.ContainsDiagnostics || unit.ContainsDirectives || unit.ContainsAnnotations ||
            unit.Usings.Count != 0 || unit.Externs.Count != 0 || unit.AttributeLists.Count != 0 || unit.Members.Count != 1)
        {
            return false;
        }
        MemberDeclarationSyntax member = unit.Members[0];
        if (member is BaseNamespaceDeclarationSyntax space)
        {
            if (space.Usings.Count != 0 || space.Externs.Count != 0 || space.Members.Count != 1)
            {
                return false;
            }
            member = space.Members[0];
        }
        if (member is not ClassDeclarationSyntax candidate || candidate.Modifiers.Count != 2 ||
            !candidate.Modifiers.Any(SyntaxKind.InternalKeyword) || !candidate.Modifiers.Any(SyntaxKind.StaticKeyword) ||
            candidate.AttributeLists.Count != 0 || candidate.TypeParameterList != null || candidate.BaseList != null ||
            candidate.ConstraintClauses.Count != 0 || candidate.Members.Count != 1 ||
            candidate.Members[0] is not MethodDeclarationSyntax leaf || leaf.AttributeLists.Count != 0 ||
            leaf.TypeParameterList != null || leaf.ConstraintClauses.Count != 0 || leaf.ExplicitInterfaceSpecifier != null ||
            leaf.ParameterList.Parameters.Count != 0 || leaf.Modifiers.Count != 2 || !leaf.Modifiers.Any(SyntaxKind.StaticKeyword) ||
            !leaf.Modifiers.Any(token => token.IsKind(SyntaxKind.PublicKeyword) || token.IsKind(SyntaxKind.InternalKeyword) || token.IsKind(SyntaxKind.PrivateKeyword)) ||
            leaf.ReturnType is not PredefinedTypeSyntax result || !result.Keyword.IsKind(SyntaxKind.IntKeyword))
        {
            return false;
        }
        var expression = leaf.ExpressionBody?.Expression ??
            (leaf.Body?.Statements.Count == 1 && leaf.Body.Statements[0] is ReturnStatementSyntax statement ? statement.Expression : null);
        if (expression is not LiteralExpressionSyntax literal || !literal.IsKind(SyntaxKind.NumericLiteralExpression) ||
            literal.Token.Value is not int || candidate.Identifier.ValueText.Length == 0)
        {
            return false;
        }
        type = candidate;
        method = leaf;
        return true;
    }
}

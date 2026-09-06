using Akbura.Language;
using Akbura.Language.CodeGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;

namespace Akbura.BlackSilence;

/// <summary>
/// Conservative generation environment, not a public-signature or method-body cache.
/// Only entire trees containing a provably unused empty internal class are omitted.
/// All other C# text, references, options and namespace introductions remain inputs.
/// Recompute this snapshot when document dependency names change, not just when C# changes.
/// </summary>
internal sealed class CSharpEnvironmentSnapshot
{
    private readonly ImmutableArray<MetadataReference> _references;
    private readonly ImmutableArray<ParseOptions> _parseOptions;
    private readonly ImmutableArray<string> _namespaces;

    private CSharpEnvironmentSnapshot(
        CSharpCompilation compilation,
        ImmutableArray<SyntaxTree> retainedSyntaxTrees,
        ImmutableArray<MetadataReference> references,
        ImmutableArray<ParseOptions> parseOptions,
        ImmutableArray<string> namespaces,
        int ignoredSyntaxTreeCount)
    {
        Compilation = compilation;
        RetainedSyntaxTrees = retainedSyntaxTrees;
        _references = references;
        _parseOptions = parseOptions;
        _namespaces = namespaces;
        IgnoredSyntaxTreeCount = ignoredSyntaxTreeCount;
    }

    // A comparer may retain a previous equivalent snapshot, including its compilation.
    // The omitted declarations are then guaranteed unused by the current documents.
    public CSharpCompilation Compilation { get; }

    public ImmutableArray<SyntaxTree> RetainedSyntaxTrees { get; }

    public int IgnoredSyntaxTreeCount { get; }

    public static CSharpEnvironmentSnapshot Create(
        CSharpCompilation compilation,
        ImmutableArray<DocumentSyntaxVersion> documents,
        CancellationToken cancellationToken = default)
    {
        return Create(compilation, documents, new GeneratorProjectOptions(string.Empty, string.Empty), cancellationToken);
    }

    public static CSharpEnvironmentSnapshot Create(
        CSharpCompilation compilation,
        ImmutableArray<DocumentSyntaxVersion> documents,
        GeneratorProjectOptions options,
        CancellationToken cancellationToken = default)
    {
        if (compilation == null)
        {
            throw new ArgumentNullException(nameof(compilation));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var referencedNames = new HashSet<string>(StringComparer.Ordinal);
        var namespaces = new HashSet<string>(StringComparer.Ordinal);
        var candidates = new Dictionary<SyntaxTree, InertTypeCandidate>();
        var parseOptions = new HashSet<ParseOptions>();
        var retainedTrees = ImmutableArray.CreateBuilder<SyntaxTree>();
        var references = compilation.References.ToImmutableArray();
        var canIgnoreTrees = compilation.ScriptCompilationInfo == null;

        // AKCSS carriers and component namespaces can introduce names which never
        // occur in AdditionalFiles text. Use the same naming rules as generation.
        AddIdentifiers(AkcssGeneratedModuleNames.GetNamespaceName(options.RootNamespace), referencedNames, cancellationToken);

        foreach (var document in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            canIgnoreTrees &= document.CanReuse;
            referencedNames.Add(document.SyntaxTree.ComponentName);
            referencedNames.Add(Path.GetFileNameWithoutExtension(document.FilePath));

            if (document.SyntaxTree is ComponentSyntaxTree)
            {
                var generatedNamespace = AkburaComponentProbeCompilationBuilder.GetNamespaceName(
                    document.SyntaxTree,
                    options.RootNamespace,
                    options.ProjectDirectory);
                AddIdentifiers(generatedNamespace, referencedNames, cancellationToken);
            }

            foreach (var dependency in document.DependencyNames)
            {
                if (dependency.Kind == DocumentDependencyKind.Identifier)
                {
                    referencedNames.Add(dependency.Name);
                }
                else
                {
                    AddIdentifiers(dependency.Name, referencedNames, cancellationToken);
                }
            }
        }

        foreach (var tree in compilation.SyntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            parseOptions.Add(tree.Options);
            var root = tree.GetRoot(cancellationToken);
            AddNamespaces(root, namespaces, cancellationToken);

            if (canIgnoreTrees && TryGetInertType(tree, root, out var candidate))
            {
                candidates.Add(tree, candidate);
            }
            else
            {
                AddIdentifiers(root, referencedNames, cancellationToken);
            }
        }

        foreach (var namespaceName in namespaces)
        {
            AddIdentifiers(namespaceName, referencedNames, cancellationToken);
        }

        var nonConflictingCandidates = new HashSet<SyntaxTree>();

        foreach (var pair in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (namespaces.Contains(pair.Value.MetadataName) ||
                HasReferenceCollision(compilation, references, pair.Value.MetadataName, cancellationToken))
            {
                referencedNames.Add(pair.Value.Name);
            }
            else
            {
                nonConflictingCandidates.Add(pair.Key);
            }
        }

        var ignoredCount = 0;

        foreach (var tree in compilation.SyntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (candidates.TryGetValue(tree, out var candidate) &&
                nonConflictingCandidates.Contains(tree) &&
                !IsReferencedName(candidate.Name, referencedNames))
            {
                ignoredCount++;
                continue;
            }

            retainedTrees.Add(tree);
        }

        return new CSharpEnvironmentSnapshot(
            compilation,
            retainedTrees.ToImmutable(),
            references,
            parseOptions.ToImmutableArray(),
            namespaces.OrderBy(static name => name, StringComparer.Ordinal).ToImmutableArray(),
            ignoredCount);
    }

    internal bool HasSameEnvironment(CSharpEnvironmentSnapshot other)
    {
        if (!string.Equals(Compilation.AssemblyName, other.Compilation.AssemblyName, StringComparison.Ordinal) ||
            !Compilation.Options.Equals(other.Compilation.Options) ||
            !ReferenceEquals(Compilation.ScriptCompilationInfo, other.Compilation.ScriptCompilationInfo) ||
            _references.Length != other._references.Length ||
            _parseOptions.Length != other._parseOptions.Length ||
            _namespaces.Length != other._namespaces.Length ||
            RetainedSyntaxTrees.Length != other.RetainedSyntaxTrees.Length)
        {
            return false;
        }

        for (var i = 0; i < _references.Length; i++)
        {
            if (!ReferenceEquals(_references[i], other._references[i]))
            {
                return false;
            }
        }

        foreach (var parseOptions in _parseOptions)
        {
            if (!other._parseOptions.Contains(parseOptions))
            {
                return false;
            }
        }

        for (var i = 0; i < _namespaces.Length; i++)
        {
            if (!string.Equals(_namespaces[i], other._namespaces[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        for (var i = 0; i < RetainedSyntaxTrees.Length; i++)
        {
            var left = RetainedSyntaxTrees[i];
            var right = other.RetainedSyntaxTrees[i];

            if (!ReferenceEquals(left, right) &&
                (!string.Equals(left.FilePath, right.FilePath, StringComparison.Ordinal) ||
                 !left.Options.Equals(right.Options) ||
                 !left.GetText().ContentEquals(right.GetText())))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryGetInertType(SyntaxTree tree, SyntaxNode root, out InertTypeCandidate candidate)
    {
        candidate = default;

        if (tree.Options.Kind != SourceCodeKind.Regular ||
            root.ContainsDiagnostics || root.ContainsDirectives || root.ContainsSkippedText ||
            root is not CompilationUnitSyntax unit ||
            unit.AttributeLists.Count != 0 || unit.Externs.Count != 0 || unit.Usings.Count != 0 ||
            unit.Members.Count != 1)
        {
            return false;
        }

        MemberDeclarationSyntax member = unit.Members[0];
        var namespaceName = string.Empty;

        while (member is BaseNamespaceDeclarationSyntax namespaceDeclaration)
        {
            if (namespaceDeclaration.Externs.Count != 0 || namespaceDeclaration.Usings.Count != 0 ||
                namespaceDeclaration.Members.Count != 1)
            {
                return false;
            }

            namespaceName = JoinName(namespaceName, GetNamespaceName(namespaceDeclaration));
            member = namespaceDeclaration.Members[0];
        }

        if (member is not ClassDeclarationSyntax declaration ||
            declaration.AttributeLists.Count != 0 || declaration.TypeParameterList != null ||
            declaration.ParameterList != null || declaration.BaseList != null ||
            declaration.ConstraintClauses.Count != 0 || declaration.Members.Count != 0 ||
            declaration.OpenBraceToken.IsMissing || declaration.OpenBraceToken.RawKind == 0 ||
            declaration.CloseBraceToken.IsMissing || declaration.CloseBraceToken.RawKind == 0)
        {
            return false;
        }

        var modifierKinds = new HashSet<int>();

        foreach (var modifier in declaration.Modifiers)
        {
            if (modifier.Kind() is not (SyntaxKind.InternalKeyword or SyntaxKind.SealedKeyword) ||
                !modifierKinds.Add(modifier.RawKind))
            {
                return false;
            }
        }

        var name = declaration.Identifier.ValueText;

        // Module carrier names and the generated namespace are implicit, not necessarily
        // written in AdditionalFiles. They are never eligible for this optimization.
        if (name.Length == 0 || name.StartsWith("__Akbura", StringComparison.Ordinal) || name == "Generated")
        {
            return false;
        }

        candidate = new InertTypeCandidate(name, JoinName(namespaceName, name));
        return true;
    }

    private static bool IsReferencedName(string name, HashSet<string> referencedNames)
    {
        return referencedNames.Contains(name) ||
            referencedNames.Contains(name + "Attribute") ||
            (name.EndsWith("Attribute", StringComparison.Ordinal) &&
             referencedNames.Contains(name.Substring(0, name.Length - "Attribute".Length)));
    }

    private static bool HasReferenceCollision(
        CSharpCompilation compilation,
        ImmutableArray<MetadataReference> references,
        string metadataName,
        CancellationToken cancellationToken)
    {
        foreach (var reference in references)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var symbol = compilation.GetAssemblyOrModuleSymbol(reference);
            var assembly = symbol as IAssemblySymbol;
            var globalNamespace = assembly?.GlobalNamespace ?? (symbol as IModuleSymbol)?.GlobalNamespace;

            if (globalNamespace == null)
            {
                return true;
            }

            INamespaceSymbol? current = globalNamespace;
            var segments = metadataName.Split('.');

            for (var i = 0; i < segments.Length && current != null; i++)
            {
                var segment = segments[i];

                if (i == segments.Length - 1 && !current.GetTypeMembers(segment).IsEmpty)
                {
                    return true;
                }

                current = current.GetNamespaceMembers().FirstOrDefault(candidate => candidate.Name == segment);

                if (current == null)
                {
                    break;
                }
            }

            if (current != null)
            {
                return true;
            }
        }

        return false;
    }

    private static void AddNamespaces(
        SyntaxNode root,
        HashSet<string> namespaces,
        CancellationToken cancellationToken)
    {
        foreach (var declaration in root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullName = string.Empty;

            foreach (var ancestor in declaration.AncestorsAndSelf().OfType<BaseNamespaceDeclarationSyntax>().Reverse())
            {
                fullName = JoinName(fullName, GetNamespaceName(ancestor));
            }

            var current = string.Empty;

            foreach (var segment in fullName.Split('.'))
            {
                current = JoinName(current, segment);
                namespaces.Add(current);
            }
        }
    }

    private static string GetNamespaceName(BaseNamespaceDeclarationSyntax declaration)
    {
        return string.Join(".", declaration.Name.DescendantTokens()
            .Where(static token => token.IsKind(SyntaxKind.IdentifierToken))
            .Select(static token => token.ValueText));
    }

    private static string JoinName(string parent, string name)
    {
        return parent.Length == 0 ? name : parent + "." + name;
    }

    private static void AddIdentifiers(
        SyntaxNode root,
        HashSet<string> identifiers,
        CancellationToken cancellationToken)
    {
        foreach (var token in root.DescendantTokens(descendIntoTrivia: true))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (token.IsKind(SyntaxKind.IdentifierToken))
            {
                identifiers.Add(token.ValueText);
            }
        }
    }

    private static void AddIdentifiers(
        string text,
        HashSet<string> identifiers,
        CancellationToken cancellationToken)
    {
        foreach (var token in SyntaxFactory.ParseTokens(text))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (token.IsKind(SyntaxKind.IdentifierToken))
            {
                identifiers.Add(token.ValueText);
            }
        }
    }

    private readonly record struct InertTypeCandidate(string Name, string MetadataName);
}

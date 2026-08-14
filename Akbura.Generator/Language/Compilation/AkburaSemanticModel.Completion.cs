using Akbura.Language.Binder;
using Akbura.Language.Symbols;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;
using System.Diagnostics;
using MarkupElementSyntax = Akbura.Language.Syntax.MarkupElementSyntax;
using MarkupRootSyntax = Akbura.Language.Syntax.MarkupRootSyntax;

namespace Akbura.Language;

internal partial class AkburaSemanticModel
{
    private readonly object _completionComponentsGate = new();
    private CompletionComponentCatalog? _completionComponentCatalog;
    private readonly object _completionMarkupExtensionsGate = new();
    private CompletionMarkupExtensionCatalog? _completionMarkupExtensionCatalog;
    private readonly object _completionTailwindUtilitiesGate = new();
    private readonly Dictionary<
        string,
        ImmutableArray<TailwindUtilityLookupCandidate>>
        _completionTailwindUtilities = new(StringComparer.Ordinal);

    internal ImmutableArray<MarkupComponentLookupCandidate>
        LookupMarkupComponents(CancellationToken cancellationToken = default)
    {
        var catalog = Volatile.Read(
            ref _completionComponentCatalog);
        if (catalog != null)
        {
            return catalog.Candidates;
        }

        var candidates = ComputeMarkupComponents(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_completionComponentsGate)
        {
            catalog = _completionComponentCatalog;
            if (catalog == null)
            {
                catalog = new CompletionComponentCatalog(candidates);
                Volatile.Write(
                    ref _completionComponentCatalog,
                    catalog);
            }
        }

        return catalog.Candidates;
    }

    internal ImmutableArray<MarkupExtensionLookupCandidate>
        LookupMarkupExtensions(CancellationToken cancellationToken = default)
    {
        var catalog = Volatile.Read(
            ref _completionMarkupExtensionCatalog);
        if (catalog != null)
        {
            return catalog.Candidates;
        }

        var candidates = ComputeMarkupExtensions(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_completionMarkupExtensionsGate)
        {
            catalog = _completionMarkupExtensionCatalog;
            if (catalog == null)
            {
                catalog = new CompletionMarkupExtensionCatalog(
                    candidates);
                Volatile.Write(
                    ref _completionMarkupExtensionCatalog,
                    catalog);
            }
        }

        return catalog.Candidates;
    }

    internal ImmutableArray<TailwindUtilityLookupCandidate>
        LookupTailwindUtilities(
            string componentName,
            CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(componentName))
        {
            return ImmutableArray<TailwindUtilityLookupCandidate>.Empty;
        }

        lock (_completionTailwindUtilitiesGate)
        {
            if (_completionTailwindUtilities.TryGetValue(
                    componentName,
                    out var cached))
            {
                return cached;
            }
        }

        var candidates = ComputeTailwindUtilities(
            componentName,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_completionTailwindUtilitiesGate)
        {
            if (_completionTailwindUtilities.TryGetValue(
                    componentName,
                    out var cached))
            {
                return cached;
            }

            _completionTailwindUtilities.Add(
                componentName,
                candidates);
            return candidates;
        }
    }

    private ImmutableArray<TailwindUtilityLookupCandidate>
        ComputeTailwindUtilities(
            string componentName,
            CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryResolveMarkupComponentForCompletion(
                componentName,
                out var component))
        {
            return ImmutableArray<TailwindUtilityLookupCandidate>.Empty;
        }

        var markupSyntax = SyntaxTree.GetRootSyntax()
            .DescendantNodesAndSelf()
            .FirstOrDefault(static syntax =>
                syntax is MarkupElementSyntax or MarkupRootSyntax);
        if (markupSyntax == null)
        {
            return ImmutableArray<TailwindUtilityLookupCandidate>.Empty;
        }

        for (var binder = BindingSession.GetBinder(markupSyntax);
             binder != null;
             binder = binder.Next)
        {
            if (binder is MarkupBinder markupBinder)
            {
                return markupBinder
                    .LookupTailwindUtilitiesForCompletion(
                        component,
                        cancellationToken);
            }
        }

        return ImmutableArray<TailwindUtilityLookupCandidate>.Empty;
    }

    private ImmutableArray<MarkupExtensionLookupCandidate>
        ComputeMarkupExtensions(CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        var visibleNamespaces = GetVisibleMarkupExtensionNamespaces();
        var candidates = new Dictionary<
            string,
            MarkupExtensionLookupCandidate>(StringComparer.Ordinal);
        var csharpCompilation = Compilation.CSharpCompilation;

        foreach (var visibleNamespace in visibleNamespaces)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var namespaceSymbol = GetNamespaceSymbol(
                csharpCompilation.GlobalNamespace,
                visibleNamespace.Name);
            if (namespaceSymbol == null)
            {
                continue;
            }

            foreach (var type in namespaceSymbol.GetTypeMembers()
                         .OrderBy(
                             static type =>
                                 HasMarkupExtensionSuffix(type.Name)
                                     ? 1
                                     : 0))
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddMarkupExtensionCandidate(
                    candidates,
                    csharpCompilation,
                    type,
                    visibleNamespace.Alias);
            }
        }

        AddMarkupExtensionTypeAliasCandidates(
            candidates,
            csharpCompilation,
            cancellationToken);

        var result = candidates.Values
            .OrderBy(
                static candidate => candidate.DisplayName,
                StringComparer.Ordinal)
            .ToImmutableArray();
        AkburaWorkspaceDiagnostics.WriteCompletionElapsed(
            "Markup extension catalog",
            timer.Elapsed);
        return result;
    }

    private ImmutableArray<MarkupComponentLookupCandidate>
        ComputeMarkupComponents(CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        var visibleNamespaces = GetVisibleMarkupNamespaces();
        AkburaWorkspaceDiagnostics.WriteCompletionElapsed(
            "Component catalog visible namespaces",
            timer.Elapsed);

        timer.Restart();
        var candidates = new Dictionary<
            string,
            MarkupComponentLookupCandidate>(StringComparer.Ordinal);
        var csharpCompilation = Compilation.CSharpProbeCompilation;
        var akburaControlType = csharpCompilation.GetTypeByMetadataName(
            "Akbura.AkburaControl");
        AkburaWorkspaceDiagnostics.WriteCompletionElapsed(
            "Component catalog probe compilation",
            timer.Elapsed);

        timer.Restart();

        foreach (var visibleNamespace in visibleNamespaces)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var namespaceSymbol = GetNamespaceSymbol(
                csharpCompilation.GlobalNamespace,
                visibleNamespace.Name);
            if (namespaceSymbol == null)
            {
                continue;
            }

            foreach (var type in namespaceSymbol.GetTypeMembers())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var isAkburaComponent = IsDerivedFromOrEqual(
                    type,
                    akburaControlType);
                if (!IsCompletableMarkupComponent(
                        csharpCompilation,
                        type,
                        isAkburaComponent))
                {
                    continue;
                }

                var displayName = visibleNamespace.Alias == null
                    ? type.Name
                    : visibleNamespace.Alias + "::" + type.Name;
                AddMarkupComponentCandidate(
                    candidates,
                    displayName,
                    GetTypeMetadataName(type),
                    type,
                    isAkburaComponent);
            }
        }

        foreach (var syntaxTree in Compilation.SyntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddVisibleAkburaComponentCandidates(
                candidates,
                visibleNamespaces,
                GetAkburaComponentMetadataName(syntaxTree),
                csharpCompilation);
        }

        foreach (var reference in Compilation.CompilationReferences)
        {
            foreach (var metadataName in
                     reference.GetComponentMetadataNames(
                         cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddVisibleAkburaComponentCandidates(
                    candidates,
                    visibleNamespaces,
                    metadataName,
                    csharpCompilation);
            }
        }

        var result = candidates.Values
            .OrderBy(
                static candidate => candidate.DisplayName,
                StringComparer.Ordinal)
            .ToImmutableArray();
        AkburaWorkspaceDiagnostics.WriteCompletionElapsed(
            "Component catalog namespace members",
            timer.Elapsed);
        return result;
    }

    internal bool TryResolveMarkupComponentForCompletion(
        string componentName,
        out IMarkupComponentSymbol component)
    {
        if (string.IsNullOrWhiteSpace(componentName))
        {
            component = null!;
            return false;
        }

        var name = componentName.Trim();
        foreach (var metadataName in
                 GetAkburaComponentCandidateMetadataNames(name))
        {
            var akburaComponent = FindLocalAkburaComponent(metadataName) ??
                Compilation
                    .GetReferencedComponentSymbols(metadataName)
                    .FirstOrDefault();
            var componentType = Compilation.CSharpProbeCompilation
                .GetTypeByMetadataName(metadataName);
            if (akburaComponent != null || componentType != null)
            {
                component = CreateMarkupComponentLookupSymbol(
                    name,
                    componentType ?? akburaComponent?.ComponentType,
                    akburaComponent);
                return true;
            }
        }

        CSharpBindingResult binding;
        try
        {
            binding = BindCSharpType(
                SyntaxFactory.ParseTypeName(name));
        }
        catch (InvalidOperationException)
        {
            component = null!;
            return false;
        }

        if (TryGetMarkupComponentType(binding, out var boundType))
        {
            var metadataName = GetTypeMetadataName(boundType);
            var akburaComponent = FindLocalAkburaComponent(metadataName) ??
                Compilation
                    .GetReferencedComponentSymbols(metadataName)
                    .FirstOrDefault();
            component = CreateMarkupComponentLookupSymbol(
                name,
                boundType,
                akburaComponent);
            return true;
        }

        component = null!;
        return false;
    }

    private IAkburaComponentSymbol? FindLocalAkburaComponent(
        string metadataName)
    {
        foreach (var syntaxTree in Compilation.SyntaxTrees)
        {
            if (ReferenceEquals(syntaxTree, SyntaxTree) ||
                !string.Equals(
                    GetAkburaComponentMetadataName(syntaxTree),
                    metadataName,
                    StringComparison.Ordinal))
            {
                continue;
            }

            var semanticModel = Compilation.GetSemanticModel(syntaxTree);
            if (semanticModel.GetDeclaredSymbol(
                    syntaxTree.GetRoot()) is IAkburaComponentSymbol component)
            {
                return component;
            }
        }

        return null;
    }

    private IEnumerable<string>
        GetAkburaComponentCandidateMetadataNames(string componentName)
    {
        var name = componentName.Trim();
        if (name.Length == 0)
        {
            yield break;
        }

        if (name.StartsWith("global::", StringComparison.Ordinal))
        {
            yield return name["global::".Length..];
            yield break;
        }

        var usingDirectives = GetCSharpUsingDirectives();
        var aliasSeparator = name.IndexOf("::", StringComparison.Ordinal);
        if (aliasSeparator > 0)
        {
            var alias = name[..aliasSeparator];
            var remainder = name[(aliasSeparator + 2)..];
            foreach (var directive in usingDirectives)
            {
                if (directive.Alias != null &&
                    directive.Name != null &&
                    string.Equals(
                        directive.Alias.Name.Identifier.ValueText,
                        alias,
                        StringComparison.Ordinal))
                {
                    yield return NormalizeGlobalName(
                        directive.Name + "." + remainder);
                }
            }

            yield break;
        }

        if (name.IndexOf(".", StringComparison.Ordinal) >= 0)
        {
            yield return name;
            yield break;
        }

        foreach (var directive in usingDirectives)
        {
            if (directive.Alias != null &&
                directive.Name != null &&
                string.Equals(
                    directive.Alias.Name.Identifier.ValueText,
                    name,
                    StringComparison.Ordinal))
            {
                yield return NormalizeGlobalName(
                    directive.Name.ToString());
            }
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var @namespace in GetAkburaUsingNamespaces())
        {
            var metadataName = @namespace + "." + name;
            if (seen.Add(metadataName))
            {
                yield return metadataName;
            }
        }

        var currentNamespace = GetAkburaNamespaceText(
            SyntaxTree.GetRoot(),
            SyntaxTree);
        if (currentNamespace.Length > 0)
        {
            var metadataName = currentNamespace + "." + name;
            if (seen.Add(metadataName))
            {
                yield return metadataName;
            }
        }

        if (seen.Add(name))
        {
            yield return name;
        }
    }

    private ImmutableArray<VisibleMarkupNamespace>
        GetVisibleMarkupNamespaces()
    {
        using var builder =
            ImmutableArrayBuilder<VisibleMarkupNamespace>.Rent();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        AddVisibleMarkupNamespace(
            builder,
            seen,
            string.Empty,
            alias: null);
        AddVisibleMarkupNamespace(
            builder,
            seen,
            GetAkburaNamespaceText(
                SyntaxTree.GetRoot(),
                SyntaxTree),
            alias: null);

        foreach (var directive in GetCSharpUsingDirectives())
        {
            if (directive.StaticKeyword.RawKind != 0 ||
                directive.Name == null)
            {
                continue;
            }

            var name = NormalizeGlobalName(
                directive.Name.ToString());
            if (name.EndsWith(".akcss", StringComparison.Ordinal))
            {
                continue;
            }

            AddVisibleMarkupNamespace(
                builder,
                seen,
                name,
                directive.Alias?.Name.Identifier.ValueText);
        }

        return builder.ToImmutable();
    }

    private ImmutableArray<VisibleMarkupNamespace>
        GetVisibleMarkupExtensionNamespaces()
    {
        using var builder =
            ImmutableArrayBuilder<VisibleMarkupNamespace>.Rent();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var visibleNamespace in GetVisibleMarkupNamespaces())
        {
            AddVisibleMarkupNamespace(
                builder,
                seen,
                visibleNamespace.Name,
                visibleNamespace.Alias);
        }

        foreach (var namespaceName in
                 GetDefaultMarkupExtensionNamespaces())
        {
            AddVisibleMarkupNamespace(
                builder,
                seen,
                namespaceName,
                alias: null);
        }

        return builder.ToImmutable();
    }

    private void AddMarkupExtensionTypeAliasCandidates(
        Dictionary<string, MarkupExtensionLookupCandidate> candidates,
        CSharpCompilation csharpCompilation,
        CancellationToken cancellationToken)
    {
        foreach (var directive in GetCSharpUsingDirectives())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (directive.Alias == null ||
                directive.Name == null ||
                directive.StaticKeyword.RawKind != 0)
            {
                continue;
            }

            var type = csharpCompilation.GetTypeByMetadataName(
                NormalizeGlobalName(directive.Name.ToString()));
            if (type == null)
            {
                continue;
            }

            AddMarkupExtensionCandidate(
                candidates,
                csharpCompilation,
                type,
                namespaceAlias: null,
                displayNameOverride:
                    directive.Alias.Name.Identifier.ValueText);
        }
    }

    private void AddMarkupExtensionCandidate(
        Dictionary<string, MarkupExtensionLookupCandidate> candidates,
        CSharpCompilation csharpCompilation,
        INamedTypeSymbol type,
        string? namespaceAlias,
        string? displayNameOverride = null)
    {
        var provideValueMethod =
            FindMarkupExtensionProvideValueMethod(type);
        var isAvaloniaBinding = IsAvaloniaBindingCompletionType(type);
        if ((provideValueMethod == null && !isAvaloniaBinding) ||
            !IsCompletableMarkupExtension(csharpCompilation, type))
        {
            return;
        }

        var displayName = displayNameOverride ??
            GetMarkupExtensionCompletionName(type, namespaceAlias);
        if (displayName.Length == 0 ||
            candidates.ContainsKey(displayName))
        {
            return;
        }

        candidates.Add(
            displayName,
            new MarkupExtensionLookupCandidate(
                displayName,
                GetTypeMetadataName(type),
                type,
                provideValueMethod,
                isAvaloniaBinding,
                IsUtilityVariantMarkupExtension(type)));
    }

    private bool IsCompletableMarkupExtension(
        CSharpCompilation compilation,
        INamedTypeSymbol type)
    {
        if (type.IsStatic ||
            type.IsAbstract ||
            type.TypeKind is not (TypeKind.Class or TypeKind.Struct) ||
            !compilation.IsSymbolAccessibleWithin(
                type,
                compilation.Assembly))
        {
            return false;
        }

        return type.InstanceConstructors.Any(constructor =>
            constructor.DeclaredAccessibility == Accessibility.Public &&
            compilation.IsSymbolAccessibleWithin(
                constructor,
                compilation.Assembly));
    }

    private static bool IsAvaloniaBindingCompletionType(
        INamedTypeSymbol type)
    {
        return string.Equals(
                type.ContainingNamespace.ToDisplayString(),
                "Avalonia.Data",
                StringComparison.Ordinal) &&
            IsAvaloniaBindingExtensionName(type.Name);
    }

    private static bool IsUtilityVariantMarkupExtension(
        INamedTypeSymbol type)
    {
        return type.GetAttributes().Any(static attribute =>
            string.Equals(
                attribute.AttributeClass?.ToDisplayString(),
                "Akbura.Markup.UtilityVariantAttribute",
                StringComparison.Ordinal));
    }

    private static string GetMarkupExtensionCompletionName(
        INamedTypeSymbol type,
        string? namespaceAlias)
    {
        var name = type.Name;
        if (HasMarkupExtensionSuffix(name))
        {
            name = name[..^"Extension".Length];
        }

        return namespaceAlias == null
            ? name
            : namespaceAlias + "::" + name;
    }

    private static void AddVisibleMarkupNamespace(
        ImmutableArrayBuilder<VisibleMarkupNamespace> builder,
        HashSet<string> seen,
        string name,
        string? alias)
    {
        var normalizedName = NormalizeGlobalName(name);
        var key = (alias ?? string.Empty) + "\0" + normalizedName;
        if (seen.Add(key))
        {
            builder.Add(new VisibleMarkupNamespace(
                normalizedName,
                alias));
        }
    }

    private static void AddMarkupComponentCandidate(
        Dictionary<string, MarkupComponentLookupCandidate> candidates,
        string displayName,
        string metadataName,
        INamedTypeSymbol? componentType,
        bool isAkburaComponent)
    {
        if (candidates.TryGetValue(displayName, out var existing) &&
            (existing.IsAkburaComponent ||
             !isAkburaComponent))
        {
            return;
        }

        candidates[displayName] = new MarkupComponentLookupCandidate(
            displayName,
            metadataName,
            componentType,
            isAkburaComponent);
    }

    private static void AddVisibleAkburaComponentCandidates(
        Dictionary<string, MarkupComponentLookupCandidate> candidates,
        ImmutableArray<VisibleMarkupNamespace> visibleNamespaces,
        string metadataName,
        CSharpCompilation csharpCompilation)
    {
        if (metadataName.Length == 0)
        {
            return;
        }

        var separator = metadataName.LastIndexOf('.');
        var namespaceName = separator < 0
            ? string.Empty
            : metadataName[..separator];
        var simpleName = separator < 0
            ? metadataName
            : metadataName[(separator + 1)..];
        var componentType = csharpCompilation.GetTypeByMetadataName(
            metadataName);

        foreach (var visibleNamespace in visibleNamespaces)
        {
            if (!string.Equals(
                    visibleNamespace.Name,
                    namespaceName,
                    StringComparison.Ordinal))
            {
                continue;
            }

            var displayName = visibleNamespace.Alias == null
                ? simpleName
                : visibleNamespace.Alias + "::" + simpleName;
            AddMarkupComponentCandidate(
                candidates,
                displayName,
                metadataName,
                componentType,
                isAkburaComponent: true);
        }
    }

    private static IMarkupComponentSymbol
        CreateMarkupComponentLookupSymbol(
            string displayName,
            INamedTypeSymbol? componentType,
            IAkburaComponentSymbol? akburaComponent)
    {
        var csharpDefinition = componentType != null
            ? new CSharpSymbolDefinition(componentType)
            : akburaComponent?.CSharpDefinition ?? default;
        return new MarkupComponentSymbol(
            displayName,
            csharpDefinition,
            akburaComponent?.ContentModel ?? default,
            akburaComponent: akburaComponent);
    }

    private static INamespaceSymbol? GetNamespaceSymbol(
        INamespaceSymbol globalNamespace,
        string namespaceName)
    {
        var current = globalNamespace;
        if (namespaceName.Length == 0)
        {
            return current;
        }

        foreach (var segment in namespaceName.Split('.'))
        {
            current = current.GetNamespaceMembers()
                .FirstOrDefault(candidate => string.Equals(
                    candidate.Name,
                    segment,
                    StringComparison.Ordinal));
            if (current == null)
            {
                return null;
            }
        }

        return current;
    }

    private static bool IsCompletableMarkupComponent(
        Compilation compilation,
        INamedTypeSymbol type,
        bool isAkburaComponent)
    {
        if (type.IsStatic ||
            type.IsAbstract ||
            type.Arity != 0 ||
            type.TypeKind is not (TypeKind.Class or TypeKind.Struct) ||
            !compilation.IsSymbolAccessibleWithin(
                type,
                compilation.Assembly))
        {
            return false;
        }

        if (isAkburaComponent)
        {
            return true;
        }

        var namespaceName = type.ContainingNamespace.ToDisplayString();
        if (namespaceName.Equals(
                "Avalonia",
                StringComparison.Ordinal) ||
            namespaceName.StartsWith(
                "Avalonia.",
                StringComparison.Ordinal))
        {
            return true;
        }

        if (type.SpecialType != SpecialType.None ||
            namespaceName.Equals(
                "System",
                StringComparison.Ordinal) ||
            namespaceName.StartsWith(
                "System.",
                StringComparison.Ordinal) ||
            namespaceName.Equals(
                "Microsoft",
                StringComparison.Ordinal) ||
            namespaceName.StartsWith(
                "Microsoft.",
                StringComparison.Ordinal))
        {
            return false;
        }

        return type.TypeKind == TypeKind.Class &&
            type.InstanceConstructors.Any(constructor =>
                constructor.Parameters.Length == 0 &&
                compilation.IsSymbolAccessibleWithin(
                    constructor,
                    compilation.Assembly));
    }

    private static bool IsDerivedFromOrEqual(
        INamedTypeSymbol type,
        INamedTypeSymbol? baseType)
    {
        if (baseType == null)
        {
            return false;
        }

        for (var current = type;
             current != null;
             current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(
                    current,
                    baseType))
            {
                return true;
            }
        }

        return false;
    }

    private static string GetTypeMetadataName(INamedTypeSymbol type)
    {
        return NormalizeGlobalName(type.ToDisplayString(
            SymbolDisplayFormat.FullyQualifiedFormat));
    }

    private static string NormalizeGlobalName(string name)
    {
        return name.StartsWith("global::", StringComparison.Ordinal)
            ? name["global::".Length..]
            : name;
    }

    private readonly struct VisibleMarkupNamespace
    {
        public VisibleMarkupNamespace(string name, string? alias)
        {
            Name = name;
            Alias = alias;
        }

        public string Name { get; }

        public string? Alias { get; }
    }

    private sealed class CompletionComponentCatalog
    {
        public CompletionComponentCatalog(
            ImmutableArray<MarkupComponentLookupCandidate> candidates)
        {
            Candidates = candidates;
        }

        public ImmutableArray<MarkupComponentLookupCandidate> Candidates
        {
            get;
        }
    }

    private sealed class CompletionMarkupExtensionCatalog
    {
        public CompletionMarkupExtensionCatalog(
            ImmutableArray<MarkupExtensionLookupCandidate> candidates)
        {
            Candidates = candidates;
        }

        public ImmutableArray<MarkupExtensionLookupCandidate> Candidates
        {
            get;
        }
    }
}

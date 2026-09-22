using Akbura.Pools;
using Akbura.Language;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Workspaces.Documents;
using Akbura.Workspaces.Projects;
using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace Akbura.Workspaces.Resources;

internal sealed class ResourceCompletionIndex
{
    private const int LocalScopePriorityBase = 10;
    private const int LocalScopePriorityStep = 100;
    private const int ImportedResourcePriorityOffset = 10;
    private const int ApplicationResourcePriority = 1_000_000;
    private const int BuiltInResourcePriority = 2_000_000;

    private static readonly string[] s_avaloniaResourceTypePrefixes =
    [
        "Avalonia.Media.",
        "Avalonia.Media.Immutable.",
        "Avalonia.Controls.",
        "Avalonia.Styling.",
        "Avalonia.",
    ];

    private readonly ConditionalWeakTable<
        object,
        ProjectResourceCandidates> _projectCandidates = new();

    public ImmutableArray<ResourceCompletionCandidate> GetCandidates(AkburaProjectSnapshot project, CancellationToken cancellationToken)
    {
        if (project == null)
        {
            throw new ArgumentNullException(nameof(project));
        }

        return GetProjectCandidates(project, cancellationToken).Candidates;
    }

    public ImmutableArray<ResourceCompletionCandidate> GetCandidates(AkburaDocumentContext context, int position, CancellationToken cancellationToken)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var projectCandidates = GetProjectCandidates(
            context.Project,
            cancellationToken);
        var candidates = CreateCandidateMap(
            projectCandidates.Candidates);
        AddAkburaDocumentCandidates(
            context,
            position,
            projectCandidates,
            candidates,
            cancellationToken);
        return CreateCandidateArray(candidates);
    }

    private ProjectResourceCandidates GetProjectCandidates(AkburaProjectSnapshot project, CancellationToken cancellationToken)
    {
        var cacheKey = project.ResourceInputsIdentity;
        if (_projectCandidates.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var created = BuildCandidates(project, cancellationToken);
        try
        {
            _projectCandidates.Add(cacheKey, created);
            return created;
        }
        catch (ArgumentException)
        {
            return _projectCandidates.TryGetValue(cacheKey, out cached)
                ? cached
                : created;
        }
    }

    private static ProjectResourceCandidates BuildCandidates(AkburaProjectSnapshot project, CancellationToken cancellationToken)
    {
        var exports = AssemblyResourceExportReader.Read(
            project.CSharpCompilation,
            cancellationToken);
        var exportsByDictionary = exports
            .GroupBy(static export => export.Dictionary)
            .ToDictionary(
                static group => group.Key,
                static group => group.ToImmutableArray());
        var availableAssemblies = exports
            .Select(static export => export.Dictionary.Assembly)
            .Concat(project.ResourceDocuments.Keys.Select(
                static dictionary => dictionary.Assembly))
            .Append(ResourceAssemblyIdentity.Create(
                project.CSharpCompilation.Assembly.Identity))
            .Distinct()
            .ToImmutableArray();
        var candidates = new Dictionary<
            string,
            List<ResourceCompletionOrigin>>(
                StringComparer.Ordinal);
        var visited = new HashSet<ResourceDictionaryIdentity>();

        foreach (var document in project.ResourceDocuments.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var applicationRoot = document.Facts.ApplicationRoot;
            if (!applicationRoot.HasValue ||
                !applicationRoot.Value.ScopeId.HasValue)
            {
                continue;
            }

            AddLocalScope(
                project,
                document,
                applicationRoot.Value.ScopeId.Value,
                ApplicationResourcePriority,
                ApplicationResourcePriority +
                    ImportedResourcePriorityOffset,
                exportsByDictionary,
                availableAssemblies,
                candidates,
                visited,
                cancellationToken);
        }

        if (project.CSharpCompilation.GetTypeByMetadataName(
                "Avalonia.Application") != null)
        {
            foreach (var hint in BuiltInAvaloniaResourceHints.All)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddCandidate(
                    candidates,
                    hint.Key,
                    new ResourceCompletionOrigin(
                        ResourceCompletionOriginKind.BuiltInHint,
                        "Built-in Avalonia resource hint",
                        project.CSharpCompilation.GetTypeByMetadataName(
                            hint.ResourceTypeMetadataName),
                        BuiltInResourcePriority));
            }
        }

        return new ProjectResourceCandidates(
            CreateCandidateArray(candidates),
            exportsByDictionary,
            availableAssemblies);
    }

    private static void AddLocalScope(AkburaProjectSnapshot project, ResourceDocumentSnapshot document, int scopeId, int localPriority, int importedPriority, IReadOnlyDictionary<ResourceDictionaryIdentity, ImmutableArray<AssemblyResourceExport>> exports, ImmutableArray<ResourceAssemblyIdentity> availableAssemblies, Dictionary<string, List<ResourceCompletionOrigin>> candidates, HashSet<ResourceDictionaryIdentity> visited, CancellationToken cancellationToken)
    {
        foreach (var declaration in document.Facts.Declarations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (declaration.ScopeId != scopeId)
            {
                continue;
            }

            AddCandidate(
                candidates,
                declaration.Key,
                new ResourceCompletionOrigin(
                    ResourceCompletionOriginKind.Local,
                    "Local resource " +
                        document.LogicalPath.Value,
                    ResolveLocalResourceType(
                        project.CSharpCompilation,
                        declaration),
                    localPriority));
        }

        foreach (var import in document.Facts.Imports)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (import.ScopeId != scopeId ||
                !ResourceIncludeResolver.TryResolve(
                    import.Source,
                    document.Input.DictionaryIdentity,
                    availableAssemblies,
                    out var importedDictionary))
            {
                continue;
            }

            AddImportedDictionary(
                project,
                importedDictionary,
                importedPriority,
                exports,
                availableAssemblies,
                candidates,
                visited,
                cancellationToken);
        }
    }

    private static void AddImportedDictionary(AkburaProjectSnapshot project, ResourceDictionaryIdentity dictionary, int priority, IReadOnlyDictionary<ResourceDictionaryIdentity, ImmutableArray<AssemblyResourceExport>> exports, ImmutableArray<ResourceAssemblyIdentity> availableAssemblies, Dictionary<string, List<ResourceCompletionOrigin>> candidates, HashSet<ResourceDictionaryIdentity> visited, CancellationToken cancellationToken)
    {
        if (!visited.Add(dictionary))
        {
            return;
        }

        if (project.ResourceDocuments.TryGetValue(
                dictionary,
                out var localDocument))
        {
            foreach (var scope in localDocument.Facts.Scopes)
            {
                if (!scope.IsDocumentRoot)
                {
                    continue;
                }

                AddLocalScope(
                    project,
                    localDocument,
                    scope.Id,
                    priority,
                    priority,
                    exports,
                    availableAssemblies,
                    candidates,
                    visited,
                    cancellationToken);
                break;
            }

            return;
        }

        if (!exports.TryGetValue(dictionary, out var dictionaryExports))
        {
            return;
        }

        foreach (var export in dictionaryExports)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddCandidate(
                candidates,
                export.Key,
                new ResourceCompletionOrigin(
                    ResourceCompletionOriginKind.AssemblyExport,
                    "Exported by " + dictionary,
                    export.ResourceType,
                    priority));
        }
    }

    private static void AddAkburaDocumentCandidates(AkburaDocumentContext context, int position, ProjectResourceCandidates projectCandidates, Dictionary<string, List<ResourceCompletionOrigin>> candidates, CancellationToken cancellationToken)
    {
        if (context.Document.SyntaxTree is not ComponentSyntaxTree)
        {
            return;
        }

        var root = context.Document.SyntaxTree.GetRootSyntax();
        if (root.FullSpan.Length == 0)
        {
            return;
        }

        var tokenPosition = Math.Min(
            Math.Max(position, root.FullSpan.Start),
            root.FullSpan.End - 1);
        var node = root.FindToken(tokenPosition).Parent;
        while (node != null && node is not MarkupElementSyntax)
        {
            node = node.Parent;
        }

        if (node is not MarkupElementSyntax element ||
            !TryGetAkburaDocumentDictionary(
                context,
                out var containingDictionary))
        {
            return;
        }

        var semanticModel = context.Project.Compilation.GetSemanticModel(
            context.Document.SyntaxTree);
        var visited = new HashSet<ResourceDictionaryIdentity>();
        var scopeDepth = 0;
        for (var current = element; current != null; current = AkburaSemanticModel.GetParentMarkupElement(current), scopeDepth++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var content in current.Body)
            {
                if (content is not MarkupElementContentSyntax
                    {
                        Element: var propertyElement,
                    } ||
                    !IsResourcesPropertyElement(
                        semanticModel,
                        propertyElement))
                {
                    continue;
                }

                AddAkburaResourceEntries(
                    context.Project,
                    semanticModel,
                    propertyElement,
                    containingDictionary,
                    LocalScopePriorityBase +
                        (scopeDepth * LocalScopePriorityStep),
                    LocalScopePriorityBase +
                        (scopeDepth * LocalScopePriorityStep) +
                        ImportedResourcePriorityOffset,
                    projectCandidates,
                    candidates,
                    visited,
                    context.Document.FilePath,
                    cancellationToken);
            }
        }
    }

    private static void AddAkburaResourceEntries(AkburaProjectSnapshot project, AkburaSemanticModel semanticModel, MarkupElementSyntax container, ResourceDictionaryIdentity containingDictionary, int localPriority, int importedPriority, ProjectResourceCandidates projectCandidates, Dictionary<string, List<ResourceCompletionOrigin>> candidates, HashSet<ResourceDictionaryIdentity> visited, string sourcePath, CancellationToken cancellationToken, string? themeVariant = null)
    {
        var isThemeDictionaries =
            IsThemeDictionariesPropertyElement(
                semanticModel,
                container);
        foreach (var content in container.Body)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (content is not MarkupElementContentSyntax
                {
                    Element: var entry,
                })
            {
                continue;
            }

            var name = GetMarkupElementName(entry);
            if (isThemeDictionaries &&
                IsResourceDictionaryElement(name))
            {
                TryGetAkburaResourceKey(
                    semanticModel,
                    entry,
                    out var branchThemeVariant,
                    out _);
                AddAkburaResourceEntries(
                    project,
                    semanticModel,
                    entry,
                    containingDictionary,
                    localPriority,
                    importedPriority,
                    projectCandidates,
                    candidates,
                    visited,
                    sourcePath,
                    cancellationToken,
                    branchThemeVariant);
                continue;
            }

            if (TryAddAkburaResourceDeclaration(
                    semanticModel,
                    entry,
                    sourcePath,
                    candidates,
                    themeVariant,
                    localPriority))
            {
                continue;
            }

            if (TryGetAkburaResourceImport(
                    semanticModel,
                    entry,
                    out var source) &&
                ResourceIncludeResolver.TryResolve(
                    source,
                    containingDictionary,
                    projectCandidates.AvailableAssemblies,
                    out var importedDictionary))
            {
                AddImportedDictionary(
                    project,
                    importedDictionary,
                    importedPriority,
                    projectCandidates.Exports,
                    projectCandidates.AvailableAssemblies,
                    candidates,
                    visited,
                    cancellationToken);
                continue;
            }

            if (IsResourceDictionaryElement(name) ||
                IsResourceCollectionProperty(name))
            {
                AddAkburaResourceEntries(
                    project,
                    semanticModel,
                    entry,
                    containingDictionary,
                    localPriority,
                    importedPriority,
                    projectCandidates,
                    candidates,
                    visited,
                    sourcePath,
                    cancellationToken,
                    themeVariant);
            }
        }
    }

    private static bool TryAddAkburaResourceDeclaration(AkburaSemanticModel semanticModel, MarkupElementSyntax element, string sourcePath, Dictionary<string, List<ResourceCompletionOrigin>> candidates, string? themeVariant, int priority)
    {
        if (!TryGetAkburaResourceKey(
                semanticModel,
                element,
                out var key,
                out var keyOperation))
        {
            return false;
        }

        var description = "Local resource " + sourcePath;
        if (!string.IsNullOrEmpty(themeVariant))
        {
            description += " (" + themeVariant + " theme)";
        }

        AddCandidate(
            candidates,
            key,
            new ResourceCompletionOrigin(
                ResourceCompletionOriginKind.Local,
                description,
                keyOperation.ContainingComponent?.ComponentType,
                priority));
        return true;
    }

    private static bool TryGetAkburaResourceKey(AkburaSemanticModel semanticModel, MarkupElementSyntax element, out string key, out IMarkupDictionaryKeyOperation keyOperation)
    {
        key = string.Empty;
        keyOperation = null!;
        if (element.StartTag == null)
        {
            return false;
        }

        foreach (var attribute in element.StartTag.Attributes)
        {
            if (AkburaSemanticModel.IsMarkupDictionaryKeyDirective(
                    attribute) &&
                semanticModel.GetOperation(attribute) is
                    IMarkupDictionaryKeyOperation
                {
                    ConstantValue: string value,
                } operation &&
                value.Length != 0)
            {
                key = value;
                keyOperation = operation;
                return true;
            }
        }

        return false;
    }

    private static bool TryGetAkburaResourceImport(AkburaSemanticModel semanticModel, MarkupElementSyntax element, out string source)
    {
        source = string.Empty;
        if (element.StartTag == null ||
            semanticModel.GetSymbolInfo(element).Symbol is not
                IMarkupComponentSymbol component ||
            component.ComponentType is not { } componentType ||
            !string.Equals(
                componentType.ContainingNamespace.ToDisplayString(),
                "Avalonia.Markup.Xaml.Styling",
                StringComparison.Ordinal) ||
            componentType.Name is not (
                "ResourceInclude" or
                "MergeResourceInclude" or
                "StyleInclude"))
        {
            return false;
        }

        foreach (var attribute in element.StartTag.Attributes)
        {
            if (!string.Equals(
                    AkburaSemanticModel.GetMarkupPropertyName(attribute),
                    "Source",
                    StringComparison.Ordinal) ||
                AkburaSemanticModel.GetMarkupAttributeValue(attribute) is not
                    MarkupLiteralAttributeValueSyntax literal)
            {
                continue;
            }

            var raw = literal.ToFullString().Trim();
            if (raw.Length == 0 ||
                (raw[0] is '\'' or '"' &&
                 (raw.Length < 2 || raw[^1] != raw[0])) ||
                (raw[0] is not ('\'' or '"') &&
                 raw.Any(char.IsWhiteSpace)))
            {
                return false;
            }

            source =
                AkburaSemanticModel.GetMarkupLiteralAttributeValueText(
                    literal);
            return source.Length != 0;
        }

        return false;
    }

    private static bool TryGetAkburaDocumentDictionary(AkburaDocumentContext context, out ResourceDictionaryIdentity dictionary)
    {
        dictionary = default;
        var filePath = context.Document.FilePath;
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        var projectDirectory = context.Project.Context.ProjectDirectory;
        var normalizedFilePath = Path.GetFullPath(filePath);
        string relativePath;
        if (!string.IsNullOrWhiteSpace(projectDirectory))
        {
            var normalizedProjectDirectory = Path
                .GetFullPath(projectDirectory)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);
            var prefix =
                normalizedProjectDirectory + Path.DirectorySeparatorChar;
            relativePath = normalizedFilePath.StartsWith(
                    prefix,
                    StringComparison.OrdinalIgnoreCase)
                ? normalizedFilePath.Substring(prefix.Length)
                : Path.GetFileName(normalizedFilePath);
        }
        else
        {
            relativePath = Path.GetFileName(normalizedFilePath);
        }

        if (!ResourcePath.TryCreateExportPath(
                relativePath.Replace('\\', '/'),
                out var resourcePath))
        {
            return false;
        }

        dictionary = new ResourceDictionaryIdentity(
            ResourceAssemblyIdentity.Create(
                context.Project.CSharpCompilation.Assembly.Identity),
            resourcePath);
        return true;
    }

    private static bool IsResourcesPropertyElement(AkburaSemanticModel semanticModel, MarkupElementSyntax element)
    {
        if (semanticModel.GetSymbolInfo(element).Symbol is not
                Akbura.Language.Symbols.IPropertySymbol property ||
            !string.Equals(
                property.Name,
                "Resources",
                StringComparison.Ordinal) ||
            property.Type.Symbol is not ITypeSymbol propertyType ||
            !semanticModel
                .CreateMarkupPropertyElementContentModel(property)
                .IsDictionary)
        {
            return false;
        }

        return IsResourceDictionaryType(
            semanticModel,
            propertyType);
    }

    private static bool IsThemeDictionariesPropertyElement(AkburaSemanticModel semanticModel, MarkupElementSyntax element)
    {
        if (semanticModel.GetSymbolInfo(element).Symbol is not
                Akbura.Language.Symbols.IPropertySymbol property ||
            !string.Equals(
                property.Name,
                "ThemeDictionaries",
                StringComparison.Ordinal) ||
            property.CSharpDefinition.Symbol is not
                Microsoft.CodeAnalysis.IPropertySymbol csharpProperty)
        {
            return false;
        }

        return IsResourceDictionaryType(
            semanticModel,
            csharpProperty.ContainingType);
    }

    private static bool IsResourceDictionaryType(AkburaSemanticModel semanticModel, ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol namedType ||
            semanticModel.Compilation.CSharpCompilation
                .GetTypeByMetadataName(
                    "Avalonia.Controls.IResourceDictionary") is not
                    { } resourceDictionaryType)
        {
            return false;
        }

        if (IsResourceDictionaryContract(
                namedType,
                resourceDictionaryType))
        {
            return true;
        }

        foreach (var @interface in namedType.AllInterfaces)
        {
            if (IsResourceDictionaryContract(
                    @interface,
                    resourceDictionaryType))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsResourceDictionaryContract(INamedTypeSymbol type, INamedTypeSymbol expectedType)
    {
        return string.Equals(
                type.MetadataName,
                expectedType.MetadataName,
                StringComparison.Ordinal) &&
            string.Equals(
                type.ContainingNamespace.ToDisplayString(),
                expectedType.ContainingNamespace.ToDisplayString(),
                StringComparison.Ordinal) &&
            type.ContainingAssembly.Identity.Equals(
                expectedType.ContainingAssembly.Identity);
    }

    private static bool IsResourceDictionaryElement(string name)
    {
        var separator = name.LastIndexOf(':');
        var simpleName = separator < 0
            ? name
            : name.Substring(separator + 1);
        return string.Equals(
            simpleName,
            "ResourceDictionary",
            StringComparison.Ordinal);
    }

    private static bool IsResourceCollectionProperty(string name)
    {
        return name.EndsWith(
                ".MergedDictionaries",
                StringComparison.Ordinal) ||
            name.EndsWith(
                ".ThemeDictionaries",
                StringComparison.Ordinal);
    }

    private static string GetMarkupElementName(MarkupElementSyntax element)
    {
        return element.StartTag?.Name.ToFullString().Trim() ??
            string.Empty;
    }

    private static Dictionary<string, List<ResourceCompletionOrigin>> CreateCandidateMap(ImmutableArray<ResourceCompletionCandidate> candidates)
    {
        var result = new Dictionary<
            string,
            List<ResourceCompletionOrigin>>(
                candidates.Length,
                StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            result.Add(
                candidate.Key,
                candidate.Origins.ToList());
        }

        return result;
    }

    private static ImmutableArray<ResourceCompletionCandidate> CreateCandidateArray(Dictionary<string, List<ResourceCompletionOrigin>> candidates)
    {
        using var result =
            ImmutableArrayBuilder<ResourceCompletionCandidate>.Rent(
                candidates.Count);
        foreach (var pair in candidates)
        {
            result.Add(new ResourceCompletionCandidate(
                pair.Key,
                pair.Value.ToImmutableArray()));
        }

        return result.ToImmutable();
    }

    private static ITypeSymbol? ResolveLocalResourceType(Compilation compilation, LocalResourceDeclarationFact declaration)
    {
        var xmlNamespace = declaration.TypeNamespace;
        var typeName = declaration.TypeName;
        var colon = typeName.IndexOf(':');
        if (colon >= 0)
        {
            typeName = typeName.Substring(colon + 1);
        }

        if (string.Equals(
                xmlNamespace,
                "http://schemas.microsoft.com/winfx/2006/xaml",
                StringComparison.Ordinal))
        {
            return compilation.GetTypeByMetadataName(
                typeName switch
                {
                    "Boolean" => "System.Boolean",
                    "Double" => "System.Double",
                    "Int32" => "System.Int32",
                    "String" => "System.String",
                    _ => "System." + typeName,
                });
        }

        if (xmlNamespace != null &&
            xmlNamespace.StartsWith(
                "using:",
                StringComparison.Ordinal))
        {
            return compilation.GetTypeByMetadataName(
                xmlNamespace.Substring("using:".Length) +
                "." + typeName);
        }

        if (xmlNamespace != null &&
            xmlNamespace.StartsWith(
                "clr-namespace:",
                StringComparison.Ordinal))
        {
            var value = xmlNamespace.Substring(
                "clr-namespace:".Length);
            var separator = value.IndexOf(';');
            var @namespace = separator < 0
                ? value
                : value.Substring(0, separator);
            var metadataName = @namespace + "." + typeName;
            if (separator < 0)
            {
                return compilation.GetTypeByMetadataName(metadataName);
            }

            var assemblyName = GetClrNamespaceAssemblyName(
                value.Substring(separator + 1));
            var assembly = FindUniqueAssembly(
                compilation,
                assemblyName);
            return assembly?.GetTypeByMetadataName(metadataName);
        }

        if (string.IsNullOrEmpty(xmlNamespace) ||
            string.Equals(
                xmlNamespace,
                "https://github.com/avaloniaui",
                StringComparison.Ordinal))
        {
            foreach (var prefix in s_avaloniaResourceTypePrefixes)
            {
                var type = compilation.GetTypeByMetadataName(
                    prefix + typeName);
                if (type != null)
                {
                    return type;
                }
            }
        }

        return null;
    }

    private static string? GetClrNamespaceAssemblyName(string parameters)
    {
        const string prefix = "assembly=";
        var start = 0;
        while (start <= parameters.Length)
        {
            var end = parameters.IndexOf(';', start);
            if (end < 0)
            {
                end = parameters.Length;
            }

            var parameter = parameters
                .Substring(start, end - start)
                .Trim();
            if (parameter.StartsWith(
                    prefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                var assemblyName = parameter
                    .Substring(prefix.Length)
                    .Trim();
                return assemblyName.Length == 0
                    ? null
                    : assemblyName;
            }

            if (end == parameters.Length)
            {
                break;
            }

            start = end + 1;
        }

        return null;
    }

    private static IAssemblySymbol? FindUniqueAssembly(Compilation compilation, string? simpleName)
    {
        if (string.IsNullOrWhiteSpace(simpleName))
        {
            return null;
        }

        IAssemblySymbol? result = null;
        if (string.Equals(
                compilation.Assembly.Identity.Name,
                simpleName,
                StringComparison.OrdinalIgnoreCase))
        {
            result = compilation.Assembly;
        }

        foreach (var reference in compilation.References)
        {
            IAssemblySymbol? candidate;
            try
            {
                candidate = compilation.GetAssemblyOrModuleSymbol(
                    reference) as IAssemblySymbol;
            }
            catch (BadImageFormatException)
            {
                continue;
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            if (candidate == null ||
                !string.Equals(
                    candidate.Identity.Name,
                    simpleName,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (result != null &&
                !result.Identity.Equals(candidate.Identity))
            {
                return null;
            }

            result = candidate;
        }

        return result;
    }

    private static void AddCandidate(Dictionary<string, List<ResourceCompletionOrigin>> candidates, string key, ResourceCompletionOrigin origin)
    {
        if (!candidates.TryGetValue(key, out var origins))
        {
            origins = new List<ResourceCompletionOrigin>();
            candidates.Add(key, origins);
        }

        origins.Add(origin);
    }

    private sealed class ProjectResourceCandidates
    {
        public ProjectResourceCandidates(ImmutableArray<ResourceCompletionCandidate> candidates, IReadOnlyDictionary<ResourceDictionaryIdentity, ImmutableArray<AssemblyResourceExport>> exports, ImmutableArray<ResourceAssemblyIdentity> availableAssemblies)
        {
            Candidates = candidates;
            Exports = exports;
            AvailableAssemblies = availableAssemblies;
        }

        public ImmutableArray<ResourceCompletionCandidate> Candidates { get; }

        public IReadOnlyDictionary<
            ResourceDictionaryIdentity,
            ImmutableArray<AssemblyResourceExport>> Exports
        { get; }

        public ImmutableArray<ResourceAssemblyIdentity>
            AvailableAssemblies
        { get; }
    }
}

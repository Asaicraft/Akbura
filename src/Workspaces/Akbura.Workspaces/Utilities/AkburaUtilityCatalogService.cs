using Akbura.Language;
using Akbura.Language.Binder;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using AkburaCandidateReason = Akbura.Language.Symbols.CandidateReason;

namespace Akbura.Workspaces.Utilities;

internal sealed class AkburaUtilityCatalogService : IAkburaUtilityCatalogService
{
    private const string StandardModuleName = "Akbura.Styles.akcss";
    private const string StandardImport = "using " + StandardModuleName + ";";
    private const string VariantImport = "using Akbura.Markup;";

    private static readonly SymbolDisplayFormat TypeFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.WithGlobalNamespaceStyle(SymbolDisplayGlobalNamespaceStyle.Omitted);

    private static readonly IComparer<RankedUtility> UtilityOrder =
        Comparer<RankedUtility>.Create(static (left, right) =>
        {
            var order = left.Rank.CompareTo(right.Rank);
            if (order != 0) return order;
            order = StringComparer.Ordinal.Compare(left.Descriptor.Name, right.Descriptor.Name);
            if (order != 0) return order;
            order = StringComparer.Ordinal.Compare(left.Signature, right.Signature);
            return order != 0 ? order : StringComparer.Ordinal.Compare(left.Descriptor.Id, right.Descriptor.Id);
        });

    private readonly ConditionalWeakTable<AkburaSemanticModel, ConcurrentDictionary<LookupKey, TailwindUtilityDefinitionLookup>>
        _lookups = new();
    private readonly ConditionalWeakTable<AkburaProjectSnapshot, StandardLibraryContext> _standardLibraries = new();

    public AkburaStandardUtilityLibrary GetStandardLibrary(
        AkburaProjectSnapshot project,
        CancellationToken cancellationToken = default)
    {
        if (project == null) throw new ArgumentNullException(nameof(project));
        cancellationToken.ThrowIfCancellationRequested();
        return GetStandardLibraryContext(project, cancellationToken).Library;
    }

    public AkburaUtilitySearchResult FindUtilities(
        AkburaDocumentContext context,
        AkburaUtilitySearchRequest request,
        CancellationToken cancellationToken = default)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));
        if (request == null) throw new ArgumentNullException(nameof(request));
        if (request.Limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(request), "Limit must be between 1 and 100.");
        cancellationToken.ThrowIfCancellationRequested();

        var snapshotId = context.Solution.Version.ToString();
        if (!string.Equals(Path.GetExtension(context.Document.FilePath), ".akbura", StringComparison.OrdinalIgnoreCase))
        {
            return Failure(AkburaUtilitySearchStatus.InvalidDocument, "invalid_document", "Select an .akbura document.");
        }
        if (request.Position.HasValue == (request.TargetType != null) ||
            (request.TargetType != null && string.IsNullOrWhiteSpace(request.TargetType)))
        {
            return Failure(AkburaUtilitySearchStatus.InvalidTargetSelection, "invalid_target_selection",
                "Specify exactly one of position and targetType.");
        }

        var compilation = context.Project.Compilation;
        var model = compilation.GetSemanticModel(context.Document.SyntaxTree);
        MarkupElementSyntax? element = null;
        INamedTypeSymbol? type;
        bool ambiguous;
        if (request.Position is { } position)
        {
            if (position < 0 || position >= context.Document.Text.Length ||
                FindMarkupElement(context.Document.SyntaxTree.GetRootSyntax(), position) is not { } markupElement)
            {
                return Failure(AkburaUtilitySearchStatus.InvalidPosition, "invalid_position",
                    "Position must identify a markup element in the document.");
            }

            element = markupElement;
            var info = model.GetSymbolInfo(element);
            ambiguous = info.CandidateReason == AkburaCandidateReason.Ambiguous;
            type = info.Symbol is IMarkupComponentSymbol component
                ? model.GetMarkupComponentReferenceType(component)
                : null;
        }
        else
        {
            type = model.ResolveUtilityCatalogTarget(request.TargetType!.Trim(), out ambiguous);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (ambiguous)
        {
            return Failure(AkburaUtilitySearchStatus.AmbiguousTarget, "ambiguous_target",
                "The target name resolves to more than one type. Use a fully qualified target type.");
        }
        if (type == null || type.TypeKind == TypeKind.Error)
        {
            if (context.Project.CSharpCompilation.GetTypeByMetadataName("Avalonia.Controls.Control") == null)
            {
                return Failure(AkburaUtilitySearchStatus.MissingReferences, "missing_references",
                    "The target could not be resolved and the project is missing the Avalonia control reference.");
            }
            return Failure(AkburaUtilitySearchStatus.UnresolvedTarget, "unresolved_target",
                "The target type could not be resolved using the document and project references.");
        }

        var standard = GetStandardLibraryContext(context.Project, cancellationToken);
        var standardModule = standard.Module;
        var library = standard.Library;
        var additionalModule = request.IncludeImportable && library.IsAvailable ? standardModule : null;
        var scope = element == null ? null : AkcssSemanticFacts.FindTemplate(element);
        var key = new LookupKey(type, scope?.Span.Start ?? -1, request.IncludeImportable);
        var cache = _lookups.GetValue(model, static _ => new());
        if (!cache.TryGetValue(key, out var lookup))
        {
            lookup = model.LookupTailwindUtilityDefinitions(type, element,
                additionalModule == null ? null : StandardModuleName, additionalModule, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!lookup.HasUnresolvedDefinitions && !lookup.HasUnresolvedImports)
            {
                lookup = cache.GetOrAdd(key, lookup);
            }
        }

        using var diagnostics = ImmutableArrayBuilder<AkburaUtilityCatalogDiagnostic>.Rent();
        if (library.Diagnostic is { } libraryDiagnostic)
        {
            diagnostics.Add(libraryDiagnostic);
        }
        if (lookup.HasUnresolvedImports)
        {
            diagnostics.Add(new("unresolved_akcss_import", "One or more AKCSS imports could not be resolved."));
        }
        if (lookup.HasUnresolvedDefinitions)
        {
            diagnostics.Add(new("unresolved_utility_definition", "Some utility definitions have unresolved types or contain semantic errors."));
        }

        var query = request.Query?.Trim();
        var category = request.Category?.Trim();
        using var ranked = ImmutableArrayBuilder<RankedUtility>.Rent();
        var identities = PooledHashSet<string>.GetInstance();
        try
        {
            foreach (var definition in lookup.Definitions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var utility = definition.Symbol;
                var metadata = IsStandardDefinition(utility, standardModule)
                    ? AkburaStandardUtilityMetadata.Find(utility.Name)
                    : null;
                if (!string.IsNullOrEmpty(category) && metadata?.MatchesCategory(category!) != true)
                {
                    continue;
                }

                var rank = GetRank(utility.Name, query, metadata);
                if (rank < 0)
                {
                    continue;
                }

                var result = Describe(context, definition, metadata, rank);
                if (identities.Add(result.Descriptor.Id))
                {
                    ranked.Add(result);
                }
            }

            ranked.Sort(UtilityOrder);
            using var results = ImmutableArrayBuilder<AkburaUtilityDescriptor>.Rent(Math.Min(ranked.Count, request.Limit));
            for (var index = 0; index < ranked.Count && index < request.Limit; index++)
            {
                results.Add(ranked.WrittenSpan[index].Descriptor);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var complete = !lookup.HasUnresolvedDefinitions && !lookup.HasUnresolvedImports &&
                (!request.IncludeImportable || library.Diagnostic == null);
            return new(AkburaUtilitySearchStatus.Success, type.ToDisplayString(TypeFormat),
                results.ToImmutable(), snapshotId, complete, ranked.Count > request.Limit, diagnostics.ToImmutable());
        }
        finally
        {
            identities.Free();
        }

        AkburaUtilitySearchResult Failure(AkburaUtilitySearchStatus status, string code, string message) =>
            new(status, null, [], snapshotId, IsComplete: false, Truncated: false, [new(code, message)]);
    }

    private static MarkupElementSyntax? FindMarkupElement(AkburaSyntax root, int position)
    {
        if (!root.FullSpan.Contains(position)) return null;
        for (var node = root.FindToken(position).Parent; node != null; node = node.Parent)
        {
            if (node is MarkupElementSyntax markup && markup.Span.Contains(position))
            {
                return markup;
            }
        }
        return null;
    }

    private StandardLibraryContext GetStandardLibraryContext(AkburaProjectSnapshot project, CancellationToken cancellationToken)
    {
        if (_standardLibraries.TryGetValue(project, out var cached))
        {
            return cached;
        }

        var module = FindStandardModule(project.Compilation);
        var context = new StandardLibraryContext(module, DescribeStandardLibrary(project, module, cancellationToken));
        cancellationToken.ThrowIfCancellationRequested();
        return _standardLibraries.GetValue(project, _ => context);
    }

    private static IAkcssModuleSymbol? FindStandardModule(AkburaCompilation compilation)
    {
        var modules = compilation.GetAkcssModuleSymbolsByLogicalName(StandardModuleName);
        return modules.Length == 1 &&
            IsStandardModule(modules[0]) &&
            modules[0].AkcssSymbols.Any(static symbol => symbol is ITailwindUtilitySymbol)
            ? modules[0]
            : null;
    }

    private static bool IsStandardModule(IAkcssModuleSymbol module) =>
        module.MetadataName == StandardModuleName &&
        (module is not IMetadataAkcssModuleSymbol metadata || metadata.RuntimeModuleType.ContainingAssembly.Identity.Name == "Akbura");

    private static bool IsStandardDefinition(ITailwindUtilitySymbol utility, IAkcssModuleSymbol? module)
    {
        if (utility is IMetadataAkcssSymbol metadata)
        {
            return IsStandardModule(metadata.MetadataModule);
        }

        return module?.DeclaringSyntax is { } declaration &&
            utility.DeclarationSyntax is { } utilityDeclaration &&
            ReferenceEquals(utilityDeclaration.Root, declaration.Root);
    }

    private static AkburaStandardUtilityLibrary DescribeStandardLibrary(
        AkburaProjectSnapshot project,
        IAkcssModuleSymbol? module,
        CancellationToken cancellationToken)
    {
        var variant = project.CSharpCompilation.GetTypeByMetadataName("Akbura.Markup.mdExtension");
        var hasVariant = variant?.ContainingAssembly.Identity.Name == "Akbura" &&
            variant.GetAttributes().Any(static attribute =>
                attribute.AttributeClass?.ToDisplayString() == "Akbura.Markup.UtilityVariantAttribute");
        cancellationToken.ThrowIfCancellationRequested();

        var diagnostic = module == null
            ? new AkburaUtilityCatalogDiagnostic("standard_library_unavailable",
                "The standard Akbura utility module could not be found unambiguously in the project's references.")
            : !project.Compilation.GetLocalAkcssSyntaxTreesByLogicalName(StandardModuleName).IsEmpty
                ? new AkburaUtilityCatalogDiagnostic("standard_import_shadowed",
                    "A project AKCSS module has the same import name as the standard utility library.")
                : null;

        var border = project.CSharpCompilation.GetTypeByMetadataName("Avalonia.Controls.Border");
        var hasGridExample = diagnostic == null && hasVariant && border != null &&
            module!.AkcssSymbols.Any(symbol =>
                symbol is ITailwindUtilitySymbol
                {
                    Name: "col-span",
                    Parameters.Length: 1,
                    TargetType.Symbol: ITypeSymbol target,
                } utility &&
                utility.Parameters[0].Type.Symbol is ITypeSymbol { SpecialType: SpecialType.System_Int32 } &&
                AkburaSemanticModel.IsAssignableTo(border, target));

        return new(diagnostic == null ? StandardImport : null, hasVariant ? VariantImport : null, diagnostic, hasGridExample);
    }

    private static int GetRank(string name, string? query, AkburaStandardUtilityMetadata? metadata)
    {
        if (string.IsNullOrEmpty(query) || name.Equals(query, StringComparison.OrdinalIgnoreCase)) return 0;
        if (name.StartsWith(query!, StringComparison.OrdinalIgnoreCase)) return 1;
        return metadata?.MatchesQuery(query!) == true ? 2 : -1;
    }

    private static RankedUtility Describe(
        AkburaDocumentContext context,
        TailwindUtilityDefinition definition,
        AkburaStandardUtilityMetadata? metadata,
        int rank)
    {
        var utility = definition.Symbol;
        using var parameters = ImmutableArrayBuilder<AkburaUtilityParameterDescriptor>.Rent(utility.Parameters.Length);
        var pooledBuilder = PooledStringBuilder.GetInstance();
        string signatureText;
        string usageText;
        try
        {
            var builder = pooledBuilder.Builder.Append(utility.Name);
            foreach (var parameter in utility.Parameters)
            {
                var type = parameter.Type.ToDisplayString(TypeFormat);
                parameters.Add(new(parameter.Name, type, parameter.IsOptional));
                builder.Append("-(").Append(type).Append(' ').Append(parameter.Name);
                if (parameter.IsOptional) builder.Append(" = default");
                builder.Append(')');
            }
            signatureText = builder.ToString();

            builder.Clear().Append(utility.Name);
            foreach (var parameter in utility.Parameters)
            {
                builder.Append("-<").Append(parameter.Name).Append('>');
            }
            usageText = builder.ToString();
        }
        finally
        {
            pooledBuilder.Free();
        }

        var target = utility.HasTargetType ? utility.TargetType.ToDisplayString(TypeFormat) : null;
        var originKind = AkburaUtilityOriginKind.Source;
        string origin;
        string definitionKey;
        AkburaUtilitySourceLocation? location = null;

        if (utility is IMetadataAkcssSymbol metadataSymbol)
        {
            originKind = AkburaUtilityOriginKind.Library;
            origin = metadataSymbol.MetadataCarrierType.ContainingAssembly.Identity.ToString();
            definitionKey = $"{origin}:{metadataSymbol.MetadataCarrierType.ToDisplayString(TypeFormat)}";
        }
        else if (utility.DeclarationSyntax is { } declaration)
        {
            var span = declaration is AkcssUtilityDeclarationSyntax sourceUtility
                ? sourceUtility.Selector.Name.Span
                : declaration.Span;
            origin = FindDocumentPath(context, declaration) ?? string.Empty;
            if (!string.IsNullOrEmpty(origin))
            {
                location = new(origin, span.Start, span.Length);
            }
            else
            {
                foreach (var module in context.Project.Compilation.ReferencedModules)
                {
                    if (!module.TryGetSource(declaration, out var source)) continue;
                    originKind = AkburaUtilityOriginKind.Library;
                    origin = module.Manifest.AssemblyName + ":" + source.Source.SourceCodePath;
                    break;
                }
            }
            definitionKey = $"{origin}#{span.Start}:{span.Length}";
        }
        else
        {
            origin = string.Empty;
            definitionKey = utility.MetadataName;
        }

        var example = metadata?.Example is { } verifiedExample && utility.Parameters.Length == 1 &&
            utility.Parameters[0].Type.Symbol is ITypeSymbol { SpecialType: SpecialType.System_Int32 }
            ? verifiedExample
            : null;
        var descriptor = new AkburaUtilityDescriptor($"{definitionKey}:{target}:{signatureText}",
            utility.Name, usageText, parameters.ToImmutable(), target,
            definition.RequiredImport == null ? AkburaUtilityAvailability.Available : AkburaUtilityAvailability.RequiresImport,
            definition.RequiredImport == null ? [] : [StandardImport],
            originKind, origin, location, metadata?.Category, metadata?.Description, example);
        return new(rank, signatureText, descriptor);
    }

    private static string? FindDocumentPath(AkburaDocumentContext context, AkburaSyntax syntax)
    {
        var root = syntax.Root;
        foreach (var project in context.Solution.Projects.Values)
        {
            foreach (var document in project.Documents.Values)
            {
                if (ReferenceEquals(document.SyntaxTree.GetRootSyntax(), root))
                {
                    return document.FilePath;
                }
            }
        }
        return null;
    }

    private sealed record StandardLibraryContext(IAkcssModuleSymbol? Module, AkburaStandardUtilityLibrary Library);

    private readonly record struct LookupKey(INamedTypeSymbol TargetType, int Scope, bool IncludeImportable);
    private readonly record struct RankedUtility(int Rank, string Signature, AkburaUtilityDescriptor Descriptor);
}

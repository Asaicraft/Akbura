using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Akbura.Workspaces.Utilities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Workspaces.Layout;

/// <summary>
/// Checks layout recipe requirements and validates reference components in isolated Workspace snapshots.
/// </summary>
internal sealed class AkburaLayoutRecipeService(
    AkburaUtilityCatalogService utilityCatalog,
    IAkburaDocumentAnalysisService diagnostics) : IAkburaLayoutRecipeService
{
    public ImmutableArray<AkburaLayoutRecipeDescriptor> GetRecipes() => AkburaLayoutRecipes.Descriptors;

    public AkburaLayoutRecipeResult? GetRecipe(
        AkburaDocumentContext context,
        AkburaLayoutRecipeRequest request,
        CancellationToken cancellationToken = default)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));
        if (request == null) throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.RecipeId)) throw new ArgumentException("A recipe ID is required.", nameof(request));
        if (!string.Equals(Path.GetExtension(context.Document.FilePath), ".akbura", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Layout recipes require an .akbura document context.", nameof(context));
        }
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var recipe in GetRecipes())
        {
            if (recipe.Id == request.RecipeId)
            {
                return CheckRecipe(recipe, context, request.IsProjectComplete, cancellationToken);
            }
        }
        return null;
    }

    private AkburaLayoutRecipeResult CheckRecipe(
        AkburaLayoutRecipeDescriptor recipe, AkburaDocumentContext context, bool isProjectComplete, CancellationToken cancellationToken)
    {
        var code = AkburaLayoutRecipes.ReadExample(recipe);
        var snapshot = AkburaDocumentSnapshotIdentity.Capture(context);
        var notRun = new AkburaLayoutRecipeValidation(AkburaLayoutRecipeValidationStatus.NotRun, null, null, []);
        var compilation = context.Project.CSharpCompilation;
        if (!isProjectComplete || compilation.GetSpecialType(SpecialType.System_Object).TypeKind == TypeKind.Error)
        {
            return new(recipe, code, AkburaLayoutRecipeAvailability.Unknown,
                ["Project loading did not provide a complete semantic context."], [], snapshot, notRun);
        }

        using var reasons = ImmutableArrayBuilder<string>.Rent();
        foreach (var typeName in recipe.RequiredTypes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsExpectedType(compilation.GetTypeByMetadataName(typeName), typeName))
            {
                reasons.Add($"Required control '{typeName}' is missing or resolves to a different definition.");
            }
        }
        foreach (var variant in recipe.RequiredVariants)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!HasStandardBreakpoint(compilation, variant))
            {
                reasons.Add($"Required standard breakpoint '{variant.Name}' ({variant.TypeName}) is missing or incompatible.");
            }
        }

        if (reasons.Count == 0)
        {
            foreach (var requirement in recipe.RequiredUtilities)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var definitions = utilityCatalog.FindStandardUtilityDefinitions(
                    context, requirement.TargetType, requirement.Name, cancellationToken);
                if (!HasRequiredUtility(definitions.Utilities, requirement))
                {
                    reasons.Add($"Standard utility '{requirement.Name}' for '{requirement.TargetType}' is missing, incompatible, or shadowed.");
                }
            }
        }

        if (reasons.Count != 0)
        {
            return new(recipe, code, AkburaLayoutRecipeAvailability.Unavailable, reasons.ToImmutable(), [], snapshot, notRun);
        }

        var missingImports = FindMissingImports(context, recipe, cancellationToken);
        var example = CreateExampleContext(context, recipe.SuggestedFileName, code, cancellationToken);
        var analysis = diagnostics.AnalyzeDocument(example, cancellationToken: cancellationToken);
        var availability = missingImports.IsEmpty ? AkburaLayoutRecipeAvailability.Available : AkburaLayoutRecipeAvailability.RequiresImport;
        var validationStatus = AkburaLayoutRecipeValidationStatus.Passed;
        if (!analysis.IsComplete)
        {
            reasons.AddRange(analysis.IncompleteReasons);
            availability = AkburaLayoutRecipeAvailability.Unknown;
            validationStatus = AkburaLayoutRecipeValidationStatus.Incomplete;
        }
        else if (analysis.Diagnostics.Any(static diagnostic => diagnostic.Severity == AkburaDiagnosticSeverity.Error))
        {
            reasons.Add("The standalone example has errors in the current project context.");
            availability = AkburaLayoutRecipeAvailability.Unavailable;
            validationStatus = AkburaLayoutRecipeValidationStatus.Failed;
        }
        else if (!BindsToRequiredDefinitions(example, recipe, cancellationToken))
        {
            reasons.Add("The standalone example does not resolve to the required standard utility and breakpoint definitions.");
            availability = AkburaLayoutRecipeAvailability.Unavailable;
            validationStatus = AkburaLayoutRecipeValidationStatus.Failed;
        }
        else if (!missingImports.IsEmpty)
        {
            reasons.Add("The document is missing imports used by the reference component.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new(recipe, code, availability, reasons.ToImmutable(), missingImports, snapshot,
            new(validationStatus, example.Document.FilePath, analysis.CompletedLevel, analysis.Diagnostics));
    }

    private static bool HasRequiredUtility(
        ImmutableArray<AkburaUtilityDescriptor> definitions, AkburaLayoutUtilityRequirement requirement)
    {
        foreach (var utility in definitions)
        {
            if (MatchesRequirement(utility, requirement))
            {
                return true;
            }
        }
        return false;
    }

    private static bool MatchesRequirement(AkburaUtilityDescriptor utility, AkburaLayoutUtilityRequirement requirement)
    {
        if (utility.Name != requirement.Name || utility.DeclaredTargetType != requirement.DeclaredTargetType ||
            utility.Parameters.Length != requirement.ParameterTypes.Length)
        {
            return false;
        }
        for (var index = 0; index < utility.Parameters.Length; index++)
        {
            if (utility.Parameters[index].Type != requirement.ParameterTypes[index])
            {
                return false;
            }
        }
        return true;
    }

    private static bool HasStandardBreakpoint(Compilation compilation, AkburaLayoutVariantRequirement requirement)
    {
        var type = compilation.GetTypeByMetadataName(requirement.TypeName);
        return IsExpectedType(type, requirement.TypeName) &&
            IsExpectedType(type!.BaseType, "Akbura.Markup.BreakpointMarkupExtension") &&
            type.GetAttributes().Any(static attribute =>
                IsExpectedType(attribute.AttributeClass, "Akbura.Markup.UtilityVariantAttribute"));
    }

    private static bool IsExpectedType(INamedTypeSymbol? type, string fullName) =>
        type != null && type.TypeKind != TypeKind.Error &&
        type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) == fullName &&
        type.ContainingAssembly.Identity.Name == (fullName.StartsWith("Akbura.", StringComparison.Ordinal) ? "Akbura" : "Avalonia.Controls");

    private static ImmutableArray<string> FindMissingImports(
        AkburaDocumentContext context, AkburaLayoutRecipeDescriptor recipe, CancellationToken cancellationToken)
    {
        var model = context.Project.Compilation.GetSemanticModel(context.Document.SyntaxTree);
        var usings = model.GetAkburaUsingDirectives();
        using var missing = ImmutableArrayBuilder<string>.Rent(recipe.RequiredImports.Length);
        foreach (var import in recipe.RequiredImports)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = import.Substring("using ".Length, import.Length - "using ".Length - 1);
            var found = false;
            foreach (var directive in usings)
            {
                if (directive.Alias == null && directive.StaticKeyword.RawKind == 0 &&
                    directive.Name.ToFullString().Trim() == name)
                {
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                missing.Add(import);
            }
        }
        return missing.ToImmutable();
    }

    private static AkburaDocumentContext CreateExampleContext(
        AkburaDocumentContext context, string filename, string code, CancellationToken cancellationToken)
    {
        var original = context.Project;
        var folder = "_AkburaLayoutRecipe_" + Guid.NewGuid().ToString("N");
        var directory = original.Context.ProjectDirectory;
        var path = Path.GetFullPath(Path.Combine(directory, folder, filename));
        var document = AkburaDocumentSnapshot.Create(original.Id, new Uri(path), SourceText.From(code),
            original.Context.RootNamespace, directory, cancellationToken);
        var project = original.AddDocument(document);
        return new(context.Solution.WithProject(project), project, document);
    }

    private bool BindsToRequiredDefinitions(
        AkburaDocumentContext example, AkburaLayoutRecipeDescriptor recipe, CancellationToken cancellationToken)
    {
        var tree = example.Document.SyntaxTree;
        var model = example.Project.Compilation.GetSemanticModel(tree);
        var boundVariants = PooledHashSet<string>.GetInstance();
        var boundTypes = PooledHashSet<string>.GetInstance();
        try
        {
            foreach (var node in tree.GetRootSyntax().DescendantNodesAndSelf())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (node is MarkupElementSyntax element)
                {
                    if (model.GetSymbolInfo(element).Symbol is not IMarkupComponentSymbol component ||
                        model.GetMarkupComponentReferenceType(component) is not { } componentType)
                    {
                        return false;
                    }

                    var name = componentType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
                    if (!recipe.RequiredTypes.Contains(name) || !IsExpectedType(componentType, name))
                    {
                        return false;
                    }

                    boundTypes.Add(name);
                    continue;
                }
                if (node is not TailwindAttributeSyntax attribute)
                {
                    continue;
                }

                if (model.GetOperation(attribute) is not ITailwindUtilityAttributeOperation operation ||
                    operation.Utilities.IsEmpty || operation.HasErrors)
                {
                    return false;
                }
                foreach (var utility in operation.Utilities)
                {
                    if (!utilityCatalog.IsStandardUtility(example.Project, utility, cancellationToken))
                    {
                        return false;
                    }
                }

                if (operation.HasCondition)
                {
                    if (operation.ConditionMarkupExtension?.ExtensionType.Symbol is not INamedTypeSymbol type ||
                        !recipe.RequiredVariants.Any(variant => IsExpectedType(type, variant.TypeName)))
                    {
                        return false;
                    }
                    boundVariants.Add(type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat));
                }
            }
            return boundTypes.Count == recipe.RequiredTypes.Length && boundVariants.Count == recipe.RequiredVariants.Length;
        }
        finally
        {
            boundTypes.Free();
            boundVariants.Free();
        }
    }
}

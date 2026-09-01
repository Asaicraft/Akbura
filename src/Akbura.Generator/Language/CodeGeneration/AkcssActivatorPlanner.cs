using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using RoslynSymbol = Microsoft.CodeAnalysis.ISymbol;

namespace Akbura.Language.CodeGeneration;

/// <summary>
/// Semantic input required to plan AKCSS activators for one generated element.
/// </summary>
internal readonly struct AkcssActivatorElementInput
{
    public AkcssActivatorElementInput(
        int id,
        IMarkupComponentSymbol symbol,
        bool requiresLocalMarkupExtensionContext)
    {
        if (id < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(id));
        }

        Id = id;
        Symbol = symbol ?? throw new ArgumentNullException(nameof(symbol));
        RequiresLocalMarkupExtensionContext = requiresLocalMarkupExtensionContext;
    }

    public int Id { get; }

    public IMarkupComponentSymbol Symbol { get; }

    public bool RequiresLocalMarkupExtensionContext { get; }
}

/// <summary>
/// Builds compact AKCSS generation plans without creating emitted C# expressions.
/// </summary>
internal static class AkcssActivatorPlanner
{
    public static AkcssComponentActivatorPlan Create(
        AkburaSemanticModel semanticModel,
        ImmutableArray<AkcssActivatorElementInput> elements,
        IReadOnlyDictionary<AkburaSyntax, string> moduleTypeNames)
    {
        return Create(semanticModel, elements.AsSpan(), moduleTypeNames);
    }

    public static AkcssComponentActivatorPlan Create(
        AkburaSemanticModel semanticModel,
        ReadOnlySpan<AkcssActivatorElementInput> elements,
        IReadOnlyDictionary<AkburaSyntax, string> moduleTypeNames)
    {
        if (semanticModel == null)
        {
            throw new ArgumentNullException(nameof(semanticModel));
        }

        if (moduleTypeNames == null)
        {
            throw new ArgumentNullException(nameof(moduleTypeNames));
        }

        using var planner = new Planner(semanticModel, moduleTypeNames);
        return planner.Create(elements);
    }

    private ref struct Planner
    {
        private readonly AkburaSemanticModel _semanticModel;
        private readonly IReadOnlyDictionary<AkburaSyntax, string> _moduleTypeNames;
        private readonly CSharpCompilation _compilation;
        private readonly INamedTypeSymbol? _controlType;
        private readonly INamedTypeSymbol? _bindingBaseType;
        private readonly INamedTypeSymbol? _observableType;
        private readonly INamedTypeSymbol? _serviceProviderType;
        private readonly INamedTypeSymbol? _bindingPriorityType;
        private readonly ITypeSymbol _objectType;
        private readonly ITypeSymbol _booleanType;

        private ImmutableArrayBuilder<AkcssElementActivatorPlan> _elementActivators;
        private ImmutableArrayBuilder<AkcssActivatorPlan> _activators;
        private ImmutableArrayBuilder<AkcssClassCachePlan> _classCaches;
        private ImmutableArrayBuilder<AkcssUtilityApplicationPlan> _applications;
        private ImmutableArrayBuilder<AkcssUtilityApplicationCachePlan> _applicationCaches;
        private ImmutableArrayBuilder<AkcssUtilityCandidatePlan> _candidates;
        private ImmutableArrayBuilder<AkcssUtilityValueSourcePlan> _valueSources;
        private ImmutableArrayBuilder<AkcssMarkupExtensionSlotPlan> _slots;

        public Planner(
            AkburaSemanticModel semanticModel,
            IReadOnlyDictionary<AkburaSyntax, string> moduleTypeNames)
        {
            _semanticModel = semanticModel;
            _moduleTypeNames = moduleTypeNames;
            _compilation = semanticModel.Compilation.CSharpCompilation;
            _controlType = _compilation.GetTypeByMetadataName("Avalonia.Controls.Control");
            _bindingBaseType = _compilation.GetTypeByMetadataName("Avalonia.Data.BindingBase");
            _observableType = _compilation.GetTypeByMetadataName("System.IObservable`1");
            _serviceProviderType = _compilation.GetTypeByMetadataName("System.IServiceProvider");
            _bindingPriorityType = _compilation.GetTypeByMetadataName("Avalonia.Data.BindingPriority");
            _objectType = _compilation.GetSpecialType(SpecialType.System_Object);
            _booleanType = _compilation.GetSpecialType(SpecialType.System_Boolean);

            _elementActivators = ImmutableArrayBuilder<AkcssElementActivatorPlan>.Rent();
            _activators = ImmutableArrayBuilder<AkcssActivatorPlan>.Rent();
            _classCaches = ImmutableArrayBuilder<AkcssClassCachePlan>.Rent();
            _applications = ImmutableArrayBuilder<AkcssUtilityApplicationPlan>.Rent();
            _applicationCaches = ImmutableArrayBuilder<AkcssUtilityApplicationCachePlan>.Rent();
            _candidates = ImmutableArrayBuilder<AkcssUtilityCandidatePlan>.Rent();
            _valueSources = ImmutableArrayBuilder<AkcssUtilityValueSourcePlan>.Rent();
            _slots = ImmutableArrayBuilder<AkcssMarkupExtensionSlotPlan>.Rent();
        }

        public AkcssComponentActivatorPlan Create(ReadOnlySpan<AkcssActivatorElementInput> elements)
        {
            for (var i = 0; i < elements.Length; i++)
            {
                BuildElement(elements[i]);
            }

            return new AkcssComponentActivatorPlan(
                _elementActivators.ToImmutable(),
                _activators.ToImmutable(),
                _classCaches.ToImmutable(),
                _applications.ToImmutable(),
                _applicationCaches.ToImmutable(),
                _candidates.ToImmutable(),
                _valueSources.ToImmutable(),
                _slots.ToImmutable(),
                _bindingPriorityType);
        }

        public void Dispose()
        {
            _elementActivators.Dispose();
            _activators.Dispose();
            _classCaches.Dispose();
            _applications.Dispose();
            _applicationCaches.Dispose();
            _candidates.Dispose();
            _valueSources.Dispose();
            _slots.Dispose();
        }

        private void BuildElement(in AkcssActivatorElementInput element)
        {
            var activatorStart = _activators.Count;
            var slotStart = _slots.Count;
            var operations = element.Symbol.AttributeOperations;
            var isControlTarget = IsControlElement(element.Symbol);

            for (var sourceOrder = 0; sourceOrder < operations.Length; sourceOrder++)
            {
                var operation = operations[sourceOrder];
                if (operation.HasErrors)
                {
                    continue;
                }

                if (operation is IMarkupPropertySetterOperation propertySetter)
                {
                    AddAppliedClasses(propertySetter);
                }

                if (operation is ITailwindUtilityAttributeOperation utilityOperation)
                {
                    AddUtilityCandidate(element, utilityOperation, sourceOrder, isControlTarget);
                }
            }

            _elementActivators.Add(new AkcssElementActivatorPlan(
                element.Id,
                new AkcssPlanRange(
                    activatorStart,
                    _activators.Count - activatorStart),
                new AkcssPlanRange(
                    slotStart,
                    _slots.Count - slotStart)));
        }

        private void AddAppliedClasses(IMarkupPropertySetterOperation operation)
        {
            var styles = operation.AppliedAkcssSymbols;
            for (var i = 0; i < styles.Length; i++)
            {
                if (!TryCreateStyleReference(styles[i], out var reference))
                {
                    continue;
                }

                var cacheIndex = GetOrAddClassCache(reference);
                _activators.Add(AkcssActivatorPlan.CreateClass(cacheIndex));
            }
        }

        private int GetOrAddClassCache(in AkcssStyleReferencePlan reference)
        {
            var caches = _classCaches.WrittenSpan;
            for (var i = 0; i < caches.Length; i++)
            {
                if (StyleReferencesEqual(caches[i].Style, reference))
                {
                    return i;
                }
            }

            var index = _classCaches.Count;
            _classCaches.Add(new AkcssClassCachePlan(index, reference));
            return index;
        }

        private void AddUtilityCandidate(
            in AkcssActivatorElementInput element,
            ITailwindUtilityAttributeOperation operation,
            int sourceOrder,
            bool isControlTarget)
        {
            var utilities = operation.Utilities;
            using var applications = ImmutableArrayBuilder<AkcssUtilityApplicationPlan>.Rent(utilities.Length);

            for (var i = 0; i < utilities.Length; i++)
            {
                var utility = utilities[i];
                if (TryCreateStyleReference(utility, out var reference))
                {
                    applications.Add(new AkcssUtilityApplicationPlan(utility, reference));
                }
            }

            if (applications.Count == 0)
            {
                return;
            }

            var applicationCacheId = GetOrAddApplicationCache(applications.WrittenSpan);
            var firstUtility = applications.WrittenSpan[0].Utility;
            var valueSourceStart = _valueSources.Count;
            AddArgumentValueSources(element, operation, firstUtility, isControlTarget);

            var valueSources = new AkcssPlanRange(
                valueSourceStart,
                _valueSources.Count - valueSourceStart);
            var variantValueSourceIndex = AddVariantValueSource(element, operation, isControlTarget);
            var candidateIndex = _candidates.Count;

            _candidates.Add(new AkcssUtilityCandidatePlan(
                operation.UtilityName,
                sourceOrder,
                applicationCacheId,
                valueSources,
                variantValueSourceIndex,
                operation.HasCondition && operation.ConditionMarkupExtension == null,
                operation.ConditionText,
                operation.ConditionOperation,
                operation.Variant,
                operation.BindingPriority));
            _activators.Add(AkcssActivatorPlan.CreateCandidate(candidateIndex));
        }

        private int GetOrAddApplicationCache(
            scoped ReadOnlySpan<AkcssUtilityApplicationPlan> applications)
        {
            var caches = _applicationCaches.WrittenSpan;
            var existingApplications = _applications.WrittenSpan;

            for (var i = 0; i < caches.Length; i++)
            {
                var cache = caches[i];
                var range = cache.Applications;

                if (range.Length != applications.Length)
                {
                    continue;
                }

                var isMatch = true;
                for (var j = 0; j < range.Length; j++)
                {
                    var existing = existingApplications[range.Start + j].Reference;
                    var current = applications[j].Reference;

                    if (!StyleReferencesEqual(existing, current))
                    {
                        isMatch = false;
                        break;
                    }
                }

                if (isMatch)
                {
                    return cache.Id;
                }
            }

            var id = _applicationCaches.Count;
            var start = _applications.Count;
            _applications.AddRange(applications);
            _applicationCaches.Add(new AkcssUtilityApplicationCachePlan(
                id,
                new AkcssPlanRange(start, applications.Length)));
            return id;
        }

        private void AddArgumentValueSources(
            in AkcssActivatorElementInput element,
            ITailwindUtilityAttributeOperation operation,
            ITailwindUtilitySymbol utility,
            bool isControlTarget)
        {
            var arguments = operation.Arguments;
            var parameters = utility.Parameters;

            for (var i = 0; i < arguments.Length; i++)
            {
                if (i >= parameters.Length || parameters[i].Type.Symbol is not ITypeSymbol expectedType)
                {
                    continue;
                }

                var argument = arguments[i];

                if (argument.MarkupExtension is { } extension)
                {
                    AddMarkupExtensionValueSource(
                        element,
                        expectedType,
                        argument,
                        extension,
                        GetArgumentExtensionSyntax(argument),
                        isControlTarget,
                        hasPriorityMember: false,
                        priorityMember: null);
                }
                else
                {
                    _valueSources.Add(new AkcssUtilityValueSourcePlan(
                        AkcssUtilityValueSourceKind.Direct,
                        expectedType,
                        observableElementType: null,
                        argument,
                        extension: null,
                        markupExtensionSlotId: -1,
                        isControlTarget,
                        hasPriorityMember: false,
                        priorityMember: null,
                        recreateOnRefresh: !argument.ValueOperation.ConstantValue.HasValue,
                        useFactoryMethod: false));
                }
            }
        }

        private int AddVariantValueSource(
            in AkcssActivatorElementInput element,
            ITailwindUtilityAttributeOperation operation,
            bool isControlTarget)
        {
            if (operation.ConditionMarkupExtension is not { } extension)
            {
                return -1;
            }

            var hasPriorityMember = operation.BindingPriority.Source ==
                    TailwindUtilityBindingPrioritySource.Member &&
                operation.BindingPriority.Member.Symbol != null;
            var index = _valueSources.Count;

            AddMarkupExtensionValueSource(
                element,
                _booleanType,
                argument: default,
                extension,
                GetConditionExtensionSyntax(operation),
                isControlTarget,
                hasPriorityMember,
                operation.BindingPriority.Member.Symbol);

            return index;
        }

        private void AddMarkupExtensionValueSource(
            in AkcssActivatorElementInput element,
            ITypeSymbol expectedType,
            in TailwindUtilityArgument argument,
            MarkupExtensionValue extension,
            AkburaSyntax syntax,
            bool isControlTarget,
            bool hasPriorityMember,
            RoslynSymbol? priorityMember)
        {
            var kind = ClassifyValueSource(extension, out var observableElementType);
            var slotId = _slots.Count;
            var needsFactory = !element.RequiresLocalMarkupExtensionContext;
            var factoryValueType = GetFactoryValueType(kind, expectedType, observableElementType);

            _slots.Add(new AkcssMarkupExtensionSlotPlan(
                slotId,
                element.Id,
                extension,
                syntax,
                factoryValueType,
                isControlTarget,
                needsTargetProperty: kind == AkcssUtilityValueSourceKind.Binding ||
                    RequiresTargetProperty(extension),
                needsFactoryMethod: needsFactory,
                priorityMember));
            _valueSources.Add(new AkcssUtilityValueSourcePlan(
                kind,
                expectedType,
                observableElementType,
                argument,
                extension,
                slotId,
                isControlTarget,
                hasPriorityMember,
                priorityMember,
                recreateOnRefresh: extension.IsUpdateDependent,
                useFactoryMethod: needsFactory));
        }

        private ITypeSymbol GetFactoryValueType(
            AkcssUtilityValueSourceKind kind,
            ITypeSymbol expectedType,
            ITypeSymbol? observableElementType)
        {
            switch (kind)
            {
                case AkcssUtilityValueSourceKind.Direct:
                    return expectedType;

                case AkcssUtilityValueSourceKind.Object:
                    return _objectType.WithNullableAnnotation(NullableAnnotation.Annotated);

                case AkcssUtilityValueSourceKind.Observable:
                case AkcssUtilityValueSourceKind.ObservableObject:
                    Debug.Assert(_observableType != null);
                    Debug.Assert(observableElementType != null);
                    var factoryElementType = kind == AkcssUtilityValueSourceKind.ObservableObject
                        ? _objectType.WithNullableAnnotation(NullableAnnotation.Annotated)
                        : observableElementType!;
                    return _observableType!
                        .Construct(factoryElementType)
                        .WithNullableAnnotation(NullableAnnotation.Annotated);

                case AkcssUtilityValueSourceKind.Binding:
                    Debug.Assert(_bindingBaseType != null);
                    return _bindingBaseType!;

                default:
                    throw new InvalidOperationException("Unexpected AKCSS value-source kind.");
            }
        }

        private AkcssUtilityValueSourceKind ClassifyValueSource(
            MarkupExtensionValue extension,
            out ITypeSymbol? observableElementType)
        {
            observableElementType = null;

            if (extension.ResultType.Symbol is not ITypeSymbol resultType)
            {
                return AkcssUtilityValueSourceKind.Object;
            }

            if (IsBindingBaseType(resultType))
            {
                return AkcssUtilityValueSourceKind.Binding;
            }

            if (TryGetObservableElementType(resultType, out observableElementType))
            {
                return observableElementType.SpecialType == SpecialType.System_Object
                    ? AkcssUtilityValueSourceKind.ObservableObject
                    : AkcssUtilityValueSourceKind.Observable;
            }

            if (resultType.SpecialType == SpecialType.System_Object ||
                resultType.TypeKind is TypeKind.Dynamic or TypeKind.Error)
            {
                return AkcssUtilityValueSourceKind.Object;
            }

            return AkcssUtilityValueSourceKind.Direct;
        }

        private bool IsControlElement(IMarkupComponentSymbol symbol)
        {
            var componentType = symbol.ComponentType ?? symbol.AkburaComponent?.ComponentType;
            return componentType != null &&
                _controlType != null &&
                _compilation.ClassifyConversion(componentType, _controlType).IsImplicit;
        }

        private bool IsBindingBaseType(ITypeSymbol type)
        {
            return _bindingBaseType != null &&
                _compilation.ClassifyConversion(type, _bindingBaseType).IsImplicit;
        }

        private bool TryGetObservableElementType(
            ITypeSymbol type,
            out ITypeSymbol elementType)
        {
            if (_observableType != null)
            {
                if (type is INamedTypeSymbol namedType &&
                    SymbolEqualityComparer.Default.Equals(namedType.OriginalDefinition, _observableType))
                {
                    elementType = namedType.TypeArguments[0];
                    return true;
                }

                var interfaces = type.AllInterfaces;
                for (var i = 0; i < interfaces.Length; i++)
                {
                    var interfaceType = interfaces[i];
                    if (SymbolEqualityComparer.Default.Equals(interfaceType.OriginalDefinition, _observableType))
                    {
                        elementType = interfaceType.TypeArguments[0];
                        return true;
                    }
                }
            }

            elementType = null!;
            return false;
        }

        private bool RequiresTargetProperty(MarkupExtensionValue extension)
        {
            if (extension.Binding != null)
            {
                return true;
            }

            if (_serviceProviderType != null &&
                extension.ProvideValueMethod.Symbol is IMethodSymbol { Parameters.Length: 1 } provideValue &&
                SymbolEqualityComparer.Default.Equals(
                    provideValue.Parameters[0].Type,
                    _serviceProviderType))
            {
                return true;
            }

            var arguments = extension.Arguments;
            for (var i = 0; i < arguments.Length; i++)
            {
                if (arguments[i].NestedValue is { } nested && RequiresTargetProperty(nested))
                {
                    return true;
                }
            }

            var properties = extension.Properties;
            for (var i = 0; i < properties.Length; i++)
            {
                if (properties[i].NestedValue is { } nested && RequiresTargetProperty(nested))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryCreateStyleReference(
            IAkcssSymbol style,
            out AkcssStyleReferencePlan reference)
        {
            if (style is IMetadataAkcssSymbol metadataStyle)
            {
                if (metadataStyle.RuntimeStyleIndex < 0)
                {
                    reference = default;
                    return false;
                }

                reference = AkcssStyleReferencePlan.CreateMetadata(
                    metadataStyle.MetadataModule.RuntimeModuleType,
                    metadataStyle.RuntimeStyleIndex);
                return true;
            }

            if (style.DeclarationSyntax is not { } declarationSyntax)
            {
                reference = default;
                return false;
            }

            var moduleSyntax = GetAkcssModuleSyntax(declarationSyntax);
            if (moduleSyntax == null ||
                _semanticModel.GetDeclaredSymbol(moduleSyntax) is not IAkcssModuleSymbol module)
            {
                reference = default;
                return false;
            }

            var styleIndex = FindStyleIndex(module, declarationSyntax);
            if (styleIndex < 0 || !TryGetModuleTypeName(module, out var moduleTypeName))
            {
                reference = default;
                return false;
            }

            reference = AkcssStyleReferencePlan.CreateGenerated(moduleTypeName, styleIndex);
            return true;
        }

        private static int FindStyleIndex(
            IAkcssModuleSymbol module,
            AkburaSyntax declarationSyntax)
        {
            var symbols = module.AkcssSymbols;
            for (var i = 0; i < symbols.Length; i++)
            {
                if (symbols[i].DeclarationSyntax is not { } candidateSyntax)
                {
                    continue;
                }

                if (ReferenceEquals(candidateSyntax, declarationSyntax) ||
                    ReferenceEquals(candidateSyntax.Root, declarationSyntax.Root) &&
                    candidateSyntax.Kind == declarationSyntax.Kind &&
                    candidateSyntax.FullSpan == declarationSyntax.FullSpan)
                {
                    return i;
                }
            }

            return -1;
        }

        private bool TryGetModuleTypeName(
            IAkcssModuleSymbol module,
            out string typeName)
        {
            if (module.DeclaringSyntax is not { } declarationSyntax)
            {
                typeName = string.Empty;
                return false;
            }

            if (_moduleTypeNames.TryGetValue(declarationSyntax, out typeName!) &&
                !string.IsNullOrWhiteSpace(typeName))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(module.Path))
            {
                typeName = string.Empty;
                return false;
            }

            var syntaxTrees = _semanticModel.Compilation.GetAkcssSyntaxTreesByLogicalName(module.Path!);
            for (var i = 0; i < syntaxTrees.Length; i++)
            {
                var syntaxTree = syntaxTrees[i];
                if (!ReferenceEquals(syntaxTree.GetRootSyntax(), declarationSyntax.Root))
                {
                    continue;
                }

                var sourcePath = GetAkcssSourcePath(syntaxTree, _semanticModel.Compilation);
                typeName = AkcssGeneratedModuleNames.GetFullyQualifiedTypeName(sourcePath);
                return true;
            }

            typeName = string.Empty;
            return false;
        }

        private static string GetAkcssSourcePath(
            AkcssSyntaxTree syntaxTree,
            AkburaCompilation compilation)
        {
            if (TryGetProjectRelativePath(syntaxTree, compilation, out var sourcePath))
            {
                return sourcePath;
            }

            var references = compilation.CompilationReferences;
            for (var i = 0; i < references.Length; i++)
            {
                if (TryGetProjectRelativePath(syntaxTree, references[i].Compilation, out sourcePath))
                {
                    return sourcePath;
                }
            }

            var path = !string.IsNullOrWhiteSpace(syntaxTree.FilePath)
                ? syntaxTree.FilePath
                : syntaxTree.LogicalName;
            return AkcssGeneratedModuleNames.NormalizeSourcePath(path);
        }

        private static bool TryGetProjectRelativePath(
            AkcssSyntaxTree syntaxTree,
            AkburaCompilation compilation,
            out string sourcePath)
        {
            sourcePath = string.Empty;
            if (!ContainsSyntaxTree(compilation.AkcssSyntaxTrees, syntaxTree) ||
                string.IsNullOrWhiteSpace(compilation.ProjectDirectory) ||
                string.IsNullOrWhiteSpace(syntaxTree.FilePath))
            {
                return false;
            }

            var projectPath = Path.GetFullPath(compilation.ProjectDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullSourcePath = Path.GetFullPath(syntaxTree.FilePath);
            var projectPrefix = projectPath + Path.DirectorySeparatorChar;
            if (!fullSourcePath.StartsWith(projectPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            sourcePath = AkcssGeneratedModuleNames.NormalizeSourcePath(
                fullSourcePath.Substring(projectPrefix.Length));
            return true;
        }

        private static bool ContainsSyntaxTree(
            ImmutableArray<AkcssSyntaxTree> syntaxTrees,
            AkcssSyntaxTree syntaxTree)
        {
            for (var i = 0; i < syntaxTrees.Length; i++)
            {
                if (ReferenceEquals(syntaxTrees[i], syntaxTree))
                {
                    return true;
                }
            }

            return false;
        }

        private static AkburaSyntax? GetAkcssModuleSyntax(AkburaSyntax syntax)
        {
            for (var current = syntax; current != null; current = current.Parent)
            {
                if (current is AkcssDocumentSyntax or InlineAkcssBlockSyntax)
                {
                    return current;
                }
            }

            return null;
        }

        private static AkburaSyntax GetArgumentExtensionSyntax(in TailwindUtilityArgument argument)
        {
            return argument.Syntax is TailwindMarkupExtensionSegmentSyntax segment
                ? segment.Extension
                : argument.Syntax;
        }

        private static AkburaSyntax GetConditionExtensionSyntax(
            ITailwindUtilityAttributeOperation operation)
        {
            return operation.Syntax is TailwindFullAttributeSyntax
            {
                Prefix: MarkupExtensionConditionalPrefixSyntax prefix,
            }
                ? prefix.Extension
                : operation.Syntax;
        }

        private static bool StyleReferencesEqual(
            in AkcssStyleReferencePlan left,
            in AkcssStyleReferencePlan right)
        {
            if (left.Kind != right.Kind || left.StyleIndex != right.StyleIndex)
            {
                return false;
            }

            return left.Kind switch
            {
                AkcssStyleReferenceKind.MetadataModule => SymbolEqualityComparer.Default.Equals(
                    left.RuntimeModuleType,
                    right.RuntimeModuleType),
                AkcssStyleReferenceKind.GeneratedModule => string.Equals(
                    left.GeneratedModuleTypeName,
                    right.GeneratedModuleTypeName,
                    StringComparison.Ordinal),
                _ => false,
            };
        }
    }
}

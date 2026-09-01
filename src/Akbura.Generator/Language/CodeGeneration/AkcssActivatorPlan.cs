using Akbura.Language.Binder;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Immutable;
using System.Globalization;
using RoslynSymbol = Microsoft.CodeAnalysis.ISymbol;

namespace Akbura.Language.CodeGeneration;

internal readonly struct AkcssPlanRange
{
    public AkcssPlanRange(int start, int length)
    {
        Start = start;
        Length = length;
    }

    public int Start { get; }

    public int Length { get; }

    public bool IsEmpty => Length == 0;
}

internal readonly struct AkcssElementActivatorPlan
{
    public AkcssElementActivatorPlan(
        int elementId,
        AkcssPlanRange activators,
        AkcssPlanRange markupExtensionSlots)
    {
        ElementId = elementId;
        Activators = activators;
        MarkupExtensionSlots = markupExtensionSlots;
    }

    public int ElementId { get; }

    public AkcssPlanRange Activators { get; }

    public AkcssPlanRange MarkupExtensionSlots { get; }
}

internal enum AkcssStyleReferenceKind : byte
{
    None,
    MetadataModule,
    GeneratedModule,
}

internal readonly struct AkcssStyleReferencePlan
{
    private AkcssStyleReferencePlan(
        AkcssStyleReferenceKind kind,
        INamedTypeSymbol? runtimeModuleType,
        string? generatedModuleTypeName,
        int styleIndex)
    {
        Kind = kind;
        RuntimeModuleType = runtimeModuleType;
        GeneratedModuleTypeName = generatedModuleTypeName;
        StyleIndex = styleIndex;
    }

    public AkcssStyleReferenceKind Kind { get; }

    public INamedTypeSymbol? RuntimeModuleType { get; }

    public string? GeneratedModuleTypeName { get; }

    public int StyleIndex { get; }

    public bool IsValid => Kind switch
    {
        AkcssStyleReferenceKind.MetadataModule => RuntimeModuleType != null && StyleIndex >= 0,
        AkcssStyleReferenceKind.GeneratedModule =>
            !string.IsNullOrWhiteSpace(GeneratedModuleTypeName) && StyleIndex >= 0,
        _ => false,
    };

    public static AkcssStyleReferencePlan CreateMetadata(
        INamedTypeSymbol runtimeModuleType,
        int styleIndex)
    {
        return new AkcssStyleReferencePlan(
            AkcssStyleReferenceKind.MetadataModule,
            runtimeModuleType ?? throw new ArgumentNullException(nameof(runtimeModuleType)),
            generatedModuleTypeName: null,
            styleIndex);
    }

    public static AkcssStyleReferencePlan CreateGenerated(
        string generatedModuleTypeName,
        int styleIndex)
    {
        return new AkcssStyleReferencePlan(
            AkcssStyleReferenceKind.GeneratedModule,
            runtimeModuleType: null,
            generatedModuleTypeName ?? throw new ArgumentNullException(nameof(generatedModuleTypeName)),
            styleIndex);
    }
}

internal enum AkcssActivatorKind : byte
{
    None,
    Class,
    UtilityCandidate,
}

internal readonly struct AkcssActivatorPlan
{
    private AkcssActivatorPlan(AkcssActivatorKind kind, int index)
    {
        Kind = kind;
        Index = index;
    }

    public AkcssActivatorKind Kind { get; }

    public int Index { get; }

    public bool IsValid => Kind != AkcssActivatorKind.None && Index >= 0;

    public static AkcssActivatorPlan CreateClass(int index)
    {
        return new AkcssActivatorPlan(AkcssActivatorKind.Class, index);
    }

    public static AkcssActivatorPlan CreateCandidate(int index)
    {
        return new AkcssActivatorPlan(AkcssActivatorKind.UtilityCandidate, index);
    }
}

internal readonly struct AkcssClassCachePlan
{
    public AkcssClassCachePlan(int id, AkcssStyleReferencePlan style)
    {
        Id = id;
        Style = style;
    }

    public int Id { get; }

    public AkcssStyleReferencePlan Style { get; }
}

internal readonly struct AkcssUtilityApplicationPlan
{
    public AkcssUtilityApplicationPlan(
        ITailwindUtilitySymbol utility,
        AkcssStyleReferencePlan reference)
    {
        Utility = utility ?? throw new ArgumentNullException(nameof(utility));
        Reference = reference;
    }

    public ITailwindUtilitySymbol Utility { get; }

    public AkcssStyleReferencePlan Reference { get; }
}

internal readonly struct AkcssUtilityApplicationCachePlan
{
    public AkcssUtilityApplicationCachePlan(int id, AkcssPlanRange applications)
    {
        Id = id;
        Applications = applications;
    }

    public int Id { get; }

    public AkcssPlanRange Applications { get; }
}

internal readonly struct AkcssUtilityCandidatePlan
{
    public AkcssUtilityCandidatePlan(
        string conflictKey,
        int sourceOrder,
        int applicationCacheId,
        AkcssPlanRange valueSources,
        int variantValueSourceIndex,
        bool hasCondition,
        string? conditionText,
        CSharpOperationDefinition conditionOperation,
        TailwindUtilityVariant variant,
        TailwindUtilityBindingPriority bindingPriority)
    {
        ConflictKey = conflictKey ?? throw new ArgumentNullException(nameof(conflictKey));
        SourceOrder = sourceOrder;
        ApplicationCacheId = applicationCacheId;
        ValueSources = valueSources;
        VariantValueSourceIndex = variantValueSourceIndex;
        HasCondition = hasCondition;
        ConditionText = conditionText;
        ConditionOperation = conditionOperation;
        Variant = variant;
        BindingPriority = bindingPriority;
    }

    public string ConflictKey { get; }

    public int SourceOrder { get; }

    public int ApplicationCacheId { get; }

    public AkcssPlanRange ValueSources { get; }

    public int VariantValueSourceIndex { get; }

    public bool HasCondition { get; }

    public string? ConditionText { get; }

    public CSharpOperationDefinition ConditionOperation { get; }

    public TailwindUtilityVariant Variant { get; }

    public TailwindUtilityBindingPriority BindingPriority { get; }
}

internal enum AkcssUtilityValueSourceKind : byte
{
    None,
    Direct,
    Object,
    Observable,
    ObservableObject,
    Binding,
}

internal readonly struct AkcssUtilityValueSourcePlan
{
    public AkcssUtilityValueSourcePlan(
        AkcssUtilityValueSourceKind kind,
        ITypeSymbol expectedType,
        ITypeSymbol? observableElementType,
        TailwindUtilityArgument argument,
        MarkupExtensionValue? extension,
        int markupExtensionSlotId,
        bool isControlTarget,
        bool hasPriorityMember,
        RoslynSymbol? priorityMember,
        bool recreateOnRefresh,
        bool useFactoryMethod)
    {
        Kind = kind;
        ExpectedType = expectedType ?? throw new ArgumentNullException(nameof(expectedType));
        ObservableElementType = observableElementType;
        Argument = argument;
        Extension = extension;
        MarkupExtensionSlotId = markupExtensionSlotId;
        IsControlTarget = isControlTarget;
        HasPriorityMember = hasPriorityMember;
        PriorityMember = priorityMember;
        RecreateOnRefresh = recreateOnRefresh;
        UseFactoryMethod = useFactoryMethod;
    }

    public AkcssUtilityValueSourceKind Kind { get; }

    public ITypeSymbol ExpectedType { get; }

    public ITypeSymbol? ObservableElementType { get; }

    public TailwindUtilityArgument Argument { get; }

    public MarkupExtensionValue? Extension { get; }

    public int MarkupExtensionSlotId { get; }

    public bool IsControlTarget { get; }

    public bool HasPriorityMember { get; }

    public RoslynSymbol? PriorityMember { get; }

    public bool RecreateOnRefresh { get; }

    public bool UseFactoryMethod { get; }
}

internal readonly struct AkcssMarkupExtensionSlotPlan
{
    public AkcssMarkupExtensionSlotPlan(
        int id,
        int elementId,
        MarkupExtensionValue extension,
        AkburaSyntax syntax,
        ITypeSymbol factoryValueType,
        bool isControlTarget,
        bool needsTargetProperty,
        bool needsFactoryMethod,
        RoslynSymbol? priorityMember)
    {
        Id = id;
        ElementId = elementId;
        Extension = extension ?? throw new ArgumentNullException(nameof(extension));
        Syntax = syntax ?? throw new ArgumentNullException(nameof(syntax));
        FactoryValueType = factoryValueType ?? throw new ArgumentNullException(nameof(factoryValueType));
        IsControlTarget = isControlTarget;
        NeedsTargetProperty = needsTargetProperty;
        NeedsFactoryMethod = needsFactoryMethod;
        PriorityMember = priorityMember;
        PropertyName = "s_akcssValueProperty" + id.ToString(CultureInfo.InvariantCulture);
        FactoryName = "__CreateAkcssValue" + id.ToString(CultureInfo.InvariantCulture);
    }

    public int Id { get; }

    public int ElementId { get; }

    public MarkupExtensionValue Extension { get; }

    public AkburaSyntax Syntax { get; }

    public ITypeSymbol FactoryValueType { get; }

    public bool IsControlTarget { get; }

    public bool NeedsTargetProperty { get; }

    public bool NeedsFactoryMethod { get; }

    public RoslynSymbol? PriorityMember { get; }

    public bool HasPriorityMember => PriorityMember != null;

    public string PropertyName { get; }

    public string FactoryName { get; }
}

internal readonly struct AkcssComponentActivatorPlan
{
    public AkcssComponentActivatorPlan(
        ImmutableArray<AkcssElementActivatorPlan> elements,
        ImmutableArray<AkcssActivatorPlan> activators,
        ImmutableArray<AkcssClassCachePlan> classCaches,
        ImmutableArray<AkcssUtilityApplicationPlan> applications,
        ImmutableArray<AkcssUtilityApplicationCachePlan> applicationCaches,
        ImmutableArray<AkcssUtilityCandidatePlan> candidates,
        ImmutableArray<AkcssUtilityValueSourcePlan> valueSources,
        ImmutableArray<AkcssMarkupExtensionSlotPlan> markupExtensionSlots,
        INamedTypeSymbol? bindingPriorityType)
    {
        Elements = elements.IsDefault
            ? ImmutableArray<AkcssElementActivatorPlan>.Empty
            : elements;
        Activators = activators.IsDefault
            ? ImmutableArray<AkcssActivatorPlan>.Empty
            : activators;
        ClassCaches = classCaches.IsDefault
            ? ImmutableArray<AkcssClassCachePlan>.Empty
            : classCaches;
        Applications = applications.IsDefault
            ? ImmutableArray<AkcssUtilityApplicationPlan>.Empty
            : applications;
        ApplicationCaches = applicationCaches.IsDefault
            ? ImmutableArray<AkcssUtilityApplicationCachePlan>.Empty
            : applicationCaches;
        Candidates = candidates.IsDefault
            ? ImmutableArray<AkcssUtilityCandidatePlan>.Empty
            : candidates;
        ValueSources = valueSources.IsDefault
            ? ImmutableArray<AkcssUtilityValueSourcePlan>.Empty
            : valueSources;
        MarkupExtensionSlots = markupExtensionSlots.IsDefault
            ? ImmutableArray<AkcssMarkupExtensionSlotPlan>.Empty
            : markupExtensionSlots;
        BindingPriorityType = bindingPriorityType;
    }

    public ImmutableArray<AkcssElementActivatorPlan> Elements { get; }

    public ImmutableArray<AkcssActivatorPlan> Activators { get; }

    public ImmutableArray<AkcssClassCachePlan> ClassCaches { get; }

    public ImmutableArray<AkcssUtilityApplicationPlan> Applications { get; }

    public ImmutableArray<AkcssUtilityApplicationCachePlan> ApplicationCaches { get; }

    public ImmutableArray<AkcssUtilityCandidatePlan> Candidates { get; }

    public ImmutableArray<AkcssUtilityValueSourcePlan> ValueSources { get; }

    public ImmutableArray<AkcssMarkupExtensionSlotPlan> MarkupExtensionSlots { get; }

    public INamedTypeSymbol? BindingPriorityType { get; }

    public bool IsEmpty => Activators.IsDefaultOrEmpty;
}

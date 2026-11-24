using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System.Collections.Immutable;

namespace Akbura.Language.Binder;

internal sealed record AkcssBindingContext(IAkcssDeclarationOwner Owner, AkburaSemanticModel DefinitionModel)
{
    public AkburaSyntax Definition => Owner.DeclarationSyntax!;

    public AkcssReceiver Subject { get; init; }

    public CSharpSymbolDefinition DefinitionReceiver { get; init; }

    public CSharpSymbolDefinition PropertyReceiver => DefinitionReceiver.IsDefault ? Subject.Type : DefinitionReceiver;

    public CSharpSymbolDefinition ExpressionReceiver => DefinitionReceiver.IsDefault && Subject.Origin == AkcssReceiverOrigin.LegacyDefault ? default : PropertyReceiver;

    public AkcssApplicationOrigin? Application { get; init; }

    public AkcssSelectorBinding? Selector { get; init; }

    public AkcssStyleParent StructuralParent { get; init; }

    public AkcssTemplateContext? SubjectTemplate { get; init; }

    public AkcssTemplateContext? ActiveTemplate { get; init; }

    public MarkupElementSyntax? OwningElement { get; init; }

    public ImmutableArray<AkcssMixinArgument> Arguments { get; init; } = [];

    public ImmutableArray<AkcssMixinSymbol> IncludeChain { get; init; } = [];

    public AkcssInclusion? Inclusion { get; init; }

    public bool IsMixinDefinition { get; init; }

    public bool IsConditional { get; init; }

    public bool HasActivator => IsConditional || Selector?.HasActivator == true;
}

internal sealed record AkcssApplicationOrigin(AkburaSyntax Syntax, IAkcssSymbol Declaration, CSharpSymbolDefinition TargetType);

internal sealed record AkcssInclusion(AkburaSyntax Site, AkcssInclusion? Parent)
{
    public AkburaSyntax OutermostSite => Parent?.OutermostSite ?? Site;
}

internal sealed record AkcssTemplateContext(
    AkcssThemeSymbol Theme, AkcssTemplateDirectiveSyntax Directive, MarkupElementSyntax Root, MarkupNameScope NameScope)
{
    public CSharpSymbolDefinition TemplatedParentType => Theme.TargetType;
}

internal enum AkcssReceiverOrigin
{
    Unknown,
    WrittenType,
    DeclaredTarget,
    LegacyDefault,
    TemplatePart,
    MarkupElement,
}

internal readonly record struct AkcssReceiver(
    CSharpSymbolDefinition Type, AkcssReceiverOrigin Origin, AkburaSyntax? Source = null);

internal enum AkcssStyleParentKind
{
    None,
    Style,
    Theme,
    Utility,
    MixinDefinition,
}

internal readonly record struct AkcssStyleParent(AkcssStyleParentKind Kind, AkburaSyntax? Syntax);

internal enum AkcssParentAnchorKind
{
    Style,
    ThemeRoot,
    Application,
    Deferred,
    Missing,
}

internal sealed record AkcssParentAnchor(
    AkcssParentSelectorSyntax Syntax,
    AkcssParentAnchorKind Kind,
    AkcssStyleParent StructuralParent,
    AkcssSelectorBinding? EnclosingSelector,
    AkcssApplicationOrigin? Application);

internal enum AkcssPredicateKind
{
    ExactType,
    Class,
    Name,
    PseudoClass,
}

internal sealed record AkcssSelectorPredicate(
    AkcssPredicateKind Kind, AkburaSyntax Syntax, string? Name, CSharpSymbolDefinition Type = default,
    MarkupNameDeclaration? SourcePart = null);

internal sealed record AkcssSelectorCompound(
    AkburaSyntax Syntax,
    ImmutableArray<AkcssSelectorPredicate> Predicates,
    AkcssReceiver Receiver,
    AkcssParentAnchor? Anchor = null);

internal enum AkcssSelectorRelationship
{
    Descendant,
    Child,
    Template,
}

internal sealed record AkcssSelectorStep(
    AkcssSelectorRelationship Relationship, AkcssSelectorStepSyntax Syntax, AkcssSelectorCompound Target,
    AkcssTemplateContext? SourceTemplate = null);

internal sealed record AkcssSelectorBinding(
    AkburaSyntax Syntax,
    AkcssSelectorCompound Initial,
    ImmutableArray<AkcssSelectorStep> Steps,
    AkcssStyleParent StructuralParent,
    bool HasActivator)
{
    public AkcssReceiver Subject => Steps.IsEmpty ? Initial.Receiver : Steps[Steps.Length - 1].Target.Receiver;
}

internal abstract record AkcssBoundData;

internal sealed record AkcssDeclarationData(IAkcssDeclarationOwner Declaration, AkcssTemplateContext? EffectiveTemplate = null)
    : AkcssBoundData;

internal sealed record AkcssSelectorData(AkcssSelectorBinding Selector) : AkcssBoundData;

internal sealed record AkcssSetterData(
    IPropertySymbol? Property,
    CSharpSymbolDefinition ValueType,
    CSharpOperationDefinition Value,
    BoundTree.AkburaConversion Conversion,
    AkcssPropertyValueKind ValueKind,
    bool RequiresBrushConversion,
    object? ConvertedValue,
    AkcssSetterPropertyKind PropertyKind,
    ImmutableArray<AkburaSemanticDiagnostic> Diagnostics,
    bool IsDeferred = false) : AkcssBoundData;

internal enum AkcssSetterPropertyKind
{
    Unresolved,
    Clr,
    Styled,
    Direct,
    Attached,
}

internal sealed record AkcssApplyData(ImmutableArray<string> Names, ImmutableArray<IAkcssSymbol> Symbols) : AkcssBoundData;

internal sealed record AkcssConditionData(CSharpBindingResult Condition) : AkcssBoundData;

internal sealed record AkcssMixinArgument(
    AkcssMixinParameterSymbol Parameter,
    CSharpExpressionSyntax Source,
    CSharpBindingResult Binding,
    bool IsDefault);

internal sealed record AkcssMixinParameterData(AkcssMixinParameterSymbol Parameter, AkcssMixinArgument? Default)
    : AkcssBoundData;

internal sealed record AkcssMixinArgumentData(AkcssMixinArgument Argument) : AkcssBoundData;

internal sealed record AkcssIncludeData(AkcssMixinSymbol? Mixin, ImmutableArray<AkcssMixinArgument> Arguments)
    : AkcssBoundData;

internal sealed record AkcssTemplateData(AkcssTemplateContext? Template) : AkcssBoundData;

internal sealed record AkcssTemplateBindingProperty(
    CSharpTypeSyntax Syntax, IPropertySymbol? Source, IPropertySymbol? Destination);

internal sealed record AkcssTemplateBindingData(
    AkcssTemplateContext? Template,
    MarkupElementSyntax? Element,
    ImmutableArray<AkcssTemplateBindingProperty> Properties,
    bool IsDeferred) : AkcssBoundData
{
    public bool IsOneWay => true;

    public bool UsesTemplatePriority => true;

    public bool UsesTargetConversion => true;
}

internal sealed record AkcssBaseThemeData(AkcssThemeSymbol? SourceTheme, CSharpBindingResult ExternalTheme)
    : AkcssBoundData;

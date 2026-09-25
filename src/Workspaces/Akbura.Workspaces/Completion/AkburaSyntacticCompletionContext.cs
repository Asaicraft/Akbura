using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Workspaces.Completion;

/// <summary>
/// Describes completion syntax at one position without requiring a semantic
/// project snapshot.
/// </summary>
public readonly struct AkburaSyntacticCompletionContext
{
    internal AkburaSyntacticCompletionContext(AkburaCompletionContextKind kind, TextSpan applicableSpan, string prefix, string? componentName, string? parentComponentName, ImmutableArray<string> existingAttributeNames, string? attributeName = null, AkburaMarkupAttributeMode attributeMode = default, TextSpan attributePrefixSpan = default, TextSpan attributeNameSpan = default, TextSpan fullAttributeNameSpan = default, bool hasAttributeEquals = false, bool hasAttributeValue = false, TextSpan attributeValueSpan = default, ImmutableArray<AkburaMarkupAttributeIdentity> existingAttributes = default) : this(kind, applicableSpan, prefix, componentName, parentComponentName, existingAttributeNames, attributeName, markupExtensionName: null, markupExtensionArgumentName: null, markupExtensionArgumentIndex: -1, completedPath: null, markupExtensionSpan: default, attributeMode, attributePrefixSpan, attributeNameSpan, fullAttributeNameSpan, hasAttributeEquals, hasAttributeValue, attributeValueSpan, existingAttributes)
    {
    }

    internal AkburaSyntacticCompletionContext(AkburaCompletionContextKind kind, TextSpan applicableSpan, string prefix, string? componentName, string? parentComponentName, ImmutableArray<string> existingAttributeNames, string? attributeName, string? markupExtensionName, string? markupExtensionArgumentName = null, int markupExtensionArgumentIndex = -1, string? completedPath = null, TextSpan markupExtensionSpan = default, AkburaMarkupAttributeMode attributeMode = default, TextSpan attributePrefixSpan = default, TextSpan attributeNameSpan = default, TextSpan fullAttributeNameSpan = default, bool hasAttributeEquals = false, bool hasAttributeValue = false, TextSpan attributeValueSpan = default, ImmutableArray<AkburaMarkupAttributeIdentity> existingAttributes = default)
    {
        Kind = kind;
        ApplicableSpan = applicableSpan;
        Prefix = prefix ?? string.Empty;
        ComponentName = componentName;
        ParentComponentName = parentComponentName;
        AttributeName = attributeName;
        MarkupExtensionName = markupExtensionName;
        MarkupExtensionArgumentName = markupExtensionArgumentName;
        MarkupExtensionArgumentIndex = markupExtensionArgumentIndex;
        CompletedPath = completedPath;
        MarkupExtensionSpan = markupExtensionSpan;
        AttributeMode = attributeMode;
        AttributePrefixSpan = attributePrefixSpan;
        AttributeNameSpan = attributeNameSpan;
        FullAttributeNameSpan = fullAttributeNameSpan;
        HasAttributeEquals = hasAttributeEquals;
        HasAttributeValue = hasAttributeValue;
        AttributeValueSpan = attributeValueSpan;
        ExistingAttributeNames = existingAttributeNames.IsDefault
            ? ImmutableArray<string>.Empty
            : existingAttributeNames;
        ExistingAttributes = existingAttributes.IsDefault
            ? ImmutableArray<AkburaMarkupAttributeIdentity>.Empty
            : existingAttributes;
    }

    public AkburaCompletionContextKind Kind { get; }

    public TextSpan ApplicableSpan { get; }

    public string Prefix { get; }

    public string? ComponentName { get; }

    public string? ParentComponentName { get; }

    public string? AttributeName { get; }

    public string? MarkupExtensionName { get; }

    public string? MarkupExtensionArgumentName { get; }

    public int MarkupExtensionArgumentIndex { get; }

    public string? CompletedPath { get; }

    public TextSpan MarkupExtensionSpan { get; }

    internal AkburaMarkupAttributeMode AttributeMode { get; }

    internal TextSpan AttributePrefixSpan { get; }

    internal TextSpan AttributeNameSpan { get; }

    internal TextSpan FullAttributeNameSpan { get; }

    internal bool HasAttributeEquals { get; }

    internal bool HasAttributeValue { get; }

    internal TextSpan AttributeValueSpan { get; }

    public ImmutableArray<string> ExistingAttributeNames { get; }

    internal ImmutableArray<AkburaMarkupAttributeIdentity> ExistingAttributes { get; }

    public bool IsDefault => Kind == AkburaCompletionContextKind.None;
}

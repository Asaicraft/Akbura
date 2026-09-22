using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Workspaces.Completion;

/// <summary>
/// Describes completion syntax at one position without requiring a semantic
/// project snapshot.
/// </summary>
public readonly struct AkburaSyntacticCompletionContext
{
    internal AkburaSyntacticCompletionContext(AkburaCompletionContextKind kind, TextSpan applicableSpan, string prefix, string? componentName, string? parentComponentName, ImmutableArray<string> existingAttributeNames, string? attributeName = null) : this(kind, applicableSpan, prefix, componentName, parentComponentName, existingAttributeNames, attributeName, markupExtensionName: null, markupExtensionArgumentName: null, markupExtensionArgumentIndex: -1, completedPath: null, markupExtensionSpan: default)
    {
    }

    internal AkburaSyntacticCompletionContext(AkburaCompletionContextKind kind, TextSpan applicableSpan, string prefix, string? componentName, string? parentComponentName, ImmutableArray<string> existingAttributeNames, string? attributeName, string? markupExtensionName, string? markupExtensionArgumentName = null, int markupExtensionArgumentIndex = -1, string? completedPath = null, TextSpan markupExtensionSpan = default)
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
        ExistingAttributeNames = existingAttributeNames.IsDefault
            ? ImmutableArray<string>.Empty
            : existingAttributeNames;
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

    public ImmutableArray<string> ExistingAttributeNames { get; }

    public bool IsDefault => Kind == AkburaCompletionContextKind.None;
}

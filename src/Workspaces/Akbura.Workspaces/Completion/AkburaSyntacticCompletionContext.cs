using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Workspaces.Completion;

/// <summary>
/// Describes completion syntax at one position without requiring a semantic
/// project snapshot.
/// </summary>
public readonly struct AkburaSyntacticCompletionContext
{
    internal AkburaSyntacticCompletionContext(
        AkburaCompletionContextKind kind,
        TextSpan applicableSpan,
        string prefix,
        string? componentName,
        string? parentComponentName,
        ImmutableArray<string> existingAttributeNames)
    {
        Kind = kind;
        ApplicableSpan = applicableSpan;
        Prefix = prefix ?? string.Empty;
        ComponentName = componentName;
        ParentComponentName = parentComponentName;
        ExistingAttributeNames = existingAttributeNames.IsDefault
            ? ImmutableArray<string>.Empty
            : existingAttributeNames;
    }

    public AkburaCompletionContextKind Kind { get; }

    public TextSpan ApplicableSpan { get; }

    public string Prefix { get; }

    public string? ComponentName { get; }

    public string? ParentComponentName { get; }

    public ImmutableArray<string> ExistingAttributeNames { get; }

    public bool IsDefault => Kind == AkburaCompletionContextKind.None;
}

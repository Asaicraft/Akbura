using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Workspaces;

public sealed class AkburaCodeAction
{
    internal AkburaCodeAction(
        AkburaCodeActionKind kind,
        string title,
        string equivalenceKey,
        string subjectText,
        string namespaceName,
        TextSpan diagnosticSpan,
        ImmutableArray<TextChange> changes)
    {
        Kind = kind;
        Title = title ?? throw new ArgumentNullException(nameof(title));
        EquivalenceKey = equivalenceKey ??
            throw new ArgumentNullException(nameof(equivalenceKey));
        SubjectText = subjectText ??
            throw new ArgumentNullException(nameof(subjectText));
        NamespaceName = namespaceName ??
            throw new ArgumentNullException(nameof(namespaceName));
        DiagnosticSpan = diagnosticSpan;
        Changes = changes.IsDefault
            ? ImmutableArray<TextChange>.Empty
            : changes;
    }

    public AkburaCodeActionKind Kind { get; }

    public string Title { get; }

    public string EquivalenceKey { get; }

    public string SubjectText { get; }

    public string NamespaceName { get; }

    public TextSpan DiagnosticSpan { get; }

    public ImmutableArray<TextChange> Changes { get; }
}

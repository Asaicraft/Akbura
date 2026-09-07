using Akbura.Diagnostics;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.Workspaces.Diagnostics;

/// <summary>
/// Describes a diagnostic and its location in an Akbura document.
/// </summary>
public readonly record struct AkburaDiagnosticSpan(
    TextSpan Span,
    string Code,
    string Message,
    AkburaDiagnosticSeverity Severity)
{
    /// <summary>
    /// The source diagnostic before host-specific presentation. The positional
    /// constructor remains available for clients that only need a document span.
    /// </summary>
    public AkburaDiagnosticRecord? CanonicalDiagnostic { get; init; }

    // Preserve the original document-span value contract. Consumers that merge
    // canonical issues must compare CanonicalDiagnostic with its full comparer.
    public bool Equals(AkburaDiagnosticSpan other) =>
        Span == other.Span &&
        string.Equals(Code, other.Code, StringComparison.Ordinal) &&
        string.Equals(Message, other.Message, StringComparison.Ordinal) &&
        Severity == other.Severity;

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = Span.GetHashCode();
            hash = (hash * 397) ^ (Code?.GetHashCode() ?? 0);
            hash = (hash * 397) ^ (Message?.GetHashCode() ?? 0);
            return (hash * 397) ^ Severity.GetHashCode();
        }
    }
}

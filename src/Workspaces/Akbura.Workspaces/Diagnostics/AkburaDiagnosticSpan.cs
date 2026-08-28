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
    AkburaDiagnosticSeverity Severity);

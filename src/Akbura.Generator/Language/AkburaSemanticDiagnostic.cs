using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace Akbura.Language;

internal sealed class AkburaSemanticDiagnostic : AkburaDiagnostic
{
    [SetsRequiredMembers]
    public AkburaSemanticDiagnostic(
        AkburaSyntax syntax,
        string code,
        ImmutableArray<object?> parameters,
        AkburaDiagnosticSeverity severity = AkburaDiagnosticSeverity.Error,
        TextSpan? span = null,
        ImmutableArray<AkburaSyntax> relatedSyntax = default)
        : base(parameters, code, severity)
    {
        Syntax = syntax ?? throw new ArgumentNullException(nameof(syntax));
        Span = span ?? syntax.Span;
        RelatedSyntax = relatedSyntax.IsDefault ? [] : relatedSyntax;
    }

    public AkburaSyntax Syntax { get; }

    public TextSpan Span { get; }

    public ImmutableArray<AkburaSyntax> RelatedSyntax { get; }
}

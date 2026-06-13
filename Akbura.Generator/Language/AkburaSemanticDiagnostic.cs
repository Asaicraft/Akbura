using Akbura.Language.Syntax;
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
        AkburaDiagnosticSeverity severity = AkburaDiagnosticSeverity.Error)
        : base(parameters, code, severity)
    {
        Syntax = syntax ?? throw new ArgumentNullException(nameof(syntax));
    }

    public AkburaSyntax Syntax { get; }
}

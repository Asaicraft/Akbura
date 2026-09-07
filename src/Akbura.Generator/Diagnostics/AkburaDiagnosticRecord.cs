using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Immutable;

namespace Akbura.Diagnostics;

public enum AkburaDiagnosticKind : byte
{
    Syntax,
    Semantic,
    Infrastructure,
}

[Flags]
public enum DiagnosticProvenance : byte
{
    AkburaCore = 1,
    BlackSilenceGenerator = 2,
    Workspaces = 4,
    LanguageServer = 8,
    VisualStudio = 16,
}

public readonly record struct AkburaDiagnosticLocation(
    string FilePath,
    TextSpan Span,
    LinePositionSpan LineSpan);

/// <summary>
/// A detached diagnostic snapshot. No syntax, semantic model, message argument,
/// or Roslyn diagnostic object is retained by this transport value.
/// </summary>
public readonly record struct AkburaDiagnosticRecord
{
    public AkburaDiagnosticRecord()
    {
    }

    public required string Id { get; init; }

    public required AkburaDiagnosticSeverity Severity { get; init; }

    public required string Message { get; init; }

    public required string FilePath { get; init; }

    public required TextSpan Span { get; init; }

    public required LinePositionSpan LineSpan { get; init; }

    public AkburaDiagnosticKind Kind { get; init; }

    public ImmutableArray<AkburaDiagnosticLocation> AdditionalLocations { get; init; } = [];

    public ImmutableDictionary<string, string?> Properties { get; init; } =
        ImmutableDictionary<string, string?>.Empty;

    public DiagnosticProvenance Provenance { get; init; } = DiagnosticProvenance.AkburaCore;

    public long DocumentVersion { get; init; }

    /// <summary>
    /// A stable transport fingerprint, not proof of equality. Consumers must
    /// compare canonical fields before merging diagnostics in the same bucket.
    /// </summary>
    public string LogicalId => AkburaDiagnosticCanonicalComparer.GetLogicalId(this);
}

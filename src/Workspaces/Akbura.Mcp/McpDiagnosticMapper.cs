using Akbura.Diagnostics;
using Akbura.Language.Syntax;
using Akbura.Workspaces.Diagnostics;
using Akbura.Workspaces.Documents;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Mcp;

/// <summary>
/// Maps canonical Workspace diagnostics and snapshot identities to the MCP response contracts.
/// </summary>
internal static class McpDiagnosticMapper
{
    internal static SnapshotResponse MapSnapshot(AkburaDocumentSnapshotIdentity snapshot) =>
        new(snapshot.Solution, snapshot.Project, snapshot.Document);

    internal static string MapLevel(AkburaDiagnosticAnalysisLevel level) =>
        level == AkburaDiagnosticAnalysisLevel.Syntax ? "syntax" : "semantic";

    internal static (CanonicalDiagnosticResponse[] Diagnostics, DiagnosticCountsResponse Counts) MapDiagnostics(
        ImmutableArray<AkburaDiagnosticRecord> diagnostics,
        string documentPath,
        string owningLabel,
        int limit,
        CancellationToken cancellationToken)
    {
        var mapped = new CanonicalDiagnosticResponse[Math.Min(diagnostics.Length, limit)];
        var errors = 0;
        var warnings = 0;
        var information = 0;
        var hidden = 0;
        for (var index = 0; index < diagnostics.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var diagnostic = diagnostics[index];
            switch (diagnostic.Severity)
            {
                case AkburaDiagnosticSeverity.Error: errors++; break;
                case AkburaDiagnosticSeverity.Warning: warnings++; break;
                case AkburaDiagnosticSeverity.Info: information++; break;
                case AkburaDiagnosticSeverity.Hidden: hidden++; break;
            }

            if (index < mapped.Length)
            {
                mapped[index] = MapDiagnostic(diagnostic, documentPath, owningLabel);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return (mapped, new(diagnostics.Length, errors, warnings, information, hidden));
    }

    private static CanonicalDiagnosticResponse MapDiagnostic(
        AkburaDiagnosticRecord diagnostic, string documentPath, string owningLabel)
    {
        var properties = diagnostic.Properties ?? ImmutableDictionary<string, string?>.Empty;
        var ownership = string.IsNullOrWhiteSpace(diagnostic.FilePath)
            ? "locationless"
            : properties.ContainsKey("akbura.module-identity") || properties.ContainsKey("akbura.module-reference")
                ? "referenced_module"
                : AkburaDiagnosticCanonicalComparer.PathsEqual(diagnostic.FilePath, documentPath)
                    ? owningLabel
                    : "foreign";

        var related = new DiagnosticLocationResponse[diagnostic.AdditionalLocations.IsDefault ? 0 : diagnostic.AdditionalLocations.Length];
        for (var index = 0; index < related.Length; index++)
        {
            var location = diagnostic.AdditionalLocations[index];
            related[index] = MapLocation(location.FilePath, location.Span, location.LineSpan) ??
                new DiagnosticLocationResponse(location.FilePath, null, null, null, "The related location has no source path.");
        }

        return new(diagnostic.Id, diagnostic.Severity.ToString().ToLowerInvariant(), diagnostic.Message,
            diagnostic.Kind.ToString().ToLowerInvariant(), diagnostic.LogicalId, ownership,
            MapLocation(diagnostic.FilePath, diagnostic.Span, diagnostic.LineSpan,
                includeFileUri: ownership is not ("referenced_module" or "example")), related,
            diagnostic.Provenance.ToString(), properties);
    }

    private static DiagnosticLocationResponse? MapLocation(string? path, TextSpan span, LinePositionSpan range, bool includeFileUri = true)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var uri = includeFileUri && Path.IsPathFullyQualified(path) && System.Uri.TryCreate(path, UriKind.Absolute, out var address) && address.IsFile
            ? address.AbsoluteUri
            : null;
        var extension = Path.GetExtension(path);
        if (extension.Equals(".akbura", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".akcss", StringComparison.OrdinalIgnoreCase))
        {
            // The canonical report computed these coordinates using the source owner's text.
            return new(path, uri, new(span.Start, span.Length),
                new(new(range.Start.Line, range.Start.Character), new(range.End.Line, range.End.Character)), null);
        }

        return new(path, uri, null, null, "The canonical issue has no mapped original Akbura source location.");
    }
}

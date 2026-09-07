using Akbura.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Workspaces.Diagnostics;

internal sealed class AkburaDiagnosticService : IAkburaDiagnosticService
{
    public ImmutableArray<AkburaDiagnosticSpan> GetSyntacticDiagnostics(
        AkburaSyntacticDocument document,
        TextSpan requestedSpan,
        CancellationToken cancellationToken = default)
    {
        if (document == null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        var diagnostics = AkburaDiagnosticEngine.Collect(
            document.SyntaxTree,
            includeSemantic: false,
            cancellationToken: cancellationToken);

        return SelectDiagnostics(diagnostics, document.FilePath, document.Text.Length, requestedSpan, cancellationToken);
    }

    public ImmutableArray<AkburaDiagnosticSpan> GetDiagnostics(
        AkburaDocumentContext context,
        TextSpan requestedSpan,
        CancellationToken cancellationToken = default)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var document = context.Document;
        var semanticModel = context.Project.Compilation.GetSemanticModel(document.SyntaxTree);
        var diagnostics = AkburaDiagnosticEngine.Collect(
            document.SyntaxTree,
            semanticModel,
            cancellationToken: cancellationToken);

        return SelectDiagnostics(diagnostics, document.FilePath, document.Text.Length, requestedSpan, cancellationToken);
    }

    internal static ImmutableArray<AkburaDiagnosticSpan> SelectDiagnostics(
        ImmutableArray<AkburaDiagnosticRecord> diagnostics,
        string filePath,
        int textLength,
        TextSpan requestedSpan,
        CancellationToken cancellationToken)
    {
        var requested = TextSpan.FromBounds(
            Math.Min(requestedSpan.Start, textLength),
            Math.Min(requestedSpan.End, textLength));
        var result = ImmutableArray.CreateBuilder<AkburaDiagnosticSpan>();

        foreach (var diagnostic in diagnostics)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Workspace documents represent local SourceText, not a referenced
            // module's embedded source, even when the stored paths coincide.
            if (!AkburaDiagnosticCanonicalComparer.PathsEqual(diagnostic.FilePath, filePath) ||
                diagnostic.Properties is { } properties &&
                (properties.ContainsKey("akbura.module-identity") || properties.ContainsKey("akbura.module-reference")))
            {
                continue;
            }

            var span = diagnostic.Span;
            if (span.Length == 0
                ? span.Start < requested.Start || span.Start > requested.End
                : !span.OverlapsWith(requested))
            {
                continue;
            }

            result.Add(new AkburaDiagnosticSpan(
                span,
                diagnostic.Id,
                diagnostic.Message,
                diagnostic.Severity)
            {
                CanonicalDiagnostic = diagnostic with
                {
                    Provenance = diagnostic.Provenance | DiagnosticProvenance.Workspaces,
                },
            });
        }

        return result.ToImmutable();
    }
}

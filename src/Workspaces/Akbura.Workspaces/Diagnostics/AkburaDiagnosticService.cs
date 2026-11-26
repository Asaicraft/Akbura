using Akbura.Diagnostics;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Workspaces.Diagnostics;

/// <summary>
/// Collects canonical diagnostics and derives the requested analysis stages or editor markers.
/// </summary>
internal sealed class AkburaDiagnosticService : IAkburaDiagnosticService, IAkburaDocumentAnalysisService
{
    public AkburaDocumentAnalysisResult AnalyzeDocument(
        AkburaDocumentContext context,
        AkburaDiagnosticAnalysisLevel level = AkburaDiagnosticAnalysisLevel.Semantic,
        bool isProjectComplete = true,
        CancellationToken cancellationToken = default)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));
        if (level is not (AkburaDiagnosticAnalysisLevel.Syntax or AkburaDiagnosticAnalysisLevel.Semantic))
        {
            throw new ArgumentOutOfRangeException(nameof(level));
        }

        cancellationToken.ThrowIfCancellationRequested();

        using var reasons = ImmutableArrayBuilder<string>.Rent();
        var includeSemantic = level == AkburaDiagnosticAnalysisLevel.Semantic;
        if (includeSemantic)
        {
            var compilation = context.Project.CSharpCompilation;
            if (compilation.GetSpecialType(SpecialType.System_Object).TypeKind == TypeKind.Error ||
                compilation.GetTypeByMetadataName("Avalonia.Controls.Control") == null ||
                compilation.GetTypeByMetadataName("Akbura.AkburaControl") == null)
            {
                includeSemantic = false;
                reasons.Add("Semantic analysis requires resolvable .NET, Avalonia, and Akbura references.");
            }
            if (!isProjectComplete)
            {
                reasons.Add("Project loading reported an incomplete design-time context.");
            }
        }

        var diagnostics = CollectCanonical(context, includeSemantic, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return new(context.Document.FilePath, AkburaDocumentSnapshotIdentity.Capture(context),
            level, includeSemantic ? AkburaDiagnosticAnalysisLevel.Semantic : AkburaDiagnosticAnalysisLevel.Syntax,
            reasons.Count == 0, diagnostics, reasons.ToImmutable());
    }

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
        => GetDiagnosticReport(context, requestedSpan, cancellationToken).SourceDiagnostics;

    public AkburaDiagnosticReport GetDiagnosticReport(
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
        var diagnostics = CollectCanonical(context, includeSemantic: true, cancellationToken);
        return new AkburaDiagnosticReport(
            diagnostics,
            SelectDiagnostics(diagnostics, document.FilePath, document.Text.Length, requestedSpan, cancellationToken));
    }

    private static ImmutableArray<AkburaDiagnosticRecord> CollectCanonical(
        AkburaDocumentContext context, bool includeSemantic, CancellationToken cancellationToken)
    {
        var tree = context.Document.SyntaxTree;
        var model = includeSemantic ? context.Project.Compilation.GetSemanticModel(tree) : null;
        var diagnostics = AkburaDiagnosticEngine.Collect(tree, model, includeSemantic, cancellationToken);
        if (diagnostics.IsEmpty)
        {
            return diagnostics;
        }

        using var canonical = ImmutableArrayBuilder<AkburaDiagnosticRecord>.Rent(diagnostics.Length);
        foreach (var diagnostic in diagnostics)
        {
            cancellationToken.ThrowIfCancellationRequested();
            canonical.Add(diagnostic with { Provenance = diagnostic.Provenance | DiagnosticProvenance.Workspaces });
        }
        return canonical.ToImmutable();
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

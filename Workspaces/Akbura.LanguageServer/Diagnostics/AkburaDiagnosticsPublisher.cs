using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Akbura.LanguageServer.Diagnostics;

internal sealed class AkburaDiagnosticsPublisher
{
    private readonly AkburaServerState _state;
    private readonly AkburaLanguageServerServices _services;
    private readonly ConcurrentDictionary<Uri, AkburaDiagnosticResult>
        _results = new(AkburaUriComparer.Instance);

    public AkburaDiagnosticsPublisher(
        AkburaServerState state,
        AkburaLanguageServerServices services)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _services = services ??
            throw new ArgumentNullException(nameof(services));
    }

    public async Task PublishSyntacticAsync(
        AkburaOpenDocument document,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        var diagnostics = _services.Workspace.LanguageServices.Diagnostics
            .GetSyntacticDiagnostics(
                document.SyntacticDocument,
                new TextSpan(0, document.Text.Length),
                cancellationToken);
        var result = CreateResult(
            document,
            documentVersion: default,
            projectVersion: default,
            diagnostics);

        await StoreAndPublishAsync(
                result,
                requireSemanticIdentity: false,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task PublishSemanticAsync(
        Uri uri,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uri);
        var snapshot = _state.Current;
        if (!snapshot.OpenDocuments.TryGetValue(
                uri,
                out var openDocument) ||
            !snapshot.Solution.TryGetDocumentContext(
                uri,
                out var documentContext))
        {
            return;
        }

        var diagnostics = _services.Workspace.LanguageServices.Diagnostics
            .GetDiagnostics(
                documentContext,
                new TextSpan(0, documentContext.Document.Text.Length),
                cancellationToken);
        var result = CreateResult(
            openDocument,
            documentContext.Document.Version,
            documentContext.Project.Version,
            diagnostics);

        await StoreAndPublishAsync(
                result,
                requireSemanticIdentity: true,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task PublishAllSemanticAsync(
        CancellationToken cancellationToken)
    {
        var snapshot = _state.Current;
        var tasks = snapshot.OpenDocuments.Keys
            .Select(uri => PublishSemanticAsync(uri, cancellationToken))
            .ToArray();
        await Task.WhenAll(tasks).ConfigureAwait(false);

        if (snapshot.ClientCapabilities.SupportsPullDiagnostics &&
            snapshot.ClientCapabilities.SupportsDiagnosticRefresh)
        {
            await RequestRefreshAsync(cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async Task ClearAsync(
        Uri uri,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uri);
        _results.TryRemove(uri, out _);
        var snapshot = _state.Current;
        if (snapshot.ClientCapabilities.SupportsPullDiagnostics)
        {
            if (snapshot.ClientCapabilities.SupportsDiagnosticRefresh)
            {
                await RequestRefreshAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            return;
        }

        await _services.Client.NotifyAsync(
                LspMethods.PublishDiagnostics,
                new PublishDiagnosticsParams
                {
                    Uri = uri.AbsoluteUri,
                    Version = null,
                    Diagnostics = [],
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<object> GetDocumentReportAsync(
        Uri uri,
        string? previousResultId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uri);
        await EnsureCurrentResultAsync(uri, cancellationToken)
            .ConfigureAwait(false);

        if (_results.TryGetValue(uri, out var result))
        {
            if (string.Equals(
                    result.ResultId,
                    previousResultId,
                    StringComparison.Ordinal))
            {
                return new UnchangedDocumentDiagnosticReport
                {
                    ResultId = result.ResultId,
                };
            }

            return new FullDocumentDiagnosticReport
            {
                ResultId = result.ResultId,
                Items = AkburaProtocolMapper.ToDiagnostics(
                    result.Text,
                    result.Diagnostics,
                    _services.PositionConverter),
            };
        }

        return new FullDocumentDiagnosticReport
        {
            ResultId = CreateEmptyResultId(uri),
            Items = [],
        };
    }

    public async Task<WorkspaceDiagnosticReport> GetWorkspaceReportAsync(
        IReadOnlyDictionary<string, string> previousResultIds,
        CancellationToken cancellationToken)
    {
        var snapshot = _state.Current;
        foreach (var uri in snapshot.OpenDocuments.Keys)
        {
            await EnsureCurrentResultAsync(uri, cancellationToken)
                .ConfigureAwait(false);
        }

        var items = new List<WorkspaceDocumentDiagnosticReport>(
            snapshot.OpenDocuments.Count);
        foreach (var pair in snapshot.OpenDocuments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_results.TryGetValue(pair.Key, out var result))
            {
                items.Add(new WorkspaceDocumentDiagnosticReport
                {
                    Uri = pair.Key.AbsoluteUri,
                    Version = pair.Value.Version,
                    Kind = "full",
                    ResultId = CreateEmptyResultId(pair.Key),
                    Items = [],
                });
                continue;
            }

            previousResultIds.TryGetValue(
                pair.Key.AbsoluteUri,
                out var previousResultId);
            if (string.Equals(
                    previousResultId,
                    result.ResultId,
                    StringComparison.Ordinal))
            {
                items.Add(new WorkspaceDocumentDiagnosticReport
                {
                    Uri = pair.Key.AbsoluteUri,
                    Version = result.LspVersion,
                    Kind = "unchanged",
                    ResultId = result.ResultId,
                    Items = [],
                });
                continue;
            }

            items.Add(new WorkspaceDocumentDiagnosticReport
            {
                Uri = pair.Key.AbsoluteUri,
                Version = result.LspVersion,
                Kind = "full",
                ResultId = result.ResultId,
                Items = AkburaProtocolMapper.ToDiagnostics(
                    result.Text,
                    result.Diagnostics,
                    _services.PositionConverter),
            });
        }

        return new WorkspaceDiagnosticReport
        {
            Items = items.ToArray(),
        };
    }

    private async Task EnsureCurrentResultAsync(
        Uri uri,
        CancellationToken cancellationToken)
    {
        var snapshot = _state.Current;
        if (!snapshot.OpenDocuments.TryGetValue(uri, out var openDocument))
        {
            return;
        }

        if (snapshot.Solution.TryGetDocumentContext(
                uri,
                out var semanticDocument))
        {
            if (!_results.TryGetValue(uri, out var result) ||
                result.LspVersion != openDocument.Version ||
                result.DocumentVersion !=
                    semanticDocument.Document.Version ||
                result.ProjectVersion != semanticDocument.Project.Version)
            {
                await PublishSemanticAsync(uri, cancellationToken)
                    .ConfigureAwait(false);
            }

            return;
        }

        if (!_results.TryGetValue(uri, out var syntacticResult) ||
            syntacticResult.LspVersion != openDocument.Version ||
            !syntacticResult.Text.ContentEquals(openDocument.Text))
        {
            await PublishSyntacticAsync(
                    openDocument,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task StoreAndPublishAsync(
        AkburaDiagnosticResult result,
        bool requireSemanticIdentity,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var current = _state.Current;
        if (!current.OpenDocuments.TryGetValue(
                result.Uri,
                out var openDocument) ||
            openDocument.Version != result.LspVersion ||
            !openDocument.Text.ContentEquals(result.Text))
        {
            return;
        }

        if (requireSemanticIdentity)
        {
            if (!current.Solution.TryGetDocumentContext(
                    result.Uri,
                    out var context) ||
                context.Document.Version != result.DocumentVersion ||
                context.Project.Version != result.ProjectVersion)
            {
                return;
            }
        }

        _results[result.Uri] = result;
        if (current.ClientCapabilities.SupportsPullDiagnostics)
        {
            return;
        }

        await _services.Client.NotifyAsync(
                LspMethods.PublishDiagnostics,
                new PublishDiagnosticsParams
                {
                    Uri = result.Uri.AbsoluteUri,
                    Version = result.LspVersion,
                    Diagnostics = AkburaProtocolMapper.ToDiagnostics(
                        result.Text,
                        result.Diagnostics,
                        _services.PositionConverter),
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task RequestRefreshAsync(
        CancellationToken cancellationToken)
    {
        await _services.Client.RequestAsync<object, object>(
                LspMethods.DiagnosticRefresh,
                new { },
                cancellationToken)
            .ConfigureAwait(false);
    }

    private static AkburaDiagnosticResult CreateResult(
        AkburaOpenDocument document,
        Microsoft.CodeAnalysis.VersionStamp documentVersion,
        Microsoft.CodeAnalysis.VersionStamp projectVersion,
        ImmutableArray<AkburaDiagnosticSpan> diagnostics)
    {
        return new AkburaDiagnosticResult(
            document.Uri,
            document.Version,
            documentVersion,
            projectVersion,
            CreateResultId(
                document.Uri,
                document.Version,
                documentVersion,
                projectVersion,
                diagnostics),
            document.Text,
            diagnostics);
    }

    private static string CreateResultId(
        Uri uri,
        int? lspVersion,
        Microsoft.CodeAnalysis.VersionStamp documentVersion,
        Microsoft.CodeAnalysis.VersionStamp projectVersion,
        ImmutableArray<AkburaDiagnosticSpan> diagnostics)
    {
        var builder = new StringBuilder();
        builder.Append(uri.AbsoluteUri)
            .Append('|')
            .Append(lspVersion)
            .Append('|')
            .Append(documentVersion.GetHashCode())
            .Append('|')
            .Append(projectVersion.GetHashCode());
        foreach (var diagnostic in diagnostics)
        {
            builder.Append('|')
                .Append(diagnostic.Code)
                .Append(':')
                .Append(diagnostic.Span.Start)
                .Append(':')
                .Append(diagnostic.Span.Length)
                .Append(':')
                .Append((int)diagnostic.Severity)
                .Append(':')
                .Append(diagnostic.Message);
        }

        return Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(builder.ToString())));
    }

    private static string CreateEmptyResultId(Uri uri)
    {
        return Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    uri.AbsoluteUri + "|empty")));
    }
}

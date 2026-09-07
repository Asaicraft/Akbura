using Akbura.Diagnostics;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Akbura.LanguageServer.Diagnostics;

internal sealed class AkburaDiagnosticsPublisher
{
    private readonly AkburaServerState _state;
    private readonly AkburaLanguageServerServices _services;
    private readonly IAkburaDiagnosticService _diagnostics;
    private readonly ConcurrentDictionary<Uri, AkburaDiagnosticResult>
        _results = new(AkburaUriComparer.Instance);

    public AkburaDiagnosticsPublisher(
        AkburaServerState state,
        AkburaLanguageServerServices services,
        IAkburaDiagnosticService? diagnosticService = null)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _services = services ??
            throw new ArgumentNullException(nameof(services));
        _diagnostics = diagnosticService ?? services.Workspace.LanguageServices.Diagnostics;
    }

    public async Task PublishSyntacticAsync(
        AkburaOpenDocument document,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        var diagnostics = _diagnostics
            .GetSyntacticDiagnostics(
                document.SyntacticDocument,
                new TextSpan(0, document.Text.Length),
                cancellationToken);
        var result = CreateResult(
            document,
            documentVersion: default,
            projectVersion: default,
            diagnostics,
            GetPublisher(_state.Current, document.Uri));

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

        var diagnostics = _diagnostics
            .GetDiagnostics(
                documentContext,
                new TextSpan(0, documentContext.Document.Text.Length),
                cancellationToken);
        var result = CreateResult(
            openDocument,
            documentContext.Document.Version,
            documentContext.Project.Version,
            diagnostics,
            documentContext.Project.Context.DiagnosticPublisher);

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
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = _state.Current;
        await EnsureCurrentResultAsync(uri, cancellationToken)
            .ConfigureAwait(false);
        ThrowIfDocumentChanged(snapshot, uri, cancellationToken);

        object report;
        if (snapshot.OpenDocuments.ContainsKey(uri) && _results.TryGetValue(uri, out var result))
        {
            ThrowIfResultDoesNotMatch(snapshot, result);
            if (string.Equals(
                    result.ResultId,
                    previousResultId,
                    StringComparison.Ordinal))
            {
                report = new UnchangedDocumentDiagnosticReport
                {
                    ResultId = result.ResultId,
                };
            }
            else
            {
                report = new FullDocumentDiagnosticReport
                {
                    ResultId = result.ResultId,
                    Items = AkburaProtocolMapper.ToDiagnostics(
                        result.Text,
                        result.Diagnostics,
                        _services.PositionConverter,
                        result.LspVersion),
                };
            }
        }
        else
        {
            if (snapshot.OpenDocuments.ContainsKey(uri))
            {
                throw ContentModified();
            }

            report = new FullDocumentDiagnosticReport
            {
                ResultId = CreateEmptyResultId(uri),
                Items = [],
            };
        }

        ThrowIfDocumentChanged(snapshot, uri, cancellationToken);
        return report;
    }

    public async Task<WorkspaceDiagnosticReport> GetWorkspaceReportAsync(
        IReadOnlyDictionary<string, string> previousResultIds,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshot = _state.Current;
        foreach (var uri in snapshot.OpenDocuments.Keys)
        {
            await EnsureCurrentResultAsync(uri, cancellationToken)
                .ConfigureAwait(false);
        }

        ThrowIfWorkspaceChanged(snapshot, cancellationToken);

        var items = new List<WorkspaceDocumentDiagnosticReport>(
            snapshot.OpenDocuments.Count);
        foreach (var pair in snapshot.OpenDocuments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_results.TryGetValue(pair.Key, out var result))
            {
                throw ContentModified();
            }

            ThrowIfResultDoesNotMatch(snapshot, result);

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
                    _services.PositionConverter,
                    result.LspVersion),
            });
        }

        ThrowIfWorkspaceChanged(snapshot, cancellationToken);
        return new WorkspaceDiagnosticReport
        {
            Items = items.ToArray(),
        };
    }

    private void ThrowIfDocumentChanged(
        AkburaServerSnapshot snapshot,
        Uri uri,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!HasSameDocumentIdentity(snapshot, _state.Current, uri))
        {
            throw ContentModified();
        }
    }

    private void ThrowIfWorkspaceChanged(AkburaServerSnapshot snapshot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var current = _state.Current;
        if (snapshot.OpenDocuments.Count != current.OpenDocuments.Count)
        {
            throw ContentModified();
        }

        foreach (var uri in snapshot.OpenDocuments.Keys)
        {
            if (!HasSameDocumentIdentity(snapshot, current, uri))
            {
                throw ContentModified();
            }
        }
    }

    private static bool HasSameDocumentIdentity(
        AkburaServerSnapshot expected,
        AkburaServerSnapshot current,
        Uri uri)
    {
        var wasOpen = expected.OpenDocuments.TryGetValue(uri, out var previousDocument);
        var isOpen = current.OpenDocuments.TryGetValue(uri, out var currentDocument);
        if (wasOpen != isOpen)
        {
            return false;
        }

        if (!wasOpen)
        {
            return true;
        }

        if (previousDocument!.Version != currentDocument!.Version ||
            previousDocument.ProjectId != currentDocument.ProjectId ||
            previousDocument.DocumentId != currentDocument.DocumentId ||
            !previousDocument.Text.ContentEquals(currentDocument.Text) ||
            GetPublisher(expected, uri) != GetPublisher(current, uri))
        {
            return false;
        }

        var hadSemantics = expected.Solution.TryGetDocumentContext(uri, out var previousContext);
        var hasSemantics = current.Solution.TryGetDocumentContext(uri, out var currentContext);
        return hadSemantics == hasSemantics && (!hadSemantics ||
            previousContext.Document.Version == currentContext.Document.Version &&
            previousContext.Project.Version == currentContext.Project.Version);
    }

    private static void ThrowIfResultDoesNotMatch(AkburaServerSnapshot snapshot, AkburaDiagnosticResult result)
    {
        if (!snapshot.OpenDocuments.TryGetValue(result.Uri, out var document) ||
            document.Version != result.LspVersion ||
            !document.Text.ContentEquals(result.Text) ||
            GetPublisher(snapshot, result.Uri) != result.Publisher)
        {
            throw ContentModified();
        }

        if (snapshot.Solution.TryGetDocumentContext(result.Uri, out var context)
            ? context.Document.Version != result.DocumentVersion || context.Project.Version != result.ProjectVersion
            : result.DocumentVersion != default || result.ProjectVersion != default)
        {
            throw ContentModified();
        }
    }

    private static AkburaProtocolException ContentModified() =>
        new(LspErrorCodes.ContentModified, "The document changed while diagnostics were being computed.");

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
            !openDocument.Text.ContentEquals(result.Text) ||
            GetPublisher(current, result.Uri) != result.Publisher)
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
                        _services.PositionConverter,
                        result.LspVersion),
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
        ImmutableArray<AkburaDiagnosticSpan> diagnostics,
        AkburaDiagnosticPublisher publisher)
    {
        if (!AkburaDiagnosticPublicationPolicy.ShouldPublishWorkspace(publisher))
        {
            diagnostics = [];
        }

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
            diagnostics,
            publisher);
    }

    private static AkburaDiagnosticPublisher GetPublisher(AkburaServerSnapshot snapshot, Uri uri)
    {
        return snapshot.Solution.TryGetDocumentContext(uri, out var context)
            ? context.Project.Context.DiagnosticPublisher
            : AkburaDiagnosticPublisher.Auto;
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
                .Append(diagnostic.Message)
                .Append(':')
                .Append(diagnostic.CanonicalDiagnostic?.LogicalId);
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

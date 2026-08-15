using Akbura.VisualStudio.CSharp;
using Akbura.VisualStudio.Editor;
using Akbura.Workspaces;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using RoslynQuickInfoService =
    Microsoft.CodeAnalysis.QuickInfo.QuickInfoService;
using VisualStudioQuickInfoItem =
    Microsoft.VisualStudio.Language.Intellisense.QuickInfoItem;

namespace Akbura.VisualStudio.QuickInfo;

internal sealed class AkburaQuickInfoSource : IAsyncQuickInfoSource
{
    private readonly ITextBuffer _buffer;

    private readonly AkburaTextBufferContext _bufferContext;

    private readonly AkburaParserService _parserService;

    private readonly AkburaProjectedCSharpDocumentService
        _projectedDocumentService;

    private readonly IAkburaQuickInfoService _quickInfoService;

    private int _disposeState;

    public AkburaQuickInfoSource(
        ITextBuffer buffer,
        AkburaTextBufferContext bufferContext,
        AkburaParserService parserService,
        AkburaProjectedCSharpDocumentService projectedDocumentService,
        IAkburaQuickInfoService quickInfoService)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        _bufferContext = bufferContext ??
            throw new ArgumentNullException(nameof(bufferContext));
        _parserService = parserService ??
            throw new ArgumentNullException(nameof(parserService));
        _projectedDocumentService = projectedDocumentService ??
            throw new ArgumentNullException(
                nameof(projectedDocumentService));
        _quickInfoService = quickInfoService ??
            throw new ArgumentNullException(nameof(quickInfoService));
    }

    public async Task<VisualStudioQuickInfoItem?> GetQuickInfoItemAsync(
        IAsyncQuickInfoSession session,
        CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _disposeState) != 0)
        {
            return null;
        }

        var snapshot = _buffer.CurrentSnapshot;
        var triggerPoint = session.GetTriggerPoint(snapshot);
        if (triggerPoint == null)
        {
            return null;
        }

        try
        {
            var projected = await TryGetProjectedQuickInfoAsync(
                    snapshot,
                    triggerPoint.Value.Position,
                    cancellationToken)
                .ConfigureAwait(false);
            if (projected != null)
            {
                AkburaWorkspaceDiagnostics.Write(
                    AkburaWorkspaceDiagnostics.Category.QuickInfo,
                    "Projected Quick Info hit.");
                return projected;
            }

            return await TryGetNativeQuickInfoAsync(
                    snapshot,
                    triggerPoint.Value,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics.Category.QuickInfo,
                "Quick Info lookup failed.",
                exception);
            return null;
        }
    }

    private async Task<VisualStudioQuickInfoItem?>
        TryGetProjectedQuickInfoAsync(
            ITextSnapshot snapshot,
            int position,
            CancellationToken cancellationToken)
    {
        try
        {
            var syntacticDocument = await _parserService
                .GetSyntacticDocumentAsync(snapshot)
                .ConfigureAwait(false);
            if (!syntacticDocument.TryGetEmbeddedCSharpContext(
                    position,
                    out var embeddedContext,
                    cancellationToken) ||
                !_bufferContext.TryGetLatestDocumentContext(
                    out var semanticContext,
                    out var semanticSnapshot) ||
                semanticSnapshot.Version.VersionNumber >
                    snapshot.Version.VersionNumber)
            {
                return null;
            }

            var projected = await _projectedDocumentService
                .GetProjectedDocumentAsync(
                    snapshot,
                    syntacticDocument,
                    semanticContext,
                    embeddedContext,
                    cancellationToken)
                .ConfigureAwait(false);
            if (projected == null)
            {
                return null;
            }

            var service = RoslynQuickInfoService.GetService(
                projected.RoslynDocument);
            if (service == null)
            {
                return null;
            }

            var quickInfo = await service.GetQuickInfoAsync(
                    projected.RoslynDocument,
                    projected.Projection.ProjectedPosition,
                    cancellationToken)
                .ConfigureAwait(false);
            if (quickInfo == null ||
                !projected.Projection.TryMapToHost(
                    quickInfo.Span,
                    out var hostSpan) ||
                hostSpan.Start < 0 ||
                hostSpan.End > snapshot.Length)
            {
                return null;
            }

            var text = string.Join(
                Environment.NewLine,
                quickInfo.Sections
                    .Select(section => string.Concat(
                        section.TaggedParts.Select(static part =>
                            part.Text)))
                    .Where(static section =>
                        !string.IsNullOrWhiteSpace(section)));
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            var trackingSpan = snapshot.CreateTrackingSpan(
                new Span(hostSpan.Start, hostSpan.Length),
                SpanTrackingMode.EdgeExclusive);
            return new VisualStudioQuickInfoItem(trackingSpan, text);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics.Category.QuickInfo,
                "Projected C# Quick Info failed: " +
                exception);
            return null;
        }
    }

    private async Task<VisualStudioQuickInfoItem?>
        TryGetNativeQuickInfoAsync(
            ITextSnapshot currentSnapshot,
            SnapshotPoint triggerPoint,
            CancellationToken cancellationToken)
    {
        if (!_bufferContext.TryGetPublishedState(
                currentSnapshot,
                out var state))
        {
            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics.Category.QuickInfo,
                "Native Quick Info skipped: no semantic state.");
            return null;
        }

        var semanticPoint = AkburaSnapshotTranslationFacts.TranslatePoint(
            triggerPoint,
            state.Snapshot);
        if (semanticPoint.Position >= state.Snapshot.Length)
        {
            return null;
        }

        var quickInfo = await Task.Run(
                () => _quickInfoService.GetQuickInfo(
                    state.Context,
                    semanticPoint.Position,
                    cancellationToken),
                cancellationToken)
            .ConfigureAwait(false);
        if (quickInfo == null ||
            quickInfo.SourceSpan.Length == 0 ||
            quickInfo.SourceSpan.Start < 0 ||
            quickInfo.SourceSpan.End > state.Snapshot.Length)
        {
            return null;
        }

        var semanticSpan = new SnapshotSpan(
            state.Snapshot,
            new Span(
                quickInfo.SourceSpan.Start,
                quickInfo.SourceSpan.Length));
        var currentSpan = AkburaSnapshotTranslationFacts.TranslateSourceSpan(
            semanticSpan,
            currentSnapshot);
        if (currentSpan.Length == 0)
        {
            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics.Category.QuickInfo,
                "Native Quick Info translated to an empty span.");
            return null;
        }

        var content = quickInfo.Details.Length == 0
            ? quickInfo.Signature
            : quickInfo.Signature + Environment.NewLine +
              string.Join(Environment.NewLine, quickInfo.Details);
        var trackingSpan = currentSnapshot.CreateTrackingSpan(
            currentSpan.Span,
            SpanTrackingMode.EdgeExclusive);
        AkburaWorkspaceDiagnostics.Write(
            AkburaWorkspaceDiagnostics.Category.QuickInfo,
            $"Native Quick Info hit: kind={quickInfo.Kind}, " +
            $"span={quickInfo.SourceSpan}.");
        return new VisualStudioQuickInfoItem(trackingSpan, content);
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _disposeState, 1);
    }
}

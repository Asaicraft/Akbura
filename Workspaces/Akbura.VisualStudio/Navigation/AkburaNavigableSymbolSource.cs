using Akbura.VisualStudio.Editor;
using Akbura.Workspaces;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using System.Diagnostics;

namespace Akbura.VisualStudio.Navigation;

internal sealed class AkburaNavigableSymbolSource : INavigableSymbolSource
{
    private readonly AkburaTextBufferContext
        _bufferContext;

    private readonly IAkburaDefinitionService
        _definitionService;

    private readonly IServiceProvider
        _serviceProvider;

    private int _disposeState;

    public AkburaNavigableSymbolSource(
        AkburaTextBufferContext bufferContext,
        IAkburaDefinitionService definitionService,
        IServiceProvider serviceProvider)
    {
        _bufferContext =
            bufferContext ??
            throw new ArgumentNullException(
                nameof(bufferContext));

        _definitionService =
            definitionService ??
            throw new ArgumentNullException(
                nameof(definitionService));

        _serviceProvider =
            serviceProvider ??
            throw new ArgumentNullException(
                nameof(serviceProvider));
    }

    public async Task<INavigableSymbol?>
        GetNavigableSymbolAsync(
            SnapshotSpan triggerSpan,
            CancellationToken cancellationToken)
    {
        if (Volatile.Read(
                ref _disposeState) != 0)
        {
            return null;
        }

        try
        {
            if (!_bufferContext
                    .TryGetPublishedState(
                        triggerSpan.Snapshot,
                        out var state))
            {
                return null;
            }

            var parsedTriggerSpan =
                TranslateTriggerSpan(
                    triggerSpan,
                    state.Snapshot);

            if (parsedTriggerSpan.Start.Position >=
                state.Snapshot.Length)
            {
                return null;
            }

            var parsedPosition =
                parsedTriggerSpan.Start.Position;

            var definition =
                await Task.Run(
                        () =>
                            TryGetDefinition(
                                state.Context,
                                parsedPosition,
                                cancellationToken))
                .ConfigureAwait(false);

            if (definition == null ||
                cancellationToken.IsCancellationRequested ||
                Volatile.Read(
                    ref _disposeState) != 0)
            {
                return null;
            }

            if (!IsValidSourceSpan(
                    definition,
                    state.Snapshot))
            {
                return null;
            }

            var parsedSymbolSpan =
                new SnapshotSpan(
                    state.Snapshot,
                    new Span(
                        definition.SourceSpan.Start,
                        definition.SourceSpan.Length));

            var currentSymbolSpan =
                TranslateSymbolSpan(
                    parsedSymbolSpan,
                    triggerSpan.Snapshot);

            if (currentSymbolSpan.Length == 0)
            {
                return null;
            }

            return new AkburaNavigableSymbol(
                currentSymbolSpan,
                definition,
                _serviceProvider);
        }
        catch (OperationCanceledException)
            when (cancellationToken
                .IsCancellationRequested)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            /*
             * Snapshot translation can fail when the editor no longer
             * retains a compatible version chain.
             */
            Debug.WriteLine(
                $"[Akbura] Definition span translation failed: " +
                $"{exception}");

            return null;
        }
        catch (Exception exception)
        {
            /*
             * A navigation lookup failure must not break the editor.
             */
            Debug.WriteLine(
                $"[Akbura] Definition lookup failed: " +
                $"{exception}");

            return null;
        }
    }

    private AkburaDefinition? TryGetDefinition(
        AkburaDocumentContext context,
        int position,
        CancellationToken cancellationToken)
    {
        try
        {
            return _definitionService
                .GetDefinition(
                    context,
                    position,
                    cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private static SnapshotSpan TranslateTriggerSpan(
        SnapshotSpan triggerSpan,
        ITextSnapshot parsedSnapshot)
    {
        if (IsSameSnapshotVersion(
                triggerSpan.Snapshot,
                parsedSnapshot))
        {
            return new SnapshotSpan(
                parsedSnapshot,
                triggerSpan.Span);
        }

        /*
         * Include both edges while translating the user's hover position
         * back to the parsed snapshot.
         */
        return triggerSpan.TranslateTo(
            parsedSnapshot,
            SpanTrackingMode.EdgeInclusive);
    }

    private static SnapshotSpan TranslateSymbolSpan(
        SnapshotSpan parsedSpan,
        ITextSnapshot currentSnapshot)
    {
        if (IsSameSnapshotVersion(
                parsedSpan.Snapshot,
                currentSnapshot))
        {
            return new SnapshotSpan(
                currentSnapshot,
                parsedSpan.Span);
        }

        /*
         * Newly inserted adjacent text must not become part of the
         * clickable symbol.
         */
        return parsedSpan.TranslateTo(
            currentSnapshot,
            SpanTrackingMode.EdgeExclusive);
    }

    private static bool IsValidSourceSpan(
        AkburaDefinition definition,
        ITextSnapshot snapshot)
    {
        return definition.SourceSpan.Start >= 0 &&
            definition.SourceSpan.Length > 0 &&
            definition.SourceSpan.End <=
                snapshot.Length;
    }

    private static bool IsSameSnapshotVersion(
        ITextSnapshot left,
        ITextSnapshot right)
    {
        return ReferenceEquals(
                   left.TextBuffer,
                   right.TextBuffer) &&
               left.Version.VersionNumber ==
                   right.Version.VersionNumber;
    }

    public void Dispose()
    {
        Interlocked.Exchange(
            ref _disposeState,
            1);
    }
}
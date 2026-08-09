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

    private readonly AkburaVisualStudioWorkspace
        _workspaceHost;

    private int _disposeState;

    public AkburaNavigableSymbolSource(
        AkburaTextBufferContext bufferContext,
        IAkburaDefinitionService definitionService,
        AkburaVisualStudioWorkspace workspaceHost,
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

        _workspaceHost =
            workspaceHost ??
            throw new ArgumentNullException(
                nameof(workspaceHost));

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
        AkburaNavigationTrace.Write(
            $"Query received: " +
            $"trigger={triggerSpan.Start.Position}, " +
            $"length={triggerSpan.Length}, " +
            $"snapshot={triggerSpan.Snapshot.Version.VersionNumber}, " +
            $"character={GetCharacter(triggerSpan)}.");

        if (Volatile.Read(
                ref _disposeState) != 0)
        {
            AkburaNavigationTrace.Write(
                "Query ignored: source is disposed.");
            return null;
        }

        try
        {
            if (!_bufferContext
                    .TryGetPublishedState(
                        triggerSpan.Snapshot,
                        out var state))
            {
                AkburaNavigationTrace.Write(
                    "Query failed: no semantic buffer state " +
                    "has been published yet.");
                return null;
            }

            var parsedTriggerSpan =
                TranslateTriggerSpan(
                    triggerSpan,
                    state.Snapshot);

            if (parsedTriggerSpan.Start.Position >=
                state.Snapshot.Length)
            {
                AkburaNavigationTrace.Write(
                    $"Query failed: translated position " +
                    $"{parsedTriggerSpan.Start.Position} is outside " +
                    $"semantic snapshot length " +
                    $"{state.Snapshot.Length}.");
                return null;
            }

            var parsedPosition =
                parsedTriggerSpan.Start.Position;

            AkburaNavigationTrace.Write(
                $"Semantic state found: " +
                $"file='{state.Context.Document.FilePath}', " +
                $"assembly='{state.Context.Project.CSharpCompilation.AssemblyName}', " +
                $"rootNamespace='{state.Context.Project.Context.RootNamespace}', " +
                $"projectReferences={state.Context.Project.Context.ProjectReferences.Length}, " +
                $"semanticSnapshot={state.Snapshot.Version.VersionNumber}, " +
                $"position={parsedPosition}, " +
                $"context={GetTextContext(state.Snapshot, parsedPosition)}.");

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
                AkburaNavigationTrace.Write(
                    definition == null
                        ? "Query failed: definition service returned null."
                        : "Query cancelled after definition lookup.");
                return null;
            }

            if (!IsValidSourceSpan(
                    definition,
                    state.Snapshot))
            {
                AkburaNavigationTrace.Write(
                    $"Query failed: definition source span " +
                    $"{definition.SourceSpan} is invalid for " +
                    $"snapshot length {state.Snapshot.Length}.");
                return null;
            }

            AkburaNavigationTrace.Write(
                $"Definition found: " +
                $"sourceSpan={definition.SourceSpan}, " +
                $"target='{definition.TargetFilePath}', " +
                $"targetLines={definition.TargetLineSpan}, " +
                $"targetAssembly='{definition.TargetAssemblyName ?? "<none>"}', " +
                $"targetSource='{definition.TargetSourcePath ?? "<none>"}'.");

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
                AkburaNavigationTrace.Write(
                    "Query failed: translated symbol span is empty.");
                return null;
            }

            AkburaNavigationTrace.Write(
                $"Navigable symbol returned: " +
                $"span={currentSymbolSpan.Start.Position}.." +
                $"{currentSymbolSpan.End.Position}.");

            return new AkburaNavigableSymbol(
                currentSymbolSpan,
                definition,
                _workspaceHost,
                _serviceProvider);
        }
        catch (OperationCanceledException)
            when (cancellationToken
                .IsCancellationRequested)
        {
            AkburaNavigationTrace.Write(
                "Query cancelled by Visual Studio.");
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

            AkburaNavigationTrace.Write(
                "Query failed while translating editor spans.",
                exception);

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

            AkburaNavigationTrace.Write(
                "Query failed with an exception.",
                exception);

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

    private static string GetCharacter(
        SnapshotSpan triggerSpan)
    {
        if (triggerSpan.Length == 0)
        {
            return "<empty>";
        }

        var character =
            triggerSpan.Snapshot[
                triggerSpan.Start.Position];
        return character switch
        {
            '\r' => "\\r",
            '\n' => "\\n",
            '\t' => "\\t",
            _ => $"'{character}'",
        };
    }

    private static string GetTextContext(
        ITextSnapshot snapshot,
        int position)
    {
        var start = Math.Max(
            0,
            position - 20);
        var end = Math.Min(
            snapshot.Length,
            position + 21);
        return "'" +
            snapshot.GetText(
                    start,
                    end - start)
                .Replace("\r", "\\r")
                .Replace("\n", "\\n") +
            "'";
    }

    public void Dispose()
    {
        AkburaNavigationTrace.Write(
            "Navigable symbol source disposed.");

        Interlocked.Exchange(
            ref _disposeState,
            1);
    }
}

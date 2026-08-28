using Akbura.VisualStudio.Editor;
using Akbura.Workspaces;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;

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
        AkburaWorkspaceDiagnostics.Write(
            AkburaWorkspaceDiagnostics.Category.Navigation,
            $"Query received: " +
            $"trigger={triggerSpan.Start.Position}, " +
            $"length={triggerSpan.Length}, " +
            $"snapshot={triggerSpan.Snapshot.Version.VersionNumber}, " +
            $"character={GetCharacter(triggerSpan)}.");

        if (Volatile.Read(
                ref _disposeState) != 0)
        {
            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics.Category.Navigation,
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
                AkburaWorkspaceDiagnostics.Write(
                    AkburaWorkspaceDiagnostics.Category.Navigation,
                    "Query failed: no semantic buffer state " +
                    "has been published yet.");
                return null;
            }

            var parsedTriggerPoint =
                AkburaSnapshotTranslationFacts.TranslatePoint(
                    triggerSpan.Start,
                    state.Snapshot);

            if (parsedTriggerPoint.Position >=
                state.Snapshot.Length)
            {
                AkburaWorkspaceDiagnostics.Write(
                    AkburaWorkspaceDiagnostics.Category.Navigation,
                    $"Query failed: translated position " +
                    $"{parsedTriggerPoint.Position} is outside " +
                    $"semantic snapshot length " +
                    $"{state.Snapshot.Length}.");
                return null;
            }

            var parsedPosition = parsedTriggerPoint.Position;

            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics.Category.Navigation,
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
                AkburaWorkspaceDiagnostics.Write(
                    AkburaWorkspaceDiagnostics.Category.Navigation,
                    definition == null
                        ? "Query failed: definition service returned null."
                        : "Query cancelled after definition lookup.");
                return null;
            }

            if (!IsValidSourceSpan(
                    definition,
                    state.Snapshot))
            {
                AkburaWorkspaceDiagnostics.Write(
                    AkburaWorkspaceDiagnostics.Category.Navigation,
                    $"Query failed: definition source span " +
                    $"{definition.SourceSpan} is invalid for " +
                    $"snapshot length {state.Snapshot.Length}.");
                return null;
            }

            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics.Category.Navigation,
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
                AkburaSnapshotTranslationFacts.TranslateSourceSpan(
                    parsedSymbolSpan,
                    triggerSpan.Snapshot);

            if (currentSymbolSpan.Length == 0)
            {
                AkburaWorkspaceDiagnostics.Write(
                    AkburaWorkspaceDiagnostics.Category.Navigation,
                    "Query failed: translated symbol span is empty.");
                return null;
            }

            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics.Category.Navigation,
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
            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics.Category.Navigation,
                "Query cancelled by Visual Studio.");
            throw;
        }
        catch (ArgumentException exception)
        {
            /*
             * Snapshot translation can fail when the editor no longer
             * retains a compatible version chain.
             */
            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics.Category.Navigation,
                "Definition span translation failed.",
                exception);

            return null;
        }
        catch (Exception exception)
        {
            /*
             * A navigation lookup failure must not break the editor.
             */
            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics.Category.Navigation,
                "Definition lookup failed.",
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

    private static bool IsValidSourceSpan(
        AkburaDefinition definition,
        ITextSnapshot snapshot)
    {
        return definition.SourceSpan.Start >= 0 &&
            definition.SourceSpan.Length > 0 &&
            definition.SourceSpan.End <=
                snapshot.Length;
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
        AkburaWorkspaceDiagnostics.Write(
            AkburaWorkspaceDiagnostics.Category.Navigation,
            "Navigable symbol source disposed.");

        Interlocked.Exchange(
            ref _disposeState,
            1);
    }
}

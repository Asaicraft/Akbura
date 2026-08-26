using Akbura.LanguageServer.Handlers.Completion;
using Akbura.LanguageServer.Handlers.Diagnostics;
using Akbura.LanguageServer.Handlers.Documents;
using Akbura.LanguageServer.Handlers.LanguageFeatures;
using Akbura.LanguageServer.Handlers.Lifecycle;
using Akbura.LanguageServer.Handlers.SemanticTokens;
using Akbura.LanguageServer.Protocol.Serialization;
using StreamJsonRpc;

namespace Akbura.LanguageServer.Hosting;

internal static class AkburaLanguageServerHost
{
    public static async Task<int> RunAsync(
        Stream input,
        Stream output,
        AkburaServerOptions options,
        TextWriter error,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(error);

        using var logger = new TextWriterAkburaServerLogger(
            error,
            options.LogLevel,
            options.LogFile);
        using var workspace = new AkburaWorkspace();
        using var lifetime = new AkburaServerLifetime();
        using var parentProcess = new AkburaParentProcessMonitor(
            lifetime,
            logger);
        var client = new StreamJsonRpcAkburaClient();
        var services = new AkburaLanguageServerServices(
            workspace,
            client,
            logger,
            new Utf16PositionConverter(),
            lifetime,
            parentProcess,
            options);
        var state = new AkburaServerState(
            AkburaServerSnapshot.Create(workspace));

        var handlers = new IAkburaLspHandler[]
        {
            new InitializeHandler(),
            new InitializedHandler(),
            new ShutdownHandler(),
            new ExitHandler(),
            new DidOpenHandler(),
            new DidChangeHandler(),
            new DidCloseHandler(),
            new DidSaveHandler(),
            new DidChangeWatchedFilesHandler(),
            new DidChangeWorkspaceFoldersHandler(),
            new DocumentDiagnosticHandler(),
            new WorkspaceDiagnosticHandler(),
            new SemanticTokensFullHandler(),
            new SemanticTokensRangeHandler(),
            new SemanticTokensDeltaHandler(),
            new CompletionHandler(),
            new CompletionResolveHandler(),
            new HoverHandler(),
            new DefinitionHandler(),
            new CodeActionHandler(),
            new CodeActionResolveHandler(),
            new DocumentSymbolHandler(),
            new WorkspaceSymbolHandler(),
            new FoldingRangeHandler(),
            new DocumentHighlightHandler(),
            new ReferencesHandler(),
            new PrepareRenameHandler(),
            new RenameHandler(),
            new SignatureHelpHandler(),
            new DocumentFormattingHandler(),
            new DocumentRangeFormattingHandler(),
            new DocumentOnTypeFormattingHandler(),
            new AkburaApplyLoadedProjectsHandler(),
        };
        var registry = new AkburaLspHandlerRegistry(handlers);
        var contextFactory = new AkburaRequestContextFactory(
            state,
            services);
        await using var queue = new AkburaRequestExecutionQueue(
            registry,
            contextFactory,
            state,
            logger);
        await using var projects = new AkburaProjectLoadCoordinator(
            services,
            queue);
        var diagnostics = new AkburaDiagnosticsPublisher(
            state,
            services);
        services.CompleteComposition(diagnostics, projects);

        var formatter = new SystemTextJsonFormatter
        {
            JsonSerializerOptions =
                AkburaProtocolJson.CreateOptions(),
        };
        using var messageHandler =
            new HeaderDelimitedMessageHandler(
                output,
                input,
                formatter);
        using var rpc = new JsonRpc(messageHandler);
        rpc.AddLocalRpcTarget(
            new AkburaLspRpcTarget(queue),
            new JsonRpcTargetOptions
            {
                UseSingleObjectParameterDeserialization = false,
            });
        client.Attach(rpc);
        rpc.StartListening();

        logger.Log(
            AkburaServerLogLevel.Information,
            "Akbura language server started over stdio.");

        var cancellationTask = WaitForCancellationAsync(
            cancellationToken);
        var completed = await Task.WhenAny(
                rpc.Completion,
                lifetime.ExitTask,
                cancellationTask)
            .ConfigureAwait(false);
        if (completed == cancellationTask)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        if (completed == lifetime.ExitTask)
        {
            return await lifetime.ExitTask.ConfigureAwait(false);
        }

        try
        {
            await rpc.Completion.ConfigureAwait(false);
        }
        catch (ConnectionLostException exception)
        {
            logger.Log(
                AkburaServerLogLevel.Information,
                "The LSP transport was closed.",
                exception);
        }

        return lifetime.IsShutdownRequested ? 0 : 1;
    }

    private static Task WaitForCancellationAsync(
        CancellationToken cancellationToken)
    {
        if (!cancellationToken.CanBeCanceled)
        {
            return Task.Delay(Timeout.InfiniteTimeSpan);
        }

        return Task.Delay(
            Timeout.InfiniteTimeSpan,
            cancellationToken);
    }
}

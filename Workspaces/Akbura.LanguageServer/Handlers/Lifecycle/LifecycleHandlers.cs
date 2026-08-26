using System.Text.Json;
using StreamJsonRpc;

namespace Akbura.LanguageServer.Handlers.Lifecycle;

internal sealed class InitializeHandler :
    AkburaLspHandler<InitializeParams, InitializeResult>
{
    public override string Method => LspMethods.Initialize;

    public override bool MutatesServerState => true;

    public override Task<AkburaLspHandlerResult<InitializeResult>> HandleAsync(
        InitializeParams parameters,
        AkburaRequestContext context,
        CancellationToken cancellationToken)
    {
        if (context.ServerSnapshot.IsInitializeReceived)
        {
            throw new AkburaProtocolException(
                LspErrorCodes.InvalidRequest,
                "The language server has already been initialized.");
        }

        var capabilities = ReadCapabilities(parameters.Capabilities);
        var folders = CreateWorkspaceFolders(parameters, context.Services);
        var clientProcessId = parameters.ProcessId ??
            context.Services.Options.ClientProcessId;
        var next = context.ServerSnapshot
            .Next(context.Solution) with
            {
                WorkspaceFolders = folders,
                ClientCapabilities = capabilities,
                PositionEncoding = AkburaPositionEncoding.Utf16,
                ClientProcessId = clientProcessId,
                IsInitializeReceived = true,
            };
        var result = new InitializeResult
        {
            Capabilities = CreateServerCapabilities(capabilities),
            ServerInfo = new ServerInfo
            {
                Name = "Akbura Language Server",
                Version = typeof(InitializeHandler)
                    .Assembly.GetName().Version?.ToString(),
            },
        };

        return Task.FromResult(
            new AkburaLspHandlerResult<InitializeResult>(
                result,
                next,
                token =>
                {
                    context.Services.ParentProcessMonitor.Start(
                        clientProcessId);
                    return Task.CompletedTask;
                }));
    }

    private static AkburaClientCapabilities ReadCapabilities(
        InitializeClientCapabilities capabilities)
    {
        var completion = capabilities.TextDocument?
            .Completion?.CompletionItem;
        return new AkburaClientCapabilities(
            SupportsSnippets:
                completion?.SnippetSupport == true,
            SupportsCompletionResolve:
                completion?.ResolveSupport != null,
            SupportsCodeActionResolve:
                capabilities.TextDocument?.CodeAction?
                    .ResolveSupport != null,
            SupportsDocumentChanges:
                capabilities.Workspace?.WorkspaceEdit?
                    .DocumentChanges == true,
            SupportsPullDiagnostics:
                capabilities.TextDocument?.Diagnostic != null,
            SupportsDiagnosticRefresh:
                capabilities.Workspace?.Diagnostics?
                    .RefreshSupport == true,
            SupportsDynamicFileWatching:
                capabilities.Workspace?.DidChangeWatchedFiles?
                    .DynamicRegistration == true);
    }

    private static ImmutableDictionary<Uri, AkburaWorkspaceFolderState>
        CreateWorkspaceFolders(
            InitializeParams parameters,
            AkburaLanguageServerServices services)
    {
        var folders = ImmutableDictionary.CreateBuilder<
            Uri,
            AkburaWorkspaceFolderState>(
                AkburaUriComparer.Instance);

        if (parameters.WorkspaceFolders is { Length: > 0 })
        {
            foreach (var folder in parameters.WorkspaceFolders)
            {
                var uri = AkburaProtocolMapper.ParseUri(folder.Uri);
                folders[uri] = AkburaWorkspaceFolderState.Create(
                    uri,
                    string.IsNullOrWhiteSpace(folder.Name)
                        ? GetFolderName(uri)
                        : folder.Name);
            }
        }
        else if (!string.IsNullOrWhiteSpace(parameters.RootUri))
        {
            var uri = AkburaProtocolMapper.ParseUri(parameters.RootUri);
            folders[uri] = AkburaWorkspaceFolderState.Create(
                uri,
                GetFolderName(uri));
        }
        else if (!string.IsNullOrWhiteSpace(parameters.RootPath))
        {
            var uri = new Uri(Path.GetFullPath(parameters.RootPath));
            folders[uri] = AkburaWorkspaceFolderState.Create(
                uri,
                GetFolderName(uri));
        }
        else
        {
            var explicitPath = services.Options.SolutionPath ??
                services.Options.ProjectPath;
            var directory = explicitPath == null
                ? Environment.CurrentDirectory
                : Path.GetDirectoryName(explicitPath) ??
                    Environment.CurrentDirectory;
            var uri = new Uri(Path.GetFullPath(directory));
            folders[uri] = AkburaWorkspaceFolderState.Create(
                uri,
                GetFolderName(uri));
        }

        return folders.ToImmutable();
    }

    private static string GetFolderName(Uri uri)
    {
        if (!uri.IsFile)
        {
            return uri.Host;
        }

        return Path.GetFileName(
            uri.LocalPath.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar));
    }

    private static ServerCapabilities CreateServerCapabilities(
        AkburaClientCapabilities capabilities)
    {
        return new ServerCapabilities
        {
            PositionEncoding = "utf-16",
            TextDocumentSync = new TextDocumentSyncOptions
            {
                OpenClose = true,
                Change = 2,
                Save = new SaveOptions
                {
                    IncludeText = true,
                },
            },
            CompletionProvider = new CompletionOptions
            {
                ResolveProvider = true,
            },
            CodeActionProvider = new CodeActionOptions
            {
                ResolveProvider = true,
            },
            SemanticTokensProvider = new SemanticTokensOptions
            {
                Legend = new SemanticTokensLegend
                {
                    TokenTypes = AkburaSemanticTokenEncoder.TokenTypes,
                    TokenModifiers = [],
                },
                Full = new SemanticTokensFullOptions
                {
                    Delta = true,
                },
                Range = true,
            },
            DiagnosticProvider =
                capabilities.SupportsPullDiagnostics
                    ? new DiagnosticOptions()
                    : null,
        };
    }
}

internal sealed class InitializedHandler :
    AkburaLspHandler<InitializedParams, object?>
{
    public override string Method => LspMethods.Initialized;

    public override bool MutatesServerState => true;

    public override Task<AkburaLspHandlerResult<object?>> HandleAsync(
        InitializedParams parameters,
        AkburaRequestContext context,
        CancellationToken cancellationToken)
    {
        if (!context.ServerSnapshot.IsInitializeReceived)
        {
            throw new AkburaProtocolException(
                LspErrorCodes.ServerNotInitialized,
                "The initialize request has not completed.");
        }

        var next = context.ServerSnapshot
            .Next(context.Solution) with
            {
                IsInitialized = true,
            };
        return Task.FromResult(
            new AkburaLspHandlerResult<object?>(
                response: null,
                snapshot: next,
                afterCommit: async token =>
                {
                    if (next.ClientCapabilities
                        .SupportsDynamicFileWatching)
                    {
                        await RegisterFileWatchersAsync(
                                context.Services,
                                token)
                            .ConfigureAwait(false);
                    }

                    await context.Services.Projects
                        .StartAsync(next, token)
                        .ConfigureAwait(false);
                }));
    }

    private static async Task RegisterFileWatchersAsync(
        AkburaLanguageServerServices services,
        CancellationToken cancellationToken)
    {
        var options = JsonSerializer.SerializeToElement(
            new
            {
                watchers = new[]
                {
                    new { globPattern = "**/*.akbura" },
                    new { globPattern = "**/*.akcss" },
                    new { globPattern = "**/*.csproj" },
                    new { globPattern = "**/*.sln" },
                    new { globPattern = "**/*.slnx" },
                    new { globPattern = "**/Directory.Build.props" },
                    new { globPattern = "**/Directory.Build.targets" },
                    new { globPattern = "**/Directory.Packages.props" },
                    new { globPattern = "**/global.json" },
                },
            });
        await services.Client.RequestAsync<RegistrationParams, object>(
                LspMethods.RegisterCapability,
                new RegistrationParams
                {
                    Registrations =
                    [
                        new Registration
                        {
                            Id = "akbura-file-watchers",
                            Method =
                                LspMethods.DidChangeWatchedFiles,
                            RegisterOptions = options,
                        },
                    ],
                },
                cancellationToken)
            .ConfigureAwait(false);
    }
}

internal sealed class ShutdownHandler :
    AkburaLspHandler<object?, object?>
{
    public override string Method => LspMethods.Shutdown;

    public override bool MutatesServerState => true;

    public override Task<AkburaLspHandlerResult<object?>> HandleAsync(
        object? parameters,
        AkburaRequestContext context,
        CancellationToken cancellationToken)
    {
        context.Services.Lifetime.RequestShutdown();
        var next = context.ServerSnapshot
            .Next(context.Solution) with
            {
                IsShuttingDown = true,
            };
        return Task.FromResult(
            new AkburaLspHandlerResult<object?>(
                response: null,
                snapshot: next));
    }
}

internal sealed class ExitHandler :
    AkburaLspHandler<object?, object?>
{
    public override string Method => LspMethods.Exit;

    public override bool MutatesServerState => true;

    public override Task<AkburaLspHandlerResult<object?>> HandleAsync(
        object? parameters,
        AkburaRequestContext context,
        CancellationToken cancellationToken)
    {
        context.Services.Lifetime.RequestExit();
        var next = context.ServerSnapshot.Next(context.Solution);
        return Task.FromResult(
            new AkburaLspHandlerResult<object?>(
                response: null,
                snapshot: next));
    }
}

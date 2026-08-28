namespace Akbura.LanguageServer.Hosting;

internal sealed class AkburaLanguageServerServices
{
    public AkburaLanguageServerServices(
        AkburaWorkspace workspace,
        IAkburaLspClient client,
        IAkburaServerLogger logger,
        IAkburaPositionConverter positionConverter,
        AkburaServerLifetime lifetime,
        AkburaParentProcessMonitor parentProcessMonitor,
        AkburaServerOptions options)
    {
        Workspace = workspace ??
            throw new ArgumentNullException(nameof(workspace));
        Client = client ?? throw new ArgumentNullException(nameof(client));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        PositionConverter = positionConverter ??
            throw new ArgumentNullException(nameof(positionConverter));
        Lifetime = lifetime ??
            throw new ArgumentNullException(nameof(lifetime));
        ParentProcessMonitor = parentProcessMonitor ??
            throw new ArgumentNullException(nameof(parentProcessMonitor));
        Options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public AkburaWorkspace Workspace { get; }

    public IAkburaLspClient Client { get; }

    public IAkburaServerLogger Logger { get; }

    public IAkburaPositionConverter PositionConverter { get; }

    public AkburaServerLifetime Lifetime { get; }

    public AkburaParentProcessMonitor ParentProcessMonitor { get; }

    public AkburaServerOptions Options { get; }

    public AkburaSemanticTokenCache SemanticTokens { get; } = new();

    public AkburaDiagnosticsPublisher Diagnostics { get; private set; } = null!;

    public AkburaProjectLoadCoordinator Projects { get; private set; } = null!;

    public void CompleteComposition(
        AkburaDiagnosticsPublisher diagnostics,
        AkburaProjectLoadCoordinator projects)
    {
        if (Diagnostics != null || Projects != null)
        {
            throw new InvalidOperationException(
                "Language server services are already composed.");
        }

        Diagnostics = diagnostics ??
            throw new ArgumentNullException(nameof(diagnostics));
        Projects = projects ??
            throw new ArgumentNullException(nameof(projects));
    }
}
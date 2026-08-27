namespace Akbura.LanguageServer.Dispatch;

internal sealed class AkburaRequestContextFactory
{
    private readonly AkburaServerState _state;
    private readonly AkburaLanguageServerServices _services;

    public AkburaRequestContextFactory(
        AkburaServerState state,
        AkburaLanguageServerServices services)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _services = services ??
            throw new ArgumentNullException(nameof(services));
    }

    public AkburaRequestContext CreateContext(
        AkburaLspHandlerDescriptor descriptor,
        object? parameters)
    {
        var snapshot = _state.Current;
        var uri = descriptor.Handler.GetDocumentUri(parameters);
        AkburaOpenDocument? openDocument = null;
        AkburaDocumentContext? semanticDocument = null;

        if (uri != null)
        {
            snapshot.OpenDocuments.TryGetValue(uri, out openDocument);
            if (openDocument?.DocumentId is { } documentId)
            {
                snapshot.Solution.TryGetDocumentContext(
                    documentId,
                    out semanticDocument);
            }
            else
            {
                snapshot.Solution.TryGetDocumentContext(
                    uri,
                    out semanticDocument);
            }
        }

        if (descriptor.RequiresDocument && openDocument == null)
        {
            throw new AkburaProtocolException(
                LspErrorCodes.InvalidParams,
                uri == null
                    ? $"Method '{descriptor.Method}' requires a document URI."
                    : $"Document '{uri}' is not open.");
        }

        return new AkburaRequestContext
        {
            Method = descriptor.Method,
            Solution = snapshot.Solution,
            ServerSnapshot = snapshot,
            OpenDocument = openDocument,
            SyntacticDocument = openDocument?.SyntacticDocument,
            SemanticDocument = semanticDocument,
            ClientCapabilities = snapshot.ClientCapabilities,
            PositionEncoding = snapshot.PositionEncoding,
            Services = _services,
        };
    }
}
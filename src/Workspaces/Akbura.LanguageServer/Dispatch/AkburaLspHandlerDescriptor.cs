namespace Akbura.LanguageServer.Dispatch;

internal sealed class AkburaLspHandlerDescriptor
{
    public AkburaLspHandlerDescriptor(IAkburaLspHandler handler)
    {
        Handler = handler ??
            throw new ArgumentNullException(nameof(handler));
        Method = handler.Method;
        MutatesServerState = handler.MutatesServerState;
        RequiresDocument = handler.RequiresDocument;
        RequiresSemanticContext = handler.RequiresSemanticContext;
    }

    public string Method { get; }

    public bool MutatesServerState { get; }

    public bool RequiresDocument { get; }

    public bool RequiresSemanticContext { get; }

    public IAkburaLspHandler Handler { get; }
}
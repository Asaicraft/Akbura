using System.Collections.Frozen;

namespace Akbura.LanguageServer.Dispatch;

internal sealed class AkburaLspHandlerRegistry
{
    private readonly FrozenDictionary<string, AkburaLspHandlerDescriptor> _handlers;

    public AkburaLspHandlerRegistry(IEnumerable<IAkburaLspHandler> handlers)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        _handlers = handlers
            .Select(static handler =>
                new AkburaLspHandlerDescriptor(handler))
            .ToFrozenDictionary(
                static descriptor => descriptor.Method,
                StringComparer.Ordinal);
    }

    public AkburaLspHandlerDescriptor GetRequired(string method)
    {
        if (!_handlers.TryGetValue(method, out var descriptor))
        {
            throw new AkburaProtocolException(
                LspErrorCodes.MethodNotFound,
                $"LSP method '{method}' is not registered.");
        }

        return descriptor;
    }
}

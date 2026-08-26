namespace Akbura.LanguageServer.Protocol;

public static class LspErrorCodes
{
    public const int ParseError = -32700;
    public const int InvalidRequest = -32600;
    public const int MethodNotFound = -32601;
    public const int InvalidParams = -32602;
    public const int InternalError = -32603;
    public const int ServerNotInitialized = -32002;
    public const int RequestCancelled = -32800;
    public const int ContentModified = -32801;
}

public sealed class AkburaProtocolException : Exception
{
    public AkburaProtocolException(int code, string message)
        : base(message)
    {
        Code = code;
    }

    public AkburaProtocolException(
        int code,
        string message,
        Exception innerException)
        : base(message, innerException)
    {
        Code = code;
    }

    public int Code { get; }
}
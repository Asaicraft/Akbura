using Microsoft.CodeAnalysis.Text;

namespace Akbura.Workspaces.Rename;

public sealed class AkburaRenameInfo
{
    internal AkburaRenameInfo(
        bool canRename,
        TextSpan span,
        string? placeholder,
        string? errorMessage,
        AkburaSymbolKey? symbol)
    {
        CanRename = canRename;
        Span = span;
        Placeholder = placeholder;
        ErrorMessage = errorMessage;
        Symbol = symbol;
    }

    public bool CanRename { get; }

    public TextSpan Span { get; }

    public string? Placeholder { get; }

    public string? ErrorMessage { get; }

    internal AkburaSymbolKey? Symbol { get; }
}

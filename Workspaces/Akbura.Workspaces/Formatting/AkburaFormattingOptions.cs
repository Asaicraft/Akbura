namespace Akbura.Workspaces.Formatting;

public sealed record AkburaFormattingOptions(
    int TabSize = 4,
    bool InsertSpaces = true,
    bool TrimTrailingWhitespace = true,
    bool InsertFinalNewline = false,
    bool TrimFinalNewlines = false)
{
    public int EffectiveTabSize => Math.Max(1, TabSize);
}
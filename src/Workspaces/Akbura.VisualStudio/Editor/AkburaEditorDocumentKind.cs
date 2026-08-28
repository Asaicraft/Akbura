namespace Akbura.VisualStudio.Editor;

internal enum AkburaEditorDocumentKind
{
    Unknown = 0,
    Component,
    Akcss,
}

internal static class AkburaEditorDocumentKindFacts
{
    public static AkburaEditorDocumentKind FromFilePath(
        string? filePath)
    {
        return Path.GetExtension(filePath) switch
        {
            var extension when string.Equals(
                extension,
                ".akbura",
                StringComparison.OrdinalIgnoreCase) =>
                AkburaEditorDocumentKind.Component,

            var extension when string.Equals(
                extension,
                ".akcss",
                StringComparison.OrdinalIgnoreCase) =>
                AkburaEditorDocumentKind.Akcss,

            _ => AkburaEditorDocumentKind.Unknown,
        };
    }

    public static AkburaEditorDocumentKind GetOrDefault(
        Microsoft.VisualStudio.Text.ITextBuffer buffer)
    {
        return buffer.Properties.TryGetProperty(
            typeof(AkburaEditorDocumentKind),
            out AkburaEditorDocumentKind kind)
                ? kind
                : AkburaEditorDocumentKind.Component;
    }

    public static string GetUntitledFileName(
        AkburaEditorDocumentKind kind)
    {
        return kind == AkburaEditorDocumentKind.Akcss
            ? "untitled.akcss"
            : "untitled.akbura";
    }
}

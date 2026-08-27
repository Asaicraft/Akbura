namespace Akbura.Workspaces.AutomaticPairing;

internal readonly struct AkburaPairDecision
{
    public AkburaPairDecision(
        AkburaPairContextKind contextKind,
        char openingCharacter,
        string closingText)
    {
        ContextKind = contextKind;
        OpeningCharacter = openingCharacter;
        ClosingText = closingText ?? string.Empty;
    }

    public static AkburaPairDecision None => default;

    public AkburaPairContextKind ContextKind { get; }

    public char OpeningCharacter { get; }

    public string ClosingText { get; }

    public bool IsValid =>
        ContextKind != AkburaPairContextKind.None &&
        !string.IsNullOrEmpty(ClosingText);

    public bool IsFixed => IsValid && ClosingText.Length == 1;
}

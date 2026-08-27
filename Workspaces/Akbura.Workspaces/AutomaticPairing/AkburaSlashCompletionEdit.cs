namespace Akbura.Workspaces.AutomaticPairing;

internal readonly struct AkburaSlashCompletionEdit
{
    public AkburaSlashCompletionEdit(
        string insertionText,
        int overtypeLength,
        bool completesClosingTag)
    {
        InsertionText = insertionText ?? string.Empty;
        OvertypeLength = overtypeLength;
        CompletesClosingTag = completesClosingTag;
    }

    public string InsertionText { get; }

    public int OvertypeLength { get; }

    public bool CompletesClosingTag { get; }

    public bool IsValid =>
        InsertionText.Length != 0 || OvertypeLength != 0;
}

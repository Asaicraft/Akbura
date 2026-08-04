namespace Akbura.Workspaces;

public readonly record struct AkburaDocumentId(Guid Value)
{
    public static AkburaDocumentId CreateNew()
    {
        return new AkburaDocumentId(Guid.NewGuid());
    }

    public override string ToString()
    {
        return Value.ToString("D");
    }
}

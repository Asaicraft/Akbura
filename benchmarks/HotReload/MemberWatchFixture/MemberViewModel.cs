namespace Demo;

public sealed class MemberViewModel
{
    private readonly string _prefix = "Akbura Counter";
    public static MemberViewModel Current { get; } = new MemberViewModel();
    public string GetName()
    {
        return _prefix + " 0";
    }
    public int GetAge() => 20;
    public bool IsActive() => true;
}

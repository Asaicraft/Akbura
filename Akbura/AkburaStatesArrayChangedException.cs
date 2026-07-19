namespace Akbura;

public sealed class AkburaStatesArrayChangedException : AkburaException
{
    public AkburaStatesArrayChangedException(AkburaControl akburaControl)
        : base(CreateMessage(akburaControl))
    {
        AkburaControl = akburaControl;
    }

    public AkburaControl AkburaControl
    {
        get;
    }

    private static string CreateMessage(AkburaControl akburaControl)
    {
        ArgumentNullException.ThrowIfNull(akburaControl);

        return $"'{akburaControl.GetType().FullName}.GetStates()' must return " +
            "the same immutable array instance on every call.";
    }
}

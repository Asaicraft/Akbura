namespace Akbura;

public sealed class AkburaCommandsArrayChangedException : AkburaException
{
    public AkburaCommandsArrayChangedException(AkburaControl akburaControl)
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

        return $"'{akburaControl.GetType().FullName}.GetCommands()' must return " +
            "the same immutable array instance on every call.";
    }
}

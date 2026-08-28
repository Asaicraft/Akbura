namespace Akbura;

public sealed class AkburaUseHookOutsideRenderException : AkburaException
{
    public AkburaUseHookOutsideRenderException(AkburaControl akburaControl)
        : base(CreateMessage(akburaControl))
    {
        AkburaControl = akburaControl;
    }

    public AkburaControl AkburaControl { get; }

    private static string CreateMessage(AkburaControl akburaControl)
    {
        ArgumentNullException.ThrowIfNull(akburaControl);

        return $"A render use hook for '{akburaControl.GetType().FullName}' was invoked " +
            "outside its Update() frame.";
    }
}

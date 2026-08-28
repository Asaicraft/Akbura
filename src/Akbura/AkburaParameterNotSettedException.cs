using Akbura.ComponentTree;

namespace Akbura;

public sealed class AkburaParameterNotSettedException : AkburaException
{
    public AkburaParameterNotSettedException(
        AkburaControl akburaControl,
        Parameter parameter)
        : base(CreateMessage(akburaControl, parameter))
    {
        AkburaControl = akburaControl;
        Parameter = parameter;
    }

    public AkburaControl AkburaControl
    {
        get;
    }

    public Parameter Parameter
    {
        get;
    }

    private static string CreateMessage(
        AkburaControl akburaControl,
        Parameter parameter)
    {
        ArgumentNullException.ThrowIfNull(akburaControl);
        ArgumentNullException.ThrowIfNull(parameter);

        return $"Required parameter '{parameter.Name}' was not set for component " +
            $"'{akburaControl.GetType().FullName}'.";
    }
}

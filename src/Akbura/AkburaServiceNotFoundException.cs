using Akbura.ComponentTree;

namespace Akbura;

public sealed class AkburaServiceNotFoundException : AkburaException
{
    public AkburaServiceNotFoundException(
        AkburaControl akburaControl,
        InjectService service)
        : base(CreateMessage(akburaControl, service))
    {
        AkburaControl = akburaControl;
        Service = service;
    }

    public AkburaControl AkburaControl
    {
        get;
    }

    public InjectService Service
    {
        get;
    }

    private static string CreateMessage(
        AkburaControl akburaControl,
        InjectService service)
    {
        ArgumentNullException.ThrowIfNull(akburaControl);
        ArgumentNullException.ThrowIfNull(service);

        return $"Required service '{service.ServiceType.FullName}' for " +
            $"'{akburaControl.GetType().FullName}.{service.Name}' was not found.";
    }
}

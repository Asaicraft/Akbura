using Splat;

namespace AkburaTemplateNamespace.Infrastructure;

internal sealed class SplatServiceProvider : IServiceProvider
{
    public object? GetService(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);

        return Locator.Current.GetService(serviceType);
    }
}

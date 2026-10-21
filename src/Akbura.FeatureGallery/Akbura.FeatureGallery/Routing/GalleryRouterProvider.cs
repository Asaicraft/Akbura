using System;

namespace Akbura.FeatureGallery.Routing;

public sealed class GalleryRouterProvider(IRouter router) : IServiceProvider
{
    public object? GetService(Type serviceType) => serviceType == typeof(IRouter) ? router : null;
}

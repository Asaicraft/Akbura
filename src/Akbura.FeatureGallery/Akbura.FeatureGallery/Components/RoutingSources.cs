using Akbura.FeatureGallery.Routing;

namespace Akbura.FeatureGallery.Components;

// The current state-initializer semantic probe does not expose DSL-injected
// members. A typed CLR getter makes the same injected service visible to it,
// without extending the compiler or introducing any additional route storage.
partial class App
{
    private IRouter RouteSource => RouterService;
}

partial class Router
{
    private IRouter RouteSource => RouterService;
}

partial class Link
{
    private IRouter RouteSource => RouterService;
}

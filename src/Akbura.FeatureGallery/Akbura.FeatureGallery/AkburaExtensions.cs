using Akbura.Engine;
using Akbura.FeatureGallery.Routing;
using Avalonia;
using System;
using System.Collections.Generic;
using System.Text;

namespace Akbura.FeatureGallery;

public static class AkburaExtensions
{
    extension(AkburaEngineExtensions.AkburaEngineBuilder builder)
    {
        public AkburaEngineExtensions.AkburaEngineBuilder WithGalleryRouter(IRouter router)
        {
            ArgumentNullException.ThrowIfNull(router);
            return builder.WithServiceProvider(new GalleryRouterProvider(router));
        }
    }

    extension(AppBuilder builder)
    {
        public AppBuilder UseGalleryAkbura(IRouter? router = null) => builder.UseAkbura(builder =>
        {
            builder.WithGalleryOptions();
            builder.WithGalleryRouter(router ?? new MockRouter());
        });
    }
}

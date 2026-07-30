using Akbura.Engine;
using Avalonia;
using System;
using System.Collections.Generic;
using System.Text;

namespace Akbura.FeatureGallery;

public static class AkburaExtensions
{
    extension(AkburaEngineExtensions.AkburaEngineBuilder builder)
    {

    }

    extension(AppBuilder builder)
    {
        public AppBuilder UseGalleryAkbura() => builder.UseAkbura(builder =>
        {
            builder.WithGalleryOptions();
        });
    }
}

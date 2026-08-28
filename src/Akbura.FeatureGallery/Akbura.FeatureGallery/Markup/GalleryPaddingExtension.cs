using System;

namespace Akbura.FeatureGallery.Markup;

public sealed class GalleryPaddingExtension
{
    private readonly double _value;

    public GalleryPaddingExtension(double value)
    {
        _value = value;
    }

    public double ProvideValue(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return _value;
    }
}

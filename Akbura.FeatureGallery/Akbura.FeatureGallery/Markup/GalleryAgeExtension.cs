using System;
using System.Collections.Generic;
using System.Text;

namespace Akbura.FeatureGallery.Markup;

public sealed class GalleryAgeExtension
{
    private readonly int _value;

    public GalleryAgeExtension(int value)
    {
        _value = value;
    }

    public int ProvideValue(IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return _value;
    }
}
using Akbura.Markup;
using System;
using System.Collections.Generic;
using System.Text;

namespace Akbura.FeatureGallery.Markup;

[UtilityVariant(0)]
public sealed class WhenExtension
{
    private readonly bool _condition;

    public WhenExtension(bool condition)
    {
        _condition = condition;
    }

    public bool ProvideValue()
    {
        return _condition;
    }
}
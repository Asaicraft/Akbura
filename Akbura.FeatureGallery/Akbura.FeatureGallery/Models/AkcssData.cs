using System;
using System.Collections.Generic;
using System.Text;

namespace Akbura.FeatureGallery.Models;

public sealed class AkcssData
{
    public string Name { get; set; } = "Default";

    public int Age { get; set; }

    public double Scale { get; set; } = 1.0;

    public override string ToString()
    {
        return $"{Name} · Age {Age} · Scale {Scale:0.##}";
    }
}
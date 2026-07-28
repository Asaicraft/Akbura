using Avalonia.Metadata;
using System;
using System.Collections.Generic;
using System.Text;

namespace Akbura.FeatureGallery.Components;

public sealed class Page
{
    public string Url { get; set; } = null!;


    [Content]
    [TemplateContent]
    public object Content { get; set; } = null!;
}
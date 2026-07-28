using Avalonia.Metadata;
using System;
using System.Collections.Generic;
using System.Text;

namespace Akbura.FeatureGallery.Components;

internal sealed class Page
{
    public string Uri { get; set; } = null!;


    [Content]
    [TemplateContent]
    public object Content { get; set; } = null!;
}
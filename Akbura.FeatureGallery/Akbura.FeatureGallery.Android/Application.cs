using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using System;
using System.Collections.Generic;
using System.Text;

namespace Akbura.FeatureGallery.Android;

[Application]
public class Application : AvaloniaAndroidApplication<App>
{
    protected Application(nint javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
    {
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        return base.CustomizeAppBuilder(builder)
            .WithInterFont();
    }
}
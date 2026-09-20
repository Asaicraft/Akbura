using Android.App;
using Android.Content.PM;
using Avalonia.Android;

namespace AkburaTemplateNamespace.Android;

[Activity(
    Label = "AkburaRawProjectNamePlaceholder.Android",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
}

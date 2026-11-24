using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Akbura.AvaloniaEdit.BrowserSmoke;

public sealed partial class SmokeApp : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is ISingleViewApplicationLifetime lifetime)
        {
            lifetime.MainView = new SmokeView();
        }
        base.OnFrameworkInitializationCompleted();
    }
}

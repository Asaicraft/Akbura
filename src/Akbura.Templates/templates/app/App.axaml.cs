using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

//-:cnd:noEmit
#if DEBUG
using Akbura.Diagnostics;
using Avalonia.Input;
#endif
//+:cnd:noEmit

namespace AkburaTemplateNamespace;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);

//-:cnd:noEmit
#if DEBUG
        // Avalonia Developer Tools: F12.
        this.AttachDeveloperTools();

        // Akbura component inspector: Ctrl+F12.
        this.AttachAkburaDevTools(options =>
        {
            options.ToggleGesture = new KeyGesture(
                Key.F12,
                KeyModifiers.Control);
        });
#endif
//+:cnd:noEmit
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}

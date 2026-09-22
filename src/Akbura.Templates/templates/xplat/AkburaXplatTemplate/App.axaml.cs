using AkburaTemplateNamespace.Infrastructure;
using AkburaTemplateNamespace.Views;
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
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = AppServices.Current.MainViewModel;
            desktop.MainWindow = new MainWindow
            {
                DataContext = viewModel,
                Content = MainViewHost.CreateWithDataContext(viewModel)
            };

            desktop.Exit += (_, _) => AppServices.DisposeOwnedServices();

//-:cnd:noEmit
#if DEBUG
            // Desktop Developer Tools: F12; Akbura component inspector: Ctrl+F12.
            this.AttachDeveloperTools();
            this.AttachAkburaDevTools(options =>
            {
                options.ToggleGesture = new KeyGesture(Key.F12, KeyModifiers.Control);
            });
#endif
//+:cnd:noEmit
        }
        else if (ApplicationLifetime is IActivityApplicationLifetime activity)
        {
            var viewModel = AppServices.Current.MainViewModel;
            // Android can recreate an Activity. Each invocation must receive a fresh visual tree.
            activity.MainViewFactory = () => MainViewHost.CreateWithDataContext(viewModel);
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            var viewModel = AppServices.Current.MainViewModel;
            singleView.MainView = MainViewHost.CreateWithDataContext(viewModel);
        }

        base.OnFrameworkInitializationCompleted();
    }
}

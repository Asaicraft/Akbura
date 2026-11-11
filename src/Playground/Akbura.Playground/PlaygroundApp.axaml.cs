using Akbura.Workspaces.Projects;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Akbura.Playground;

public sealed partial class PlaygroundApp : Application
{
    public Func<AkburaReferenceSet>? PrepareReferences
    {
        get; init;
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            singleView.MainView = new PlaygroundView(PrepareReferences);
        }

        base.OnFrameworkInitializationCompleted();
    }
}

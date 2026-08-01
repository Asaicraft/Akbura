using CommunityToolkit.Mvvm.ComponentModel;

namespace Akbura.SyntaxViewer.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    public partial string Greeting { get; set; } = "Welcome to Avalonia!";
}

using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using AkburaTemplateNamespace.Services;

namespace AkburaTemplateNamespace.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly IGreetingService _greetingService;
    private int _count;
    private string _userName = "Akbura";

    public MainViewModel(IGreetingService greetingService)
    {
        _greetingService = greetingService ?? throw new ArgumentNullException(nameof(greetingService));
        IncrementCommand = new RelayCommand(Increment);
        ResetCounterCommand = new RelayCommand(ResetCounter, () => Count != 0);
        UseExampleNameCommand = new RelayCommand(() => UserName = "Developer");
    }

    public int Count
    {
        get => _count;
        private set
        {
            if (SetProperty(ref _count, value))
            {
                OnPropertyChanged(nameof(CountText));
                ResetCounterCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string CountText => Count.ToString(CultureInfo.InvariantCulture);

    public string UserName
    {
        get => _userName;
        set
        {
            if (SetProperty(ref _userName, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(Greeting));
            }
        }
    }

    public string Greeting => _greetingService.CreateGreeting(UserName);

    public IRelayCommand IncrementCommand { get; }

    public IRelayCommand ResetCounterCommand { get; }

    public IRelayCommand UseExampleNameCommand { get; }

    private void Increment() => Count++;

    private void ResetCounter() => Count = 0;
}

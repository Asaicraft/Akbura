using System.Globalization;
using System.Reactive;
using System.Reactive.Linq;
using AkburaTemplateNamespace.Services;
using ReactiveUI;

namespace AkburaTemplateNamespace.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly IGreetingService _greetingService;
    private int _count;
    private string _userName = "Akbura";

    public MainViewModel(IGreetingService greetingService)
    {
        _greetingService = greetingService ?? throw new ArgumentNullException(nameof(greetingService));
        IncrementCommand = ReactiveCommand.Create(Increment);
        ResetCounterCommand = ReactiveCommand.Create(
            ResetCounter,
            this.WhenAnyValue(viewModel => viewModel.Count).Select(count => count != 0));
        UseExampleNameCommand = ReactiveCommand.Create(() =>
        {
            UserName = "Developer";
        });
    }

    public int Count
    {
        get => _count;
        private set
        {
            if (_count == value)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _count, value);
            this.RaisePropertyChanged(nameof(CountText));
        }
    }

    public string CountText => Count.ToString(CultureInfo.InvariantCulture);

    public string UserName
    {
        get => _userName;
        set
        {
            var normalized = value ?? string.Empty;
            if (_userName == normalized)
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _userName, normalized);
            this.RaisePropertyChanged(nameof(Greeting));
        }
    }

    public string Greeting => _greetingService.CreateGreeting(UserName);

    public string GeneratedStatus => "ReactiveUI ViewModel";

    public ReactiveCommand<Unit, Unit> IncrementCommand { get; }

    public ReactiveCommand<Unit, Unit> ResetCounterCommand { get; }

    public ReactiveCommand<Unit, Unit> UseExampleNameCommand { get; }

    private void Increment() => Count++;

    private void ResetCounter() => Count = 0;
}

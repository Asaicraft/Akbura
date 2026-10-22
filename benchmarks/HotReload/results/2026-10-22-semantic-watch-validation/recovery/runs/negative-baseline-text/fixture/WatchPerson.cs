using System.ComponentModel;

namespace Demo;

public sealed class WatchPerson : INotifyPropertyChanged
{
    private PropertyChangedEventHandler? _changed;
    private string _firstName = "First";
    private string _lastName = "Last";

    public static WatchPerson Current { get; } = new();
    public int Subscribers => _changed?.GetInvocationList().Length ?? 0;

    public event PropertyChangedEventHandler? PropertyChanged
    {
        add => _changed += value;
        remove => _changed -= value;
    }

    public string FirstName
    {
        get => _firstName;
        set
        {
            if (_firstName == value)
            {
                return;
            }
            _firstName = value;
            _changed?.Invoke(this, new PropertyChangedEventArgs(nameof(FirstName)));
        }
    }

    public string LastName
    {
        get => _lastName;
        set
        {
            if (_lastName == value)
            {
                return;
            }
            _lastName = value;
            _changed?.Invoke(this, new PropertyChangedEventArgs(nameof(LastName)));
        }
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace Akbura.FeatureGallery.Models;

public sealed class StateBindingDemoModel : ObservableObject
{
    private int _selectedIndex;
    private string _draft = "Write through the in state";
    private int _remoteNameVersion;
    private int _remoteDraftVersion;

    public StateBindingDemoModel()
    {
        People =
        [
            new StateBindingPerson("Ada"),
            new StateBindingPerson("Linus"),
            new StateBindingPerson("Grace"),
        ];
    }

    public ObservableCollection<StateBindingPerson> People { get; }

    public int SelectedIndex
    {
        get => _selectedIndex;
        set => SetProperty(ref _selectedIndex, value);
    }

    public string Draft
    {
        get => _draft;
        set => SetProperty(ref _draft, value);
    }

    public void RenameSelected()
    {
        _remoteNameVersion++;
        People[SelectedIndex].Name = $"Remote name {_remoteNameVersion}";
    }

    public void ChangeDraftExternally()
    {
        _remoteDraftVersion++;
        Draft = $"Model-only draft {_remoteDraftVersion}";
    }
}

public sealed class StateBindingPerson : ObservableObject
{
    private string _name;

    public StateBindingPerson(string name)
    {
        _name = name;
    }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }
}

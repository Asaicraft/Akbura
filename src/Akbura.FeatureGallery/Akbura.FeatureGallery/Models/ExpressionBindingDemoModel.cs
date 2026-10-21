using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.ObjectModel;

namespace Akbura.FeatureGallery.Models;

public sealed class ExpressionBindingDemoModel : ObservableObject
{
    private string _firstName = "Ada";
    private string _lastName = "Lovelace";
    private ExpressionBindingPerson? _profile = new("Ada's profile");
    private int _selectedIndex;
    private bool _usePrimaryName = true;
    private string _primaryName = "Primary Ada";
    private string _fallbackName = "Guest Grace";
    private bool _isSelected;
    private bool _hasError;
    private int _revision;

    public string FirstName { get => _firstName; set => SetProperty(ref _firstName, value); }
    public string LastName { get => _lastName; set => SetProperty(ref _lastName, value); }
    public ExpressionBindingPerson? Profile { get => _profile; set => SetProperty(ref _profile, value); }

    // The demo never removes the last person. Index changes are clamped before
    // publishing a notification, so the expression always has a valid receiver.
    public ObservableCollection<ExpressionBindingPerson> People { get; } =
    [
        new("Ada"), new("Grace"), new("Linus"),
    ];

    public int SelectedIndex
    {
        get => _selectedIndex;
        set => SetProperty(ref _selectedIndex, Math.Clamp(value, 0, People.Count - 1));
    }

    public bool UsePrimaryName { get => _usePrimaryName; set => SetProperty(ref _usePrimaryName, value); }
    public string PrimaryName { get => _primaryName; set => SetProperty(ref _primaryName, value); }
    public string FallbackName { get => _fallbackName; set => SetProperty(ref _fallbackName, value); }
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
    public bool HasError { get => _hasError; set => SetProperty(ref _hasError, value); }

    public void RenameProfile()
    {
        if (Profile is { } profile) profile.Name = $"Renamed profile {++_revision}";
    }

    public void ReplaceProfile() => Profile = new($"New profile {++_revision}");

    public void RenameSelected() => People[SelectedIndex].Name = $"Selected person {++_revision}";

    public void RenameUnselected() => People[(SelectedIndex + 1) % People.Count].Name = $"Other person {++_revision}";

    public void ReplaceSelected() => People[SelectedIndex] = new($"Replacement person {++_revision}");

    public void Reset()
    {
        _revision = 0;
        FirstName = "Ada";
        LastName = "Lovelace";
        Profile = new("Ada's profile");
        SelectedIndex = 0;
        People[0] = new("Ada");
        People[1] = new("Grace");
        People[2] = new("Linus");
        UsePrimaryName = true;
        PrimaryName = "Primary Ada";
        FallbackName = "Guest Grace";
        IsSelected = false;
        HasError = false;
    }
}

public sealed class ExpressionBindingPerson(string name) : ObservableObject
{
    private string _name = name;
    public string Name { get => _name; set => SetProperty(ref _name, value); }
}

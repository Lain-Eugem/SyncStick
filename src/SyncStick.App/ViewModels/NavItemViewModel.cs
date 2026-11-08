using System.Windows;
using System.Windows.Media;
using SyncStick.App.Infrastructure;

namespace SyncStick.App.ViewModels;

public sealed class NavItemViewModel : ObservableObject
{
    private bool _isSelected;
    private string? _badge;

    public NavItemViewModel(string key, string title, string iconResourceKey)
    {
        Key = key;
        Title = title;
        Icon = Application.Current.TryFindResource(iconResourceKey) as Geometry ?? Geometry.Empty;
    }

    public string Key { get; }

    public string Title { get; }

    public Geometry Icon { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public string? Badge
    {
        get => _badge;
        set
        {
            if (SetProperty(ref _badge, value))
            {
                OnPropertyChanged(nameof(HasBadge));
            }
        }
    }

    public bool HasBadge => !string.IsNullOrWhiteSpace(Badge);
}

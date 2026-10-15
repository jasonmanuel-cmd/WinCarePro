using CommunityToolkit.Mvvm.ComponentModel;
using WinCareDesktop.ViewModels;

namespace WinCareDesktop.Models;

public class NavigationItem : ObservableObject
{
    public string Title { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public ViewModelBase ViewModel { get; set; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    private string _badge = string.Empty;
    public string Badge
    {
        get => _badge;
        set => SetProperty(ref _badge, value);
    }

    public NavigationItem(string title, string icon, string description, ViewModelBase viewModel)
    {
        Title = title;
        Icon = icon;
        Description = description;
        ViewModel = viewModel;
    }
}

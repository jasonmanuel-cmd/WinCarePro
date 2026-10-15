using CommunityToolkit.Mvvm.ComponentModel;

namespace WinCareDesktop.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
    private string _title = string.Empty;
    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    private string _subtitle = string.Empty;
    public string Subtitle
    {
        get => _subtitle;
        set => SetProperty(ref _subtitle, value);
    }

    public virtual void OnNavigatedTo() { }
    public virtual void OnNavigatedFrom() { }
}

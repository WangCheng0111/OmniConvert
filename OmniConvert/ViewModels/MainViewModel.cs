using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace OmniConvert.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    public partial bool IsSettingsOpen { get; set; }

    [RelayCommand]
    private void OpenSettings()
    {
        IsSettingsOpen = true;
    }

    [RelayCommand]
    private void CloseSettings()
    {
        IsSettingsOpen = false;
    }
}

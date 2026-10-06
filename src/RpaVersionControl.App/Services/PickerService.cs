using Microsoft.UI.Xaml;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace RpaVersionControl.App.Services;

public sealed class PickerService
{
    private readonly Window _window;

    public PickerService(Window window) => _window = window;

    public async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(_window));
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }
}

using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using RpaVersionControl.App.Views;
using WinRT.Interop;

namespace RpaVersionControl.App;

public sealed partial class MainWindow : Window
{
    private bool _allowClose;
    private AppWindow? _appWindow;

    public IRelayCommand ShowWindowCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }
    public IRelayCommand ExitCommand { get; }

    public MainWindow()
    {
        ShowWindowCommand = new RelayCommand(ShowFromTray);
        RefreshCommand = new AsyncRelayCommand(RefreshCurrentAsync);
        ExitCommand = new RelayCommand(ExitApplication);

        InitializeComponent();
        Title = "RPA Version Control";
        SystemBackdrop = new MicaBackdrop();

        Activated += MainWindow_Activated;
        Nav.Loaded += Nav_Loaded;

        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        _appWindow.Closing += AppWindow_Closing;

        App.Current.Services.BackgroundSync.DataChanged += BackgroundSync_DataChanged;
    }


    public async Task RefreshSecurityUiAsync()
    {
        var services = App.Current.Services;
        var user = services.Users.GetCurrent();
        UserNameText.Text = user.DisplayName;

        var layout = await services.RootProvider.TryGetAsync();
        if (layout is null)
        {
            UserRoleText.Text = "Configuração inicial necessária";
            QaQueueItem.Visibility = Visibility.Collapsed;
            return;
        }

        await services.Security.BootstrapIfMissingAsync(layout);
        var isQa = await services.Security.IsQaAsync(layout);
        var isAdmin = await services.Security.IsAdminAsync(layout);
        QaQueueItem.Visibility = isQa ? Visibility.Visible : Visibility.Collapsed;
        UserRoleText.Text = isAdmin ? "Administrador / QA" : isQa ? "QA" : "Desenvolvedor";
    }

    public void NavigateToChange(string changeId, bool forQa)
    {
        ShowFromTray();
        ContentFrame.Navigate(forQa ? typeof(QaReviewPage) : typeof(ChangeDetailPage), changeId);
    }

    public void NavigateToSubmission(string projectId, string? existingChangeId = null)
    {
        ContentFrame.Navigate(typeof(SubmissionPage), new SubmissionNavigationArgs(projectId, existingChangeId));
    }

    public void NavigateToHistory(string? projectId = null)
    {
        ContentFrame.Navigate(typeof(HistoryPage), projectId);
    }

    private async void Nav_Loaded(object sender, RoutedEventArgs e)
    {
        var services = App.Current.Services;
        await RefreshSecurityUiAsync();

        var layout = await services.RootProvider.TryGetAsync();
        if (layout is null)
        {
            Nav.SelectedItem = Nav.SettingsItem;
            ContentFrame.Navigate(typeof(SettingsPage));
            return;
        }

        Nav.SelectedItem = Nav.MenuItems[0];
        ContentFrame.Navigate(typeof(DashboardPage));

        var localSettings = await services.LocalSettings.GetAsync();
        if (localSettings.StartMinimized ||
            Environment.GetCommandLineArgs().Any(x => x.Equals("--background", StringComparison.OrdinalIgnoreCase)))
            HideToTray();
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            ContentFrame.Navigate(typeof(SettingsPage));
            return;
        }

        if (args.SelectedItemContainer?.Tag is not string tag)
            return;

        var type = tag switch
        {
            "dashboard" => typeof(DashboardPage),
            "projects" => typeof(ProjectsPage),
            "mychanges" => typeof(MyChangesPage),
            "qa" => typeof(QaQueuePage),
            "history" => typeof(HistoryPage),
            _ => typeof(DashboardPage)
        };

        ContentFrame.Navigate(type);
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose) return;
        args.Cancel = true;
        HideToTray();
    }

    private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        // Intentionally empty: activation keeps the app responsive after tray restore.
    }

    private void BackgroundSync_DataChanged(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(async () => await RefreshCurrentAsync());
    }

    private async Task RefreshCurrentAsync()
    {
        if (ContentFrame.Content is IRefreshable refreshable)
            await refreshable.RefreshAsync();
    }

    public void RestoreFromTray() => ShowFromTray();

    private void ShowFromTray()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        ShowWindow(hwnd, SW_RESTORE);
        SetForegroundWindow(hwnd);
    }

    private void HideToTray()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        ShowWindow(hwnd, SW_HIDE);
    }

    private void ExitApplication()
    {
        _allowClose = true;
        App.Current.Services.Dispose();
        TrayIcon.Dispose();
        Close();
    }

    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    private const int SW_HIDE = 0;
    private const int SW_RESTORE = 9;
}

public sealed record SubmissionNavigationArgs(string ProjectId, string? ExistingChangeId);

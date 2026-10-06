using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace RpaVersionControl.App.Views;

public sealed partial class MyChangesPage : Page, IRefreshable
{
    public MyChangesPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        var services = App.Current.Services;
        var layout = await services.RootProvider.TryGetAsync();
        if (layout is null) return;

        var user = services.Users.GetCurrent();
        var changes = await services.Changes.ListAsync(layout);
        ChangesList.ItemsSource = changes
            .Where(x => string.Equals(x.DeveloperWindowsUser, user.WindowsUser, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string changeId })
            Frame.Navigate(typeof(ChangeDetailPage), changeId);
    }
}

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RpaVersionControl.Core.Models;

namespace RpaVersionControl.App.Views;

public sealed partial class DashboardPage : Page, IRefreshable
{
    public DashboardPage()
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
        var projects = await services.Projects.ListAsync(layout);
        var changes = await services.Changes.ListAsync(layout);
        var isQa = await services.Security.IsQaAsync(layout);

        ProjectsCount.Text = projects.Count.ToString();
        MyPendingCount.Text = changes.Count(x =>
            string.Equals(x.DeveloperWindowsUser, user.WindowsUser, StringComparison.OrdinalIgnoreCase) &&
            x.Status is ChangeStatus.AdjustmentsRequested or ChangeStatus.NeedsRebase or ChangeStatus.RetrofitRequired)
            .ToString();
        QaPendingCount.Text = isQa
            ? changes.Count(x => x.Status is ChangeStatus.Submitted or ChangeStatus.PublishFailed).ToString()
            : "—";

        SubtitleText.Text = $"{user.DisplayName} • dados compartilhados em {layout.Root}";
        var recent = changes.Take(8).ToList();
        RecentList.ItemsSource = recent;
        RecentEmptyState.Visibility = recent.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}

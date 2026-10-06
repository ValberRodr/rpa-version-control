using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RpaVersionControl.Core.Models;

namespace RpaVersionControl.App.Views;

public sealed partial class QaQueuePage : Page, IRefreshable
{
    public QaQueuePage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        var services = App.Current.Services;
        var layout = await services.RootProvider.TryGetAsync();
        if (layout is null) return;

        if (!await services.Security.IsQaAsync(layout))
        {
            QueueList.ItemsSource = Array.Empty<ChangeRequest>();
            return;
        }

        var changes = await services.Changes.ListAsync(layout);
        QueueList.ItemsSource = changes
            .Where(x => x.Status is ChangeStatus.Submitted or ChangeStatus.PublishFailed or ChangeStatus.ApprovedPendingPublish)
            .ToList();
    }

    private void Review_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string changeId })
            Frame.Navigate(typeof(QaReviewPage), changeId);
    }
}

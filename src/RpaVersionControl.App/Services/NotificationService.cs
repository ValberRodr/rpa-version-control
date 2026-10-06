using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace RpaVersionControl.App.Services;

public sealed class NotificationService : IDisposable
{
    private bool _registered;
    public event EventHandler? Invoked;

    public void Register()
    {
        if (_registered) return;

        try
        {
            AppNotificationManager.Default.NotificationInvoked += OnNotificationInvoked;

            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "App.png");
            if (File.Exists(iconPath))
                AppNotificationManager.Default.Register("RPA Version Control", new Uri(iconPath));
            else
                AppNotificationManager.Default.Register();

            _registered = true;
        }
        catch
        {
            try { AppNotificationManager.Default.NotificationInvoked -= OnNotificationInvoked; } catch { }
            _registered = false;
        }
    }

    public void Show(string title, string message)
    {
        if (!_registered) return;

        try
        {
            var notification = new AppNotificationBuilder()
                .AddText(title)
                .AddText(message)
                .BuildNotification();

            AppNotificationManager.Default.Show(notification);
        }
        catch
        {
            // Notification failure must never break versioning.
        }
    }

    private void OnNotificationInvoked(
        AppNotificationManager sender,
        AppNotificationActivatedEventArgs args)
    {
        Invoked?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        try { AppNotificationManager.Default.NotificationInvoked -= OnNotificationInvoked; } catch { }
        if (!_registered) return;
        try { AppNotificationManager.Default.Unregister(); } catch { }
        _registered = false;
    }
}

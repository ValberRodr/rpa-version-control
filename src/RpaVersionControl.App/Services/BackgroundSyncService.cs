using RpaVersionControl.Core.Models;

namespace RpaVersionControl.App.Services;

public sealed class BackgroundSyncService : IDisposable
{
    private readonly AppServices _services;
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public event EventHandler? DataChanged;

    public BackgroundSyncService(AppServices services) => _services = services;

    public void Start()
    {
        if (_loop is not null) return;
        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => LoopAsync(_cts.Token));
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var settings = await _services.LocalSettings.GetAsync();
                var layout = await _services.RootProvider.TryGetAsync();
                if (layout is not null)
                {
                    var events = await _services.Audit.ReadSinceAsync(layout, settings.LastAuditSeenUtc, ct);
                    if (events.Count > 0)
                    {
                        var user = _services.Users.GetCurrent();
                        var isQa = await _services.Security.IsQaAsync(layout, ct);

                        foreach (var evt in events)
                        {
                            if (string.Equals(evt.ActorWindowsUser, user.WindowsUser, StringComparison.OrdinalIgnoreCase))
                                continue;

                            var targetsUser = !string.IsNullOrWhiteSpace(evt.TargetWindowsUser) &&
                                string.Equals(evt.TargetWindowsUser, user.WindowsUser, StringComparison.OrdinalIgnoreCase);
                            var targetsQa = string.Equals(evt.TargetRole, "QA", StringComparison.OrdinalIgnoreCase) && isQa;

                            if (targetsUser || targetsQa)
                                _services.Notifications.Show("RPA Version Control", evt.Message);
                        }

                        settings.LastAuditSeenUtc = events.Max(x => x.TimestampUtc);
                        await _services.LocalSettings.SaveAsync(settings);
                        DataChanged?.Invoke(this, EventArgs.Empty);
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(settings.PollSeconds, 15, 300)), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                await Task.Delay(TimeSpan.FromSeconds(20), ct);
            }
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        try { _loop?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _cts?.Dispose();
        _cts = null;
        _loop = null;
    }
}

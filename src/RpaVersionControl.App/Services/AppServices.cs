using Microsoft.UI.Xaml;
using RpaVersionControl.Core.Services;

namespace RpaVersionControl.App.Services;

public sealed class AppServices : IDisposable
{
    public AtomicJsonStore Json { get; } = new();
    public NetworkLockService Locks { get; } = new();
    public FileManifestService Manifests { get; } = new();
    public ProjectStore Projects { get; }
    public ChangeStore Changes { get; }
    public AuditStore Audit { get; }
    public GitVersionService Git { get; }
    public VersioningWorkflowService Workflow { get; }

    public LocalSettingsService LocalSettings { get; } = new();
    public WindowsUserService Users { get; } = new();
    public SharedRootProvider RootProvider { get; }
    public SecurityService Security { get; }
    public StartupService Startup { get; } = new();
    public NotificationService Notifications { get; } = new();
    public BackgroundSyncService BackgroundSync { get; }
    public PickerService? Picker { get; private set; }

    public AppServices()
    {
        Projects = new ProjectStore(Json);
        Changes = new ChangeStore(Json, Locks);
        Audit = new AuditStore(Json);
        Git = new GitVersionService(Manifests);
        Workflow = new VersioningWorkflowService(Json, Projects, Changes, Audit, Locks, Manifests, Git);
        RootProvider = new SharedRootProvider(LocalSettings);
        Security = new SecurityService(Json, Users);
        BackgroundSync = new BackgroundSyncService(this);
    }

    public async Task InitializeAsync(Window window)
    {
        Picker = new PickerService(window);
        if (window is MainWindow mainWindow)
        {
            Notifications.Invoked += (_, _) =>
                mainWindow.DispatcherQueue.TryEnqueue(mainWindow.RestoreFromTray);
        }
        Notifications.Register();

        var layout = await RootProvider.TryGetAsync();
        if (layout is not null)
            await Security.BootstrapIfMissingAsync(layout);

        BackgroundSync.Start();
    }

    public void Dispose()
    {
        BackgroundSync.Dispose();
        Notifications.Dispose();
    }
}

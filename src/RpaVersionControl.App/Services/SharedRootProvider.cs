using RpaVersionControl.Core.Services;

namespace RpaVersionControl.App.Services;

public sealed class SharedRootProvider
{
    private readonly LocalSettingsService _settings;

    public SharedRootProvider(LocalSettingsService settings) => _settings = settings;

    public async Task<SharedLayout?> TryGetAsync()
    {
        var root = await _settings.GetEffectiveSharedRootAsync();
        if (string.IsNullOrWhiteSpace(root)) return null;

        try
        {
            var layout = new SharedLayout(root);
            layout.Ensure();
            return layout;
        }
        catch
        {
            return null;
        }
    }

    public async Task<SharedLayout> GetRequiredAsync() =>
        await TryGetAsync() ?? throw new InvalidOperationException(
            "Configure a pasta compartilhada em Configurações.");
}

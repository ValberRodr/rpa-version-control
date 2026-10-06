using System.Text.Json;
using RpaVersionControl.Core.Models;

namespace RpaVersionControl.App.Services;

public sealed class LocalSettingsService
{
    private readonly string _path = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RpaVersionControl", "settings.json");

    private LocalAppSettings? _cache;

    public async Task<LocalAppSettings> GetAsync()
    {
        if (_cache is not null) return _cache;

        if (!File.Exists(_path))
            return _cache = new LocalAppSettings();

        try
        {
            var json = await File.ReadAllTextAsync(_path);
            _cache = JsonSerializer.Deserialize<LocalAppSettings>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new LocalAppSettings();
        }
        catch
        {
            _cache = new LocalAppSettings();
        }

        return _cache;
    }

    public async Task SaveAsync(LocalAppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(temp, json);
        File.Move(temp, _path, true);
        _cache = settings;
    }

    public async Task<string?> GetEffectiveSharedRootAsync()
    {
        var env = Environment.GetEnvironmentVariable("RPA_VERSION_CONTROL_ROOT");
        if (!string.IsNullOrWhiteSpace(env))
            return env.Trim();

        var sidecar = Path.Combine(AppContext.BaseDirectory, "shared-root.txt");
        if (File.Exists(sidecar))
        {
            var text = (await File.ReadAllTextAsync(sidecar)).Trim();
            if (!string.IsNullOrWhiteSpace(text))
                return text;
        }

        var settings = await GetAsync();
        return string.IsNullOrWhiteSpace(settings.SharedRootPath) ? null : settings.SharedRootPath;
    }
}

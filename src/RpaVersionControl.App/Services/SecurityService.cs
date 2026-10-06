using System.Security.Principal;
using RpaVersionControl.Core.Models;
using RpaVersionControl.Core.Services;

namespace RpaVersionControl.App.Services;

public sealed class SecurityService
{
    private readonly AtomicJsonStore _json;
    private readonly WindowsUserService _users;

    public SecurityService(AtomicJsonStore json, WindowsUserService users)
    {
        _json = json;
        _users = users;
    }

    public async Task BootstrapIfMissingAsync(SharedLayout layout, CancellationToken ct = default)
    {
        layout.Ensure();
        if (File.Exists(layout.SecurityFile)) return;

        var current = _users.GetCurrent();
        var config = new SecurityConfiguration
        {
            AdminUsers = new List<string> { current.WindowsUser },
            QaUsers = new List<string> { current.WindowsUser }
        };
        await _json.WriteAsync(layout.SecurityFile, config, ct);
    }

    public async Task<SecurityConfiguration> GetAsync(SharedLayout layout, CancellationToken ct = default) =>
        await _json.ReadAsync<SecurityConfiguration>(layout.SecurityFile, ct) ?? new SecurityConfiguration();

    public Task SaveAsync(SharedLayout layout, SecurityConfiguration config, CancellationToken ct = default) =>
        _json.WriteAsync(layout.SecurityFile, config, ct);

    public async Task<bool> IsAdminAsync(SharedLayout layout, CancellationToken ct = default)
    {
        var config = await GetAsync(layout, ct);
        var current = _users.GetCurrent().WindowsUser;
        return config.AdminUsers.Any(x => MatchesUser(x, current));
    }

    public async Task<bool> IsQaAsync(SharedLayout layout, CancellationToken ct = default)
    {
        var config = await GetAsync(layout, ct);
        var current = _users.GetCurrent().WindowsUser;

        if (config.AdminUsers.Any(x => MatchesUser(x, current)) ||
            config.QaUsers.Any(x => MatchesUser(x, current)))
            return true;

        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return config.QaWindowsGroups.Any(group => principal.IsInRole(group));
        }
        catch
        {
            return false;
        }
    }

    private static bool MatchesUser(string configured, string actual)
    {
        configured = configured.Trim();
        if (configured.Contains('\\'))
            return string.Equals(configured, actual, StringComparison.OrdinalIgnoreCase);

        var actualLeaf = actual.Contains('\\') ? actual.Split('\\').Last() : actual;
        return string.Equals(configured, actualLeaf, StringComparison.OrdinalIgnoreCase);
    }
}

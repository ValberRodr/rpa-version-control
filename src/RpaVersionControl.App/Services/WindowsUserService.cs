using System.Security.Principal;
using System.Globalization;
using RpaVersionControl.Core.Models;

namespace RpaVersionControl.App.Services;

public sealed class WindowsUserService
{
    public CurrentUser GetCurrent()
    {
        // Audit trail, Git authorship and QA/Admin matching all key off this value, so a
        // silent, domain-less fallback here would make those records ambiguous. Fail loudly
        // instead of guessing an identity.
        var full = WindowsIdentity.GetCurrent().Name;
        if (string.IsNullOrWhiteSpace(full))
            throw new InvalidOperationException(
                "Não foi possível determinar a identidade do usuário Windows atual.");

        return new CurrentUser(full, ToDisplayName(full), Environment.MachineName);
    }

    public static string ToDisplayName(string windowsUser)
    {
        var leaf = windowsUser.Contains('\\') ? windowsUser.Split('\\').Last() : windowsUser;
        var parts = leaf
            .Replace('_', '.')
            .Replace('-', '.')
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length == 0) return leaf;

        return string.Join(' ', parts.Select(p =>
            CultureInfo.CurrentCulture.TextInfo.ToTitleCase(p.ToLowerInvariant())));
    }
}

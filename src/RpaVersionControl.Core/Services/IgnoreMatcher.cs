using System.Text.RegularExpressions;

namespace RpaVersionControl.Core.Services;

public sealed class IgnoreMatcher
{
    public static readonly IReadOnlyList<string> DefaultPatterns = new[]
    {
        "**/.git/**",
        "**/__pycache__/**",
        "**/.venv/**",
        "**/venv/**",
        "**/logs/**",
        "**/log/**",
        "**/temp/**",
        "**/tmp/**",
        "**/output/**",
        "**/downloads/**",
        "*.log",
        "*.tmp",
        "*.bak",
        "*.pyc",
        "~$*"
    };

    private readonly List<Regex> _patterns;

    public IgnoreMatcher(IEnumerable<string> patterns)
    {
        _patterns = patterns
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(NormalizePattern)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(ToRegex)
            .ToList();
    }

    public bool IsIgnored(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/').TrimStart('/');
        return _patterns.Any(r => r.IsMatch(normalized));
    }

    public bool IsIgnoredDirectory(string relativeDirectory)
    {
        var normalized = relativeDirectory.Replace('\\', '/').Trim('/').Trim();
        if (normalized.Length == 0) return false;
        return IsIgnored(normalized + "/__rvc_probe__");
    }

    private static string NormalizePattern(string pattern)
    {
        var p = pattern.Trim().Replace('\\', '/').TrimStart('/');
        if (p.EndsWith('/')) p += "**";
        return p;
    }

    private static Regex ToRegex(string glob)
    {
        var anyDepth = glob.StartsWith("**/", StringComparison.Ordinal);
        if (anyDepth)
            glob = glob[3..];

        var escaped = Regex.Escape(glob)
            .Replace(@"\*\*", "§§DOUBLESTAR§§")
            .Replace(@"\*", @"[^/]*")
            .Replace(@"\?", @"[^/]")
            .Replace("§§DOUBLESTAR§§", @".*");

        if (anyDepth)
            escaped = @"^(?:.*/)?" + escaped;
        else if (!glob.Contains('/'))
            escaped = @"(?:^|.*/)" + escaped;
        else
            escaped = "^" + escaped;

        return new Regex(escaped + "$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}

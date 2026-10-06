using RpaVersionControl.Core.Models;

namespace RpaVersionControl.Core.Services;

public sealed class ProjectStore
{
    private readonly AtomicJsonStore _json;

    public ProjectStore(AtomicJsonStore json) => _json = json;

    public async Task<IReadOnlyList<ProjectDefinition>> ListAsync(SharedLayout layout, CancellationToken ct = default)
    {
        if (!Directory.Exists(layout.Projects))
            return Array.Empty<ProjectDefinition>();

        var result = new List<ProjectDefinition>();
        foreach (var file in Directory.EnumerateFiles(layout.Projects, "project.json", SearchOption.AllDirectories))
        {
            var project = await _json.ReadAsync<ProjectDefinition>(file, ct);
            if (project is not null) result.Add(project);
        }

        return result.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public Task<ProjectDefinition?> GetAsync(SharedLayout layout, string projectId, CancellationToken ct = default) =>
        _json.ReadAsync<ProjectDefinition>(layout.ProjectFile(projectId), ct);

    public Task SaveAsync(SharedLayout layout, ProjectDefinition project, CancellationToken ct = default)
    {
        project.UpdatedAtUtc = DateTimeOffset.UtcNow;
        return _json.WriteAsync(layout.ProjectFile(project.Id), project, ct);
    }

    public async Task<IReadOnlyList<VersionRecord>> ListVersionsAsync(
        SharedLayout layout, string projectId, CancellationToken ct = default)
    {
        var dir = layout.VersionsDirectory(projectId);
        if (!Directory.Exists(dir))
            return Array.Empty<VersionRecord>();

        var versions = new List<VersionRecord>();
        foreach (var file in Directory.EnumerateFiles(dir, "v*.json", SearchOption.TopDirectoryOnly)
                     .Where(x => !x.EndsWith(".manifest.json", StringComparison.OrdinalIgnoreCase)))
        {
            var version = await _json.ReadAsync<VersionRecord>(file, ct);
            if (version is not null) versions.Add(version);
        }

        return versions.OrderByDescending(x => x.Version).ToList();
    }
}

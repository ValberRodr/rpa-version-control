namespace RpaVersionControl.Core.Services;

public sealed class SharedLayout
{
    public string Root { get; }

    public SharedLayout(string root)
    {
        Root = Path.GetFullPath(root);
    }

    public string Config => Path.Combine(Root, "config");
    public string Projects => Path.Combine(Root, "projects");
    public string Changes => Path.Combine(Root, "changes");
    public string Repositories => Path.Combine(Root, "repositories");
    public string Locks => Path.Combine(Root, "locks");
    public string Audit => Path.Combine(Root, "audit");

    public string SecurityFile => Path.Combine(Config, "security.json");
    public string SequenceFile => Path.Combine(Config, "change-sequence.json");
    public string AuditChainFile => Path.Combine(Config, "audit-chain.json");
    public string AuditChainLock => Path.Combine(Locks, "audit-chain.lock");
    public string ProjectDirectory(string projectId) => Path.Combine(Projects, projectId);
    public string ProjectFile(string projectId) => Path.Combine(ProjectDirectory(projectId), "project.json");
    public string VersionsDirectory(string projectId) => Path.Combine(ProjectDirectory(projectId), "versions");
    public string VersionFile(string projectId, int version) => Path.Combine(VersionsDirectory(projectId), $"v{version}.json");
    public string VersionManifestFile(string projectId, int version) => Path.Combine(VersionsDirectory(projectId), $"v{version}.manifest.json");
    public string BareRepository(string projectId) => Path.Combine(Repositories, projectId + ".git");
    public string ChangeDirectory(string changeId) => Path.Combine(Changes, changeId);
    public string ChangeFile(string changeId) => Path.Combine(ChangeDirectory(changeId), "change.json");
    public string RevisionDirectory(string changeId, int revision) => Path.Combine(ChangeDirectory(changeId), "revisions", revision.ToString());
    public string RevisionSnapshot(string changeId, int revision) => Path.Combine(RevisionDirectory(changeId, revision), "snapshot");
    public string RevisionDiff(string changeId, int revision) => Path.Combine(RevisionDirectory(changeId, revision), "diff.patch");
    public string RevisionManifest(string changeId, int revision) => Path.Combine(RevisionDirectory(changeId, revision), "manifest.json");
    public string ChangeLock(string changeId) => Path.Combine(Locks, "changes", changeId + ".lock");
    public string ProjectLock(string projectId) => Path.Combine(Locks, "projects", projectId + ".lock");
    public string SequenceLock => Path.Combine(Locks, "sequence.lock");

    public void Ensure()
    {
        foreach (var dir in new[] { Root, Config, Projects, Changes, Repositories, Locks, Audit })
            Directory.CreateDirectory(dir);
    }
}

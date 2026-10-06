using LibGit2Sharp;
using RpaVersionControl.Core.Models;

namespace RpaVersionControl.Core.Services;

public sealed class GitVersionService
{
    private readonly FileManifestService _manifests;

    public GitVersionService(FileManifestService manifests) => _manifests = manifests;

    public async Task<(string CommitSha, string Patch, List<FileChangeSummary> Changes)> PreviewAsync(
        ProjectDefinition project,
        string candidateFolder,
        CancellationToken ct = default)
    {
        var work = NewTempDirectory();
        try
        {
            Repository.Clone(project.BareRepositoryPath, work, new CloneOptions { BranchName = "main" });

            using var repo = new Repository(work);
            await SynchronizeCandidateIntoWorkingTreeAsync(project, candidateFolder, work, ct);

            using var patch = repo.Diff.Compare<Patch>(repo.Head.Tip.Tree, DiffTargets.WorkingDirectory);
            var changes = patch.Select(x => new FileChangeSummary
            {
                Path = x.Path,
                Kind = x.Status.ToString(),
                LinesAdded = x.LinesAdded,
                LinesDeleted = x.LinesDeleted,
                IsBinary = x.IsBinaryComparison
            }).ToList();

            return (repo.Head.Tip.Sha, patch.Content, changes);
        }
        finally
        {
            DeleteDirectoryBestEffort(work);
        }
    }



    public string GetMainHeadSha(ProjectDefinition project)
    {
        using var repo = new Repository(project.BareRepositoryPath);
        var branch = repo.Branches["main"]
            ?? throw new InvalidOperationException("Branch main não encontrada no repositório.");
        return branch.Tip.Sha;
    }

    public string? FindPendingApprovedCommit(ProjectDefinition project, string changeId, int targetVersion)
    {
        using var repo = new Repository(project.BareRepositoryPath);

        var tagged = repo.Lookup<Commit>($"v{targetVersion}");
        if (tagged is not null &&
            tagged.MessageShort.StartsWith(changeId, StringComparison.OrdinalIgnoreCase))
            return tagged.Sha;

        var main = repo.Branches["main"]?.Tip;
        if (main is not null &&
            main.MessageShort.StartsWith(changeId, StringComparison.OrdinalIgnoreCase))
            return main.Sha;

        return null;
    }

    public Task<string> CompareCommitsAsync(
        ProjectDefinition project,
        string oldCommitSha,
        string newCommitSha,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var repo = new Repository(project.BareRepositoryPath);
        var oldCommit = repo.Lookup<Commit>(oldCommitSha)
            ?? throw new InvalidOperationException("Commit de origem não encontrado.");
        var newCommit = repo.Lookup<Commit>(newCommitSha)
            ?? throw new InvalidOperationException("Commit de destino não encontrado.");
        using var patch = repo.Diff.Compare<Patch>(oldCommit.Tree, newCommit.Tree);
        return Task.FromResult(patch.Content);
    }

    public async Task<string> InitializeRepositoryAsync(
        ProjectDefinition project,
        CurrentUser user,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(project.BareRepositoryPath)!);
        if (Repository.IsValid(project.BareRepositoryPath))
            throw new InvalidOperationException("O repositório deste projeto já existe.");

        Repository.Init(project.BareRepositoryPath, isBare: true);

        var work = NewTempDirectory();
        try
        {
            Repository.Init(work);
            await _manifests.CopyControlledFilesAsync(project.OfficialPath, work, project.IgnorePatterns, ct);

            using var repo = new Repository(work);
            Commands.Stage(repo, "*");

            var signature = SignatureFor(user);
            var commit = repo.Commit("Baseline inicial do projeto", signature, signature);

            if (!string.Equals(repo.Head.FriendlyName, "main", StringComparison.OrdinalIgnoreCase))
                repo.Branches.Rename(repo.Head, "main");

            repo.ApplyTag("v1");

            var remote = repo.Network.Remotes.Add("origin", project.BareRepositoryPath);
            repo.Network.Push(remote, "refs/heads/main:refs/heads/main", new PushOptions());
            repo.Network.Push(remote, "refs/tags/v1:refs/tags/v1", new PushOptions());

            return commit.Sha;
        }
        finally
        {
            DeleteDirectoryBestEffort(work);
        }
    }

    public async Task<string> CreateApprovedCommitAsync(
        ProjectDefinition project,
        string snapshotFolder,
        string changeId,
        string summary,
        int targetVersion,
        CurrentUser qa,
        CancellationToken ct = default)
    {
        var work = NewTempDirectory();
        try
        {
            Repository.Clone(project.BareRepositoryPath, work, new CloneOptions { BranchName = "main" });

            using var repo = new Repository(work);
            await SynchronizeSnapshotIntoWorkingTreeAsync(snapshotFolder, work, ct);

            Commands.Stage(repo, "*");
            using var patch = repo.Diff.Compare<Patch>(repo.Head.Tip.Tree, DiffTargets.WorkingDirectory);
            if (!patch.Any())
                throw new InvalidOperationException("A versão candidata não possui diferenças para a versão atual.");

            var signature = SignatureFor(qa);
            var commit = repo.Commit($"{changeId}: {summary}", signature, signature);
            var tag = $"v{targetVersion}";
            repo.ApplyTag(tag);

            var remote = repo.Network.Remotes["origin"];
            repo.Network.Push(remote, "refs/heads/main:refs/heads/main", new PushOptions());
            repo.Network.Push(remote, $"refs/tags/{tag}:refs/tags/{tag}", new PushOptions());

            return commit.Sha;
        }
        finally
        {
            DeleteDirectoryBestEffort(work);
        }
    }

    public async Task<string> CreateRollbackCommitAsync(
        ProjectDefinition project,
        string sourceCommitSha,
        string changeId,
        int targetVersion,
        CurrentUser qa,
        CancellationToken ct = default)
    {
        var work = NewTempDirectory();
        try
        {
            Repository.Clone(project.BareRepositoryPath, work, new CloneOptions { BranchName = "main" });
            using var repo = new Repository(work);

            var source = repo.Lookup<Commit>(sourceCommitSha)
                ?? throw new InvalidOperationException("Versão de origem do rollback não encontrada.");

            var restore = NewTempDirectory();
            try
            {
                await ExportTreeAsync(source.Tree, restore, ct);
                await SynchronizeSnapshotIntoWorkingTreeAsync(restore, work, ct);
                Commands.Stage(repo, "*");

                var signature = SignatureFor(qa);
                var commit = repo.Commit($"{changeId}: rollback para versão anterior", signature, signature);
                var tag = $"v{targetVersion}";
                repo.ApplyTag(tag);

                var remote = repo.Network.Remotes["origin"];
                repo.Network.Push(remote, "refs/heads/main:refs/heads/main", new PushOptions());
                repo.Network.Push(remote, $"refs/tags/{tag}:refs/tags/{tag}", new PushOptions());
                return commit.Sha;
            }
            finally
            {
                DeleteDirectoryBestEffort(restore);
            }
        }
        finally
        {
            DeleteDirectoryBestEffort(work);
        }
    }

    public async Task ExportCommitAsync(
        ProjectDefinition project,
        string commitSha,
        string destination,
        CancellationToken ct = default)
    {
        var work = NewTempDirectory();
        try
        {
            Repository.Clone(project.BareRepositoryPath, work, new CloneOptions { BranchName = "main" });
            using var repo = new Repository(work);
            var commit = repo.Lookup<Commit>(commitSha)
                ?? throw new InvalidOperationException("Commit não encontrado.");
            Directory.CreateDirectory(destination);
            await ExportTreeAsync(commit.Tree, destination, ct);
        }
        finally
        {
            DeleteDirectoryBestEffort(work);
        }
    }

    private async Task SynchronizeCandidateIntoWorkingTreeAsync(
        ProjectDefinition project,
        string candidateFolder,
        string work,
        CancellationToken ct)
    {
        var candidateManifest = await _manifests.BuildAsync(candidateFolder, project.IgnorePatterns, ct);
        var wanted = candidateManifest.Files.Select(x => x.RelativePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        using (var repo = new Repository(work))
        {
            foreach (var entry in repo.Index)
            {
                var relative = entry.Path.Replace('\\', '/');
                if (wanted.Contains(relative))
                    continue;

                var path = Path.Combine(work, relative.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        await _manifests.CopyControlledFilesAsync(candidateFolder, work, project.IgnorePatterns, ct);
    }

    private static async Task SynchronizeSnapshotIntoWorkingTreeAsync(
        string snapshotFolder,
        string work,
        CancellationToken ct)
    {
        var wanted = Directory.EnumerateFiles(snapshotFolder, "*", SearchOption.AllDirectories)
            .Select(x => Path.GetRelativePath(snapshotFolder, x).Replace('\\', '/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        using (var repo = new Repository(work))
        {
            foreach (var entry in repo.Index)
            {
                ct.ThrowIfCancellationRequested();
                var relative = entry.Path.Replace('\\', '/');
                if (wanted.Contains(relative))
                    continue;

                var path = Path.Combine(work, relative.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        foreach (var relative in wanted)
        {
            ct.ThrowIfCancellationRequested();
            var source = Path.Combine(snapshotFolder, relative.Replace('/', Path.DirectorySeparatorChar));
            var target = Path.Combine(work, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, overwrite: true);
        }

        await Task.CompletedTask;
    }

    private static async Task ExportTreeAsync(Tree tree, string destination, CancellationToken ct)
    {
        foreach (var entry in tree)
        {
            ct.ThrowIfCancellationRequested();
            var target = Path.Combine(destination, entry.Name);

            if (entry.TargetType == TreeEntryTargetType.Tree)
            {
                Directory.CreateDirectory(target);
                await ExportTreeAsync((Tree)entry.Target, target, ct);
            }
            else if (entry.TargetType == TreeEntryTargetType.Blob)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                var blob = (Blob)entry.Target;
                await using var output = File.Create(target);
                using var content = blob.GetContentStream();
                await content.CopyToAsync(output, ct);
            }
        }
    }

    private static Signature SignatureFor(CurrentUser user) =>
        new(user.DisplayName, $"{SanitizeEmailName(user.WindowsUser)}@local.invalid", DateTimeOffset.Now);

    private static string SanitizeEmailName(string user) =>
        string.Concat(user.Select(ch => char.IsLetterOrDigit(ch) || ch is '.' or '-' ? ch : '.')).Trim('.');

    private static string NewTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "RpaVersionControl", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectoryBestEffort(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch { }
    }
}

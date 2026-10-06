using System.Security.AccessControl;
using System.Security.Principal;
using RpaVersionControl.Core.Models;

namespace RpaVersionControl.Core.Services;

public sealed class VersioningWorkflowService
{
    private readonly AtomicJsonStore _json;
    private readonly ProjectStore _projects;
    private readonly ChangeStore _changes;
    private readonly AuditStore _audit;
    private readonly NetworkLockService _locks;
    private readonly FileManifestService _manifests;
    private readonly GitVersionService _git;

    public VersioningWorkflowService(
        AtomicJsonStore json,
        ProjectStore projects,
        ChangeStore changes,
        AuditStore audit,
        NetworkLockService locks,
        FileManifestService manifests,
        GitVersionService git)
    {
        _json = json;
        _projects = projects;
        _changes = changes;
        _audit = audit;
        _locks = locks;
        _manifests = manifests;
        _git = git;
    }

    public async Task<ProjectDefinition> CreateProjectAsync(
        SharedLayout layout,
        string name,
        string officialPath,
        IEnumerable<string> ignorePatterns,
        CurrentUser user,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Informe o nome do projeto.");
        if (!Directory.Exists(officialPath))
            throw new DirectoryNotFoundException(officialPath);

        var existingProjects = await _projects.ListAsync(layout, ct);
        if (existingProjects.Any(x => string.Equals(
                Path.GetFullPath(x.OfficialPath).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(officialPath).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Esta pasta oficial já está cadastrada em outro projeto.");

        var project = new ProjectDefinition
        {
            Name = name.Trim(),
            OfficialPath = Path.GetFullPath(officialPath),
            IgnorePatterns = IgnoreMatcher.DefaultPatterns
                .Concat(ignorePatterns)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            CreatedBy = user.WindowsUser
        };
        project.BareRepositoryPath = layout.BareRepository(project.Id);

        await using var projectLock = await _locks.AcquireAsync(layout.ProjectLock(project.Id), user.WindowsUser, ct: ct);

        var baselineManifest = await _manifests.BuildAsync(project.OfficialPath, project.IgnorePatterns, ct);
        if (baselineManifest.Files.Count == 0)
            throw new InvalidOperationException(
                "Nenhum arquivo elegível para versionamento foi encontrado após aplicar as regras de exclusão.");

        var commitSha = await _git.InitializeRepositoryAsync(project, user, ct);

        project.CurrentVersion = 1;
        project.CurrentCommitSha = commitSha;
        project.CurrentManifestHash = baselineManifest.AggregateHash;

        var manifestPath = layout.VersionManifestFile(project.Id, 1);
        await _json.WriteAsync(manifestPath, baselineManifest, ct);

        var version = new VersionRecord
        {
            ProjectId = project.Id,
            Version = 1,
            CommitSha = commitSha,
            ChangeId = "BASELINE",
            DeveloperWindowsUser = user.WindowsUser,
            DeveloperDisplayName = user.DisplayName,
            QaWindowsUser = user.WindowsUser,
            QaDisplayName = user.DisplayName,
            ManifestHash = baselineManifest.AggregateHash,
            ManifestRelativePath = Path.GetRelativePath(layout.Root, manifestPath),
            Summary = "Baseline inicial",
            PublishedAtUtc = DateTimeOffset.UtcNow
        };

        await _json.WriteAsync(layout.VersionFile(project.Id, 1), version, ct);
        await _projects.SaveAsync(layout, project, ct);

        await _audit.AppendAsync(layout, new AuditEvent
        {
            ActorWindowsUser = user.WindowsUser,
            ActorDisplayName = user.DisplayName,
            Action = "ProjectCreated",
            EntityType = "Project",
            EntityId = project.Id,
            ProjectId = project.Id,
            TargetRole = "QA",
            Message = $"{project.Name} cadastrado na versão v1."
        }, ct);

        return project;
    }

    public async Task<SubmissionPreview> PreviewSubmissionAsync(
        SharedLayout layout,
        string projectId,
        string candidateFolder,
        CancellationToken ct = default)
    {
        var project = await _projects.GetAsync(layout, projectId, ct)
            ?? throw new InvalidOperationException("Projeto não encontrado.");

        var currentSha = project.CurrentCommitSha;
        var candidateManifest = await _manifests.BuildAsync(candidateFolder, project.IgnorePatterns, ct);
        var preview = await _git.PreviewAsync(project, candidateFolder, ct);

        if (!string.Equals(currentSha, preview.CommitSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("A versão oficial mudou durante a comparação. Atualize e compare novamente.");

        if (preview.Changes.Count == 0)
            throw new InvalidOperationException("Nenhuma alteração elegível foi encontrada em relação à versão oficial.");

        return new SubmissionPreview(
            project,
            candidateFolder,
            preview.CommitSha,
            candidateManifest,
            preview.Patch,
            preview.Changes);
    }

    public async Task<ChangeRequest> SubmitAsync(
        SharedLayout layout,
        SubmissionPreview preview,
        string incidentReference,
        string reason,
        string changeSummary,
        string expectedImpact,
        CurrentUser user,
        string? existingChangeId = null,
        CancellationToken ct = default)
    {
        ValidateDeveloperComments(incidentReference, reason, changeSummary, expectedImpact);

        var project = await _projects.GetAsync(layout, preview.Project.Id, ct)
            ?? throw new InvalidOperationException("Projeto não encontrado.");

        if (!string.Equals(project.CurrentCommitSha, preview.BaseCommitSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("A versão oficial mudou desde a prévia. Gere uma nova comparação antes de submeter.");

        var change = existingChangeId is null
            ? new ChangeRequest
            {
                Id = await _changes.NextIdAsync(layout, user.WindowsUser, ct),
                ProjectId = project.Id,
                ProjectName = project.Name,
                DeveloperWindowsUser = user.WindowsUser,
                DeveloperDisplayName = user.DisplayName,
                IncidentReference = incidentReference.Trim(),
                Reason = reason.Trim(),
                ChangeSummary = changeSummary.Trim(),
                ExpectedImpact = expectedImpact.Trim(),
                CreatedAtUtc = DateTimeOffset.UtcNow
            }
            : await _changes.GetAsync(layout, existingChangeId, ct)
              ?? throw new InvalidOperationException("Alteração original não encontrada.");

        if (!string.Equals(change.DeveloperWindowsUser, user.WindowsUser, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Apenas o desenvolvedor que criou a alteração pode reenviá-la.");

        if (existingChangeId is not null &&
            change.Status is not (ChangeStatus.AdjustmentsRequested or ChangeStatus.NeedsRebase or ChangeStatus.RetrofitRequired))
            throw new InvalidOperationException("Esta alteração não está disponível para reenvio.");

        await using var changeLock = await _locks.AcquireAsync(layout.ChangeLock(change.Id), user.WindowsUser, ct: ct);

        var revisionNumber = change.CurrentRevisionNumber + 1;
        var snapshot = layout.RevisionSnapshot(change.Id, revisionNumber);
        Directory.CreateDirectory(snapshot);

        await _manifests.CopyControlledFilesAsync(preview.CandidateFolder, snapshot, project.IgnorePatterns, ct);

        var diffFile = layout.RevisionDiff(change.Id, revisionNumber);
        Directory.CreateDirectory(Path.GetDirectoryName(diffFile)!);
        await File.WriteAllTextAsync(diffFile, preview.Patch, ct);

        var manifestFile = layout.RevisionManifest(change.Id, revisionNumber);
        await _json.WriteAsync(manifestFile, preview.CandidateManifest, ct);

        var revision = new ChangeRevision
        {
            RevisionNumber = revisionNumber,
            SubmittedAtUtc = DateTimeOffset.UtcNow,
            BaseVersion = project.CurrentVersion,
            BaseCommitSha = project.CurrentCommitSha,
            SnapshotRelativePath = Path.GetRelativePath(layout.Root, snapshot),
            DiffRelativePath = Path.GetRelativePath(layout.Root, diffFile),
            CandidateManifestRelativePath = Path.GetRelativePath(layout.Root, manifestFile),
            CandidateManifestHash = preview.CandidateManifest.AggregateHash,
            ChangedFiles = preview.Changes.ToList()
        };

        change.CurrentRevisionNumber = revisionNumber;
        change.Revisions.Add(revision);
        change.Status = ChangeStatus.Submitted;
        change.IncidentReference = incidentReference.Trim();
        change.Reason = reason.Trim();
        change.ChangeSummary = changeSummary.Trim();
        change.ExpectedImpact = expectedImpact.Trim();

        await _changes.SaveAsync(layout, change, ct);

        await _audit.AppendAsync(layout, new AuditEvent
        {
            ActorWindowsUser = user.WindowsUser,
            ActorDisplayName = user.DisplayName,
            Action = revisionNumber == 1 ? "ChangeSubmitted" : "ChangeResubmitted",
            EntityType = "Change",
            EntityId = change.Id,
            ProjectId = project.Id,
            TargetRole = "QA",
            Message = $"{change.Id} • {project.Name} • revisão {revisionNumber} aguardando QA."
        }, ct);

        return change;
    }

    public async Task<ChangeRequest> ReviewAsync(
        SharedLayout layout,
        string changeId,
        IReadOnlyList<QaChecklistAnswer> answers,
        bool approve,
        CurrentUser qa,
        CancellationToken ct = default)
    {
        if (answers.Count != QaChecklistQuestions.All.Count)
            throw new InvalidOperationException("Responda as 6 perguntas do checklist.");

        foreach (var question in QaChecklistQuestions.All)
        {
            var answer = answers.SingleOrDefault(x => x.QuestionId == question.Id)
                ?? throw new InvalidOperationException($"Pergunta {question.Id} não respondida.");

            if (!answer.IsCompliant && string.IsNullOrWhiteSpace(answer.Comment))
                throw new InvalidOperationException($"Informe o comentário da pergunta {question.Id}.");
        }

        await using var changeLock = await _locks.AcquireAsync(layout.ChangeLock(changeId), qa.WindowsUser, ct: ct);

        var change = await _changes.GetAsync(layout, changeId, ct)
            ?? throw new InvalidOperationException("Alteração não encontrada.");
        var revision = change.CurrentRevision
            ?? throw new InvalidOperationException("Revisão não encontrada.");

        if (change.Status is not (ChangeStatus.Submitted or ChangeStatus.InReview))
            throw new InvalidOperationException("Esta alteração não está aguardando revisão.");

        var checklist = new QaChecklist
        {
            ReviewerWindowsUser = qa.WindowsUser,
            ReviewerDisplayName = qa.DisplayName,
            ReviewedAtUtc = DateTimeOffset.UtcNow,
            Answers = answers.ToList()
        };
        revision.Checklist = checklist;
        change.QaWindowsUser = qa.WindowsUser;
        change.QaDisplayName = qa.DisplayName;

        if (approve)
        {
            if (!checklist.AllCompliant)
                throw new InvalidOperationException("Uma ou mais respostas estão como NÃO. A aprovação está bloqueada.");

            var project = await _projects.GetAsync(layout, change.ProjectId, ct)
                ?? throw new InvalidOperationException("Projeto não encontrado.");

            if (!string.Equals(project.CurrentCommitSha, revision.BaseCommitSha, StringComparison.OrdinalIgnoreCase))
            {
                change.Status = ChangeStatus.NeedsRebase;
                await _changes.SaveAsync(layout, change, ct);
                await NotifyDeveloperAsync(layout, change, qa, "NeedsRebase",
                    $"{change.Id}: a versão oficial mudou. Recompare e reenvie a alteração.", ct);
                return change;
            }

            await _changes.SaveAsync(layout, change, ct);
            return await ApproveAndPublishLockedAsync(layout, change, qa, ct);
        }

        var retrofitAnswer = answers.Single(x => x.QuestionId == 6);
        change.Status = !retrofitAnswer.IsCompliant
            ? ChangeStatus.RetrofitRequired
            : ChangeStatus.AdjustmentsRequested;

        await _changes.SaveAsync(layout, change, ct);

        var msg = change.Status == ChangeStatus.RetrofitRequired
            ? $"{change.Id}: QA classificou a mudança como possível retrofit. Encaminhar pela esteira de projetos."
            : $"{change.Id}: QA solicitou ajustes na revisão {revision.RevisionNumber}.";

        await NotifyDeveloperAsync(layout, change, qa,
            change.Status == ChangeStatus.RetrofitRequired ? "RetrofitRequired" : "AdjustmentsRequested",
            msg, ct);

        return change;
    }

    public async Task<VersionRecord> RetryPublishAsync(
        SharedLayout layout,
        string changeId,
        CurrentUser qa,
        CancellationToken ct = default)
    {
        await using var changeLock = await _locks.AcquireAsync(layout.ChangeLock(changeId), qa.WindowsUser, ct: ct);
        var change = await _changes.GetAsync(layout, changeId, ct)
            ?? throw new InvalidOperationException("Alteração não encontrada.");

        if (change.Status is not (ChangeStatus.PublishFailed or ChangeStatus.ApprovedPendingPublish))
            throw new InvalidOperationException("A alteração não está pendente de publicação.");

        if (string.IsNullOrWhiteSpace(change.ApprovedCommitSha))
        {
            var project = await _projects.GetAsync(layout, change.ProjectId, ct)
                ?? throw new InvalidOperationException("Projeto não encontrado.");
            var targetVersion = project.CurrentVersion + 1;
            var pending = _git.FindPendingApprovedCommit(project, change.Id, targetVersion);

            if (!string.IsNullOrWhiteSpace(pending))
            {
                change.ApprovedCommitSha = pending;
                change.PublishedVersion = targetVersion;
                await _changes.SaveAsync(layout, change, ct);
                return await PublishExistingApprovedCommitLockedAsync(layout, change, qa, ct);
            }

            var resumed = await ApproveAndPublishLockedAsync(layout, change, qa, ct);
            if (resumed.Status != ChangeStatus.Published || resumed.PublishedVersion is null)
                throw new InvalidOperationException("A publicação não pôde ser retomada.");

            return await _json.ReadAsync<VersionRecord>(
                layout.VersionFile(change.ProjectId, resumed.PublishedVersion.Value), ct)
                ?? throw new InvalidOperationException("Registro da versão publicada não encontrado.");
        }

        return await PublishExistingApprovedCommitLockedAsync(layout, change, qa, ct);
    }

    public async Task<VersionRecord> RollbackAsync(
        SharedLayout layout,
        string projectId,
        int sourceVersion,
        string reason,
        CurrentUser qa,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Informe o motivo do rollback.");

        await using var projectLock = await _locks.AcquireAsync(layout.ProjectLock(projectId), qa.WindowsUser, ct: ct);

        var project = await _projects.GetAsync(layout, projectId, ct)
            ?? throw new InvalidOperationException("Projeto não encontrado.");
        var source = await _json.ReadAsync<VersionRecord>(layout.VersionFile(projectId, sourceVersion), ct)
            ?? throw new InvalidOperationException("Versão de origem não encontrada.");

        if (sourceVersion == project.CurrentVersion)
            throw new InvalidOperationException("A versão selecionada já é a versão atual.");

        await EnsureNoProductionDriftAsync(layout, project, ct);

        var changeId = await _changes.NextIdAsync(layout, qa.WindowsUser, ct);
        var targetVersion = project.CurrentVersion + 1;
        var revisionNumber = 1;
        var snapshot = layout.RevisionSnapshot(changeId, revisionNumber);
        Directory.CreateDirectory(snapshot);

        await _git.ExportCommitAsync(project, source.CommitSha, snapshot, ct);
        var candidateManifest = await _manifests.BuildAsync(snapshot, Array.Empty<string>(), ct);
        await _json.WriteAsync(layout.RevisionManifest(changeId, revisionNumber), candidateManifest, ct);

        var change = new ChangeRequest
        {
            Id = changeId,
            ProjectId = project.Id,
            ProjectName = project.Name,
            DeveloperWindowsUser = qa.WindowsUser,
            DeveloperDisplayName = qa.DisplayName,
            IncidentReference = "ROLLBACK",
            Reason = reason.Trim(),
            ChangeSummary = $"Rollback para v{sourceVersion}",
            ExpectedImpact = $"Restaurar o conteúdo aprovado da v{sourceVersion}.",
            Status = ChangeStatus.ApprovedPendingPublish,
            CurrentRevisionNumber = 1,
            QaWindowsUser = qa.WindowsUser,
            QaDisplayName = qa.DisplayName,
            PublishedVersion = targetVersion,
            RollbackFromVersion = sourceVersion,
            Revisions = new List<ChangeRevision>
            {
                new()
                {
                    RevisionNumber = 1,
                    SubmittedAtUtc = DateTimeOffset.UtcNow,
                    BaseVersion = project.CurrentVersion,
                    BaseCommitSha = project.CurrentCommitSha,
                    SnapshotRelativePath = Path.GetRelativePath(layout.Root, snapshot),
                    DiffRelativePath = string.Empty,
                    CandidateManifestRelativePath = Path.GetRelativePath(layout.Root, layout.RevisionManifest(changeId, revisionNumber)),
                    CandidateManifestHash = candidateManifest.AggregateHash
                }
            }
        };

        await _changes.SaveAsync(layout, change, ct);

        try
        {
            var commit = await _git.CreateApprovedCommitAsync(
                project, snapshot, changeId, change.ChangeSummary, targetVersion, qa, ct);

            change.ApprovedCommitSha = commit;
            await _changes.SaveAsync(layout, change, ct);

            var record = await PublishSnapshotAsync(
                layout, project, change, targetVersion, commit, snapshot, qa, ct);

            change.Status = ChangeStatus.Published;
            await _changes.SaveAsync(layout, change, ct);

            await _audit.AppendAsync(layout, new AuditEvent
            {
                ActorWindowsUser = qa.WindowsUser,
                ActorDisplayName = qa.DisplayName,
                Action = "RollbackPublished",
                EntityType = "Version",
                EntityId = $"v{targetVersion}",
                ProjectId = project.Id,
                TargetRole = "QA",
                Message = $"{project.Name}: rollback v{sourceVersion} publicado como v{targetVersion}."
            }, ct);

            return record;
        }
        catch
        {
            change.Status = ChangeStatus.PublishFailed;
            await _changes.SaveAsync(layout, change, ct);
            throw;
        }
    }

    /// <summary>
    /// Compares the project's production folder against the last version approved by QA and
    /// reports every file that differs — i.e. every file someone edited directly in production
    /// instead of going through submit/review. Read-only; does not block or change anything.
    /// </summary>
    public async Task<DriftReport> BuildDriftReportAsync(
        SharedLayout layout,
        string projectId,
        CancellationToken ct = default)
    {
        var project = await _projects.GetAsync(layout, projectId, ct)
            ?? throw new InvalidOperationException("Projeto não encontrado.");

        return await BuildDriftReportCoreAsync(project, ct);
    }

    private async Task<DriftReport> BuildDriftReportCoreAsync(ProjectDefinition project, CancellationToken ct)
    {
        var preview = await _git.PreviewAsync(project, project.OfficialPath, ct);

        var files = preview.Changes.Select(change =>
        {
            var fullPath = Path.Combine(project.OfficialPath, change.Path.Replace('/', Path.DirectorySeparatorChar));
            return new DriftedFileInfo
            {
                Path = change.Path,
                Kind = change.Kind,
                IsBinary = change.IsBinary,
                SuspectedEditor = TryGetFileOwner(fullPath),
                LastWriteTimeUtc = TryGetLastWriteTimeUtc(fullPath)
            };
        }).ToList();

        return new DriftReport
        {
            ProjectId = project.Id,
            ProjectName = project.Name,
            ApprovedVersion = project.CurrentVersion,
            ApprovedCommitSha = project.CurrentCommitSha,
            HasDrift = files.Count > 0,
            Files = files,
            Patch = preview.Patch
        };
    }

    /// <summary>
    /// Forcibly re-syncs the production folder back to the content of the last version
    /// approved by QA, overwriting any unauthorized edit made directly in production. This is
    /// a corrective action, not a publish: it does not create a new version, since the result
    /// is, by definition, identical to the version already on record as current.
    /// </summary>
    public async Task<DriftReport> RestoreProductionToApprovedAsync(
        SharedLayout layout,
        string projectId,
        string reason,
        CurrentUser qa,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Informe o motivo da restauração.");

        await using var projectLock = await _locks.AcquireAsync(layout.ProjectLock(projectId), qa.WindowsUser, ct: ct);

        var project = await _projects.GetAsync(layout, projectId, ct)
            ?? throw new InvalidOperationException("Projeto não encontrado.");

        var report = await BuildDriftReportCoreAsync(project, ct);
        if (!report.HasDrift)
            throw new InvalidOperationException("A produção já corresponde à versão aprovada; não há o que restaurar.");

        var approvedManifest = await _json.ReadAsync<ManifestDocument>(
            layout.VersionManifestFile(project.Id, project.CurrentVersion), ct)
            ?? throw new InvalidOperationException("Manifesto da versão aprovada não encontrado.");

        var snapshot = Path.Combine(Path.GetTempPath(), "RpaVersionControl", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(snapshot);
        try
        {
            await _git.ExportCommitAsync(project, project.CurrentCommitSha, snapshot, ct);
            await _manifests.ReplaceControlledTreeAsync(
                snapshot, project.OfficialPath, approvedManifest.Files.Select(x => x.RelativePath), ct);

            var verification = await _manifests.BuildAsync(project.OfficialPath, project.IgnorePatterns, ct);
            if (!FileManifestService.Equivalent(verification, approvedManifest))
                throw new IOException("A verificação pós-restauração falhou. A produção não corresponde à versão aprovada.");
        }
        finally
        {
            try { Directory.Delete(snapshot, recursive: true); } catch { }
        }

        var fileList = string.Join("; ", report.Files.Select(f =>
            $"{f.Path} [{DriftReport.IntegrityCompromisedStatus}, editor suspeito: {f.SuspectedEditor ?? "desconhecido"}]"));

        await _audit.AppendAsync(layout, new AuditEvent
        {
            ActorWindowsUser = qa.WindowsUser,
            ActorDisplayName = qa.DisplayName,
            Action = "UnauthorizedProductionEditOverwritten",
            EntityType = "Project",
            EntityId = project.Id,
            ProjectId = project.Id,
            TargetRole = "QA",
            Message = $"{project.Name}: produção restaurada para v{project.CurrentVersion} por {qa.DisplayName}, " +
                      $"sobrescrevendo edição(ões) não autorizada(s). Motivo: {reason.Trim()}. Arquivo(s): {fileList}."
        }, ct);

        return report;
    }

    private static string? TryGetFileOwner(string path)
    {
        if (!OperatingSystem.IsWindows())
            return null;

        try
        {
            if (!File.Exists(path)) return null;
            var security = new FileInfo(path).GetAccessControl();
            return security.GetOwner(typeof(NTAccount))?.Value;
        }
        catch
        {
            return null;
        }
    }

    private static DateTimeOffset? TryGetLastWriteTimeUtc(string path)
    {
        try
        {
            return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : null;
        }
        catch
        {
            return null;
        }
    }

    private async Task<ChangeRequest> ApproveAndPublishLockedAsync(
        SharedLayout layout,
        ChangeRequest change,
        CurrentUser qa,
        CancellationToken ct)
    {
        await using var projectLock = await _locks.AcquireAsync(layout.ProjectLock(change.ProjectId), qa.WindowsUser, ct: ct);

        var project = await _projects.GetAsync(layout, change.ProjectId, ct)
            ?? throw new InvalidOperationException("Projeto não encontrado.");
        var revision = change.CurrentRevision!;

        var repositoryHead = _git.GetMainHeadSha(project);
        if (!string.Equals(repositoryHead, project.CurrentCommitSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "O repositório possui uma versão pendente de recuperação/publicação. " +
                "Conclua a publicação pendente antes de aprovar outra alteração.");

        if (!string.Equals(project.CurrentCommitSha, revision.BaseCommitSha, StringComparison.OrdinalIgnoreCase))
        {
            change.Status = ChangeStatus.NeedsRebase;
            await _changes.SaveAsync(layout, change, ct);
            return change;
        }

        await EnsureNoProductionDriftAsync(layout, project, ct);

        var snapshot = Path.Combine(layout.Root, revision.SnapshotRelativePath);
        var targetVersion = project.CurrentVersion + 1;

        change.Status = ChangeStatus.ApprovedPendingPublish;
        await _changes.SaveAsync(layout, change, ct);

        try
        {
            var commit = await _git.CreateApprovedCommitAsync(
                project, snapshot, change.Id, change.ChangeSummary, targetVersion, qa, ct);

            change.ApprovedCommitSha = commit;
            change.PublishedVersion = targetVersion;
            await _changes.SaveAsync(layout, change, ct);

            await PublishSnapshotAsync(layout, project, change, targetVersion, commit, snapshot, qa, ct);
            change.Status = ChangeStatus.Published;
            await _changes.SaveAsync(layout, change, ct);

            await _audit.AppendAsync(layout, new AuditEvent
            {
                ActorWindowsUser = qa.WindowsUser,
                ActorDisplayName = qa.DisplayName,
                Action = "ChangePublished",
                EntityType = "Change",
                EntityId = change.Id,
                ProjectId = project.Id,
                TargetWindowsUser = change.DeveloperWindowsUser,
                Message = $"{change.Id} aprovado por {qa.DisplayName} e publicado como v{targetVersion}."
            }, ct);

            return change;
        }
        catch
        {
            change.Status = ChangeStatus.PublishFailed;
            await _changes.SaveAsync(layout, change, ct);
            throw;
        }
    }

    private async Task<VersionRecord> PublishExistingApprovedCommitLockedAsync(
        SharedLayout layout,
        ChangeRequest change,
        CurrentUser qa,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(change.ApprovedCommitSha) || change.PublishedVersion is null)
            throw new InvalidOperationException("Não existe commit aprovado para republicação.");

        await using var projectLock = await _locks.AcquireAsync(layout.ProjectLock(change.ProjectId), qa.WindowsUser, ct: ct);
        var project = await _projects.GetAsync(layout, change.ProjectId, ct)
            ?? throw new InvalidOperationException("Projeto não encontrado.");

        var revision = change.CurrentRevision!;
        var snapshot = Path.Combine(layout.Root, revision.SnapshotRelativePath);

        var repositoryHead = _git.GetMainHeadSha(project);
        if (!string.Equals(repositoryHead, change.ApprovedCommitSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "O HEAD do repositório não corresponde ao commit aprovado desta publicação. Operação bloqueada.");

        if (change.PublishedVersion != project.CurrentVersion + 1)
            throw new InvalidOperationException(
                "A sequência de versões mudou desde a aprovação. A publicação foi bloqueada para evitar sobrescrita.");

        var record = await PublishSnapshotAsync(
            layout, project, change, change.PublishedVersion.Value,
            change.ApprovedCommitSha, snapshot, qa, ct);

        change.Status = ChangeStatus.Published;
        await _changes.SaveAsync(layout, change, ct);
        return record;
    }

    private async Task<VersionRecord> PublishSnapshotAsync(
        SharedLayout layout,
        ProjectDefinition project,
        ChangeRequest change,
        int targetVersion,
        string commitSha,
        string snapshot,
        CurrentUser qa,
        CancellationToken ct)
    {
        var expectedManifest = await _json.ReadAsync<ManifestDocument>(
            layout.RevisionManifest(change.Id, change.CurrentRevisionNumber), ct)
            ?? throw new InvalidOperationException("Manifesto da candidata não encontrado.");

        var currentProduction = await _manifests.BuildAsync(project.OfficialPath, project.IgnorePatterns, ct);
        if (!FileManifestService.Equivalent(currentProduction, expectedManifest))
        {
            await EnsureNoProductionDriftAsync(layout, project, ct);

            var previousManifest = await _json.ReadAsync<ManifestDocument>(
                layout.VersionManifestFile(project.Id, project.CurrentVersion), ct)
                ?? throw new InvalidOperationException("Manifesto da versão atual não encontrado.");

            await _manifests.ReplaceControlledTreeAsync(
                snapshot, project.OfficialPath,
                previousManifest.Files.Select(x => x.RelativePath), ct);

            currentProduction = await _manifests.BuildAsync(project.OfficialPath, project.IgnorePatterns, ct);
            if (!FileManifestService.Equivalent(currentProduction, expectedManifest))
                throw new IOException("A verificação pós-publicação falhou. A produção não corresponde à versão aprovada.");
        }

        var manifestPath = layout.VersionManifestFile(project.Id, targetVersion);
        await _json.WriteAsync(manifestPath, currentProduction, ct);

        var record = new VersionRecord
        {
            ProjectId = project.Id,
            Version = targetVersion,
            CommitSha = commitSha,
            ChangeId = change.Id,
            DeveloperWindowsUser = change.DeveloperWindowsUser,
            DeveloperDisplayName = change.DeveloperDisplayName,
            QaWindowsUser = qa.WindowsUser,
            QaDisplayName = qa.DisplayName,
            ManifestHash = currentProduction.AggregateHash,
            ManifestRelativePath = Path.GetRelativePath(layout.Root, manifestPath),
            Summary = change.ChangeSummary,
            PublishedAtUtc = DateTimeOffset.UtcNow,
            RestoredFromVersion = change.RollbackFromVersion
        };

        await _json.WriteAsync(layout.VersionFile(project.Id, targetVersion), record, ct);

        project.CurrentVersion = targetVersion;
        project.CurrentCommitSha = commitSha;
        project.CurrentManifestHash = currentProduction.AggregateHash;
        await _projects.SaveAsync(layout, project, ct);

        return record;
    }

    private async Task EnsureNoProductionDriftAsync(
        SharedLayout layout,
        ProjectDefinition project,
        CancellationToken ct)
    {
        var approved = await _json.ReadAsync<ManifestDocument>(
            layout.VersionManifestFile(project.Id, project.CurrentVersion), ct)
            ?? throw new InvalidOperationException("Manifesto da versão oficial não encontrado.");

        var production = await _manifests.BuildAsync(project.OfficialPath, project.IgnorePatterns, ct);

        if (!FileManifestService.Equivalent(approved, production))
            throw new InvalidOperationException(
                $"Integridade {DriftReport.IntegrityCompromisedStatus.ToLowerInvariant()}: arquivos controlados em produção " +
                "foram alterados fora do fluxo de aprovação do aplicativo. A publicação foi bloqueada para evitar " +
                "sobrescrita silenciosa. Gere o relatório de integridade do projeto para identificar os arquivos " +
                "afetados e, se confirmado que a edição não foi autorizada, use a opção de restaurar a produção " +
                "para a versão aprovada.");
    }

    private async Task NotifyDeveloperAsync(
        SharedLayout layout,
        ChangeRequest change,
        CurrentUser qa,
        string action,
        string message,
        CancellationToken ct)
    {
        await _audit.AppendAsync(layout, new AuditEvent
        {
            ActorWindowsUser = qa.WindowsUser,
            ActorDisplayName = qa.DisplayName,
            Action = action,
            EntityType = "Change",
            EntityId = change.Id,
            ProjectId = change.ProjectId,
            TargetWindowsUser = change.DeveloperWindowsUser,
            Message = message
        }, ct);
    }

    private static void ValidateDeveloperComments(
        string incidentReference,
        string reason,
        string changeSummary,
        string expectedImpact)
    {
        if (string.IsNullOrWhiteSpace(incidentReference))
            throw new ArgumentException("Informe o incidente ou solicitação relacionada.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Informe o motivo da alteração.");
        if (string.IsNullOrWhiteSpace(changeSummary))
            throw new ArgumentException("Descreva o que foi alterado.");
        if (string.IsNullOrWhiteSpace(expectedImpact))
            throw new ArgumentException("Informe o impacto esperado.");
    }
}

public sealed record SubmissionPreview(
    ProjectDefinition Project,
    string CandidateFolder,
    string BaseCommitSha,
    ManifestDocument CandidateManifest,
    string Patch,
    IReadOnlyList<FileChangeSummary> Changes);

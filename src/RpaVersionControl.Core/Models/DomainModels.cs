using System.Text.Json.Serialization;

namespace RpaVersionControl.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ChangeStatus
{
    Draft,
    Submitted,
    InReview,
    AdjustmentsRequested,
    RetrofitRequired,
    NeedsRebase,
    ApprovedPendingPublish,
    PublishFailed,
    Published,
    Rejected,
    Cancelled
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AppRole
{
    Developer,
    Qa,
    Admin
}

public sealed record CurrentUser(
    string WindowsUser,
    string DisplayName,
    string MachineName);

public sealed class ProjectDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string OfficialPath { get; set; } = string.Empty;
    public string BareRepositoryPath { get; set; } = string.Empty;
    public List<string> IgnorePatterns { get; set; } = new();
    public int CurrentVersion { get; set; }
    public string CurrentCommitSha { get; set; } = string.Empty;
    public string CurrentManifestHash { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ChangeRequest
{
    public string Id { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public string DeveloperWindowsUser { get; set; } = string.Empty;
    public string DeveloperDisplayName { get; set; } = string.Empty;
    public string IncidentReference { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string ChangeSummary { get; set; } = string.Empty;
    public string ExpectedImpact { get; set; } = string.Empty;
    public ChangeStatus Status { get; set; } = ChangeStatus.Draft;
    public int CurrentRevisionNumber { get; set; }
    public List<ChangeRevision> Revisions { get; set; } = new();
    public string? QaWindowsUser { get; set; }
    public string? QaDisplayName { get; set; }
    public string? ApprovedCommitSha { get; set; }
    public int? PublishedVersion { get; set; }
    public int? RollbackFromVersion { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    [JsonIgnore]
    public ChangeRevision? CurrentRevision =>
        Revisions.OrderByDescending(x => x.RevisionNumber).FirstOrDefault();
}

public sealed class ChangeRevision
{
    public int RevisionNumber { get; set; }
    public DateTimeOffset SubmittedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public int BaseVersion { get; set; }
    public string BaseCommitSha { get; set; } = string.Empty;
    public string SnapshotRelativePath { get; set; } = string.Empty;
    public string DiffRelativePath { get; set; } = string.Empty;
    public string CandidateManifestRelativePath { get; set; } = string.Empty;
    public string CandidateManifestHash { get; set; } = string.Empty;
    public List<FileChangeSummary> ChangedFiles { get; set; } = new();
    public QaChecklist? Checklist { get; set; }
}

public sealed class FileChangeSummary
{
    public string Path { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public int LinesAdded { get; set; }
    public int LinesDeleted { get; set; }
    public bool IsBinary { get; set; }
}

public sealed class QaChecklist
{
    public string ReviewerWindowsUser { get; set; } = string.Empty;
    public string ReviewerDisplayName { get; set; } = string.Empty;
    public DateTimeOffset ReviewedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<QaChecklistAnswer> Answers { get; set; } = new();

    [JsonIgnore]
    public bool AllCompliant => Answers.Count == QaChecklistQuestions.All.Count && Answers.All(a => a.IsCompliant);
}

public sealed class QaChecklistAnswer
{
    public int QuestionId { get; set; }
    public string Question { get; set; } = string.Empty;
    public bool IsCompliant { get; set; }
    public string Comment { get; set; } = string.Empty;
}

public static class QaChecklistQuestions
{
    public static IReadOnlyList<(int Id, string Text)> All { get; } = new[]
    {
        (1, "A alteração está relacionada ao incidente ou solicitação informada?"),
        (2, "A alteração está restrita ao necessário para resolver o problema?"),
        (3, "A mudança mantém tratamento adequado de erros e exceções?"),
        (4, "A alteração mantém logs e rastreabilidade suficientes?"),
        (5, "Não foram identificadas mudanças indevidas, credenciais expostas ou riscos evidentes?"),
        (6, "A alteração caracteriza realmente uma sustentação, e não um retrofit?")
    };
}

public sealed class VersionRecord
{
    public string ProjectId { get; set; } = string.Empty;
    public int Version { get; set; }
    public string CommitSha { get; set; } = string.Empty;
    public string ChangeId { get; set; } = string.Empty;
    public string DeveloperWindowsUser { get; set; } = string.Empty;
    public string DeveloperDisplayName { get; set; } = string.Empty;
    public string QaWindowsUser { get; set; } = string.Empty;
    public string QaDisplayName { get; set; } = string.Empty;
    public string ManifestHash { get; set; } = string.Empty;
    public string ManifestRelativePath { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public DateTimeOffset PublishedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public int? RestoredFromVersion { get; set; }
}

public sealed class ManifestDocument
{
    public List<ManifestEntry> Files { get; set; } = new();
    public string AggregateHash { get; set; } = string.Empty;
}

public sealed class ManifestEntry
{
    public string RelativePath { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public long Length { get; set; }
}

public sealed class SecurityConfiguration
{
    public List<string> AdminUsers { get; set; } = new();
    public List<string> QaUsers { get; set; } = new();
    public List<string> QaWindowsGroups { get; set; } = new();
}

public sealed class AuditEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;
    public string ActorWindowsUser { get; set; } = string.Empty;
    public string ActorDisplayName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public string? TargetWindowsUser { get; set; }
    public string? TargetRole { get; set; }
    public string Message { get; set; } = string.Empty;
    public string PreviousHash { get; set; } = string.Empty;
    public string Hash { get; set; } = string.Empty;
}

public sealed class AuditChainState
{
    public string LastHash { get; set; } = string.Empty;
}

public sealed record AuditChainVerificationResult(bool IsValid, string? FirstBrokenEventId);

/// <summary>
/// A file found in production whose content does not match the last version approved by QA.
/// Since every controlled file is only ever supposed to change through a reviewed change
/// request, any such difference means the file was edited directly in production outside the
/// app's workflow — its content is not trustworthy ("COMPROMETIDO") until restored or re-approved.
/// </summary>
public sealed class DriftedFileInfo
{
    public string Path { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public bool IsBinary { get; set; }

    /// <summary>
    /// Best-effort NTFS owner of the file on disk. This is the account that owns the file,
    /// which is usually (but not always) whoever created/last touched its ACL — Windows does
    /// not track "last editor" without Advanced Auditing enabled on the server, so this is a
    /// hint for investigation, not a forensic guarantee. Null when unavailable (non-Windows,
    /// no permission to read the ACL, file removed, etc.).
    /// </summary>
    public string? SuspectedEditor { get; set; }

    public DateTimeOffset? LastWriteTimeUtc { get; set; }
}

/// <summary>
/// Report comparing the current contents of a project's production folder against the last
/// version approved by QA. A non-empty <see cref="Files"/> list means production integrity is
/// compromised: someone wrote to the official folder outside the submit/review flow.
/// </summary>
public sealed class DriftReport
{
    public string ProjectId { get; set; } = string.Empty;
    public string ProjectName { get; set; } = string.Empty;
    public int ApprovedVersion { get; set; }
    public string ApprovedCommitSha { get; set; } = string.Empty;
    public DateTimeOffset GeneratedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public bool HasDrift { get; set; }
    public List<DriftedFileInfo> Files { get; set; } = new();
    public string Patch { get; set; } = string.Empty;

    public const string IntegrityCompromisedStatus = "COMPROMETIDO";
}

public sealed class LocalAppSettings
{
    public string SharedRootPath { get; set; } = string.Empty;
    public DateTimeOffset LastAuditSeenUtc { get; set; } = DateTimeOffset.UtcNow.AddMinutes(-5);
    public bool StartMinimized { get; set; }
    public bool StartWithWindows { get; set; }
    public int PollSeconds { get; set; } = 45;
}

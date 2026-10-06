using System.Security.Cryptography;
using System.Text;
using RpaVersionControl.Core.Models;

namespace RpaVersionControl.Core.Services;

public sealed class AuditStore
{
    private readonly AtomicJsonStore _json;
    private readonly NetworkLockService _locks;

    public AuditStore(AtomicJsonStore json, NetworkLockService locks)
    {
        _json = json;
        _locks = locks;
    }

    public async Task AppendAsync(SharedLayout layout, AuditEvent evt, CancellationToken ct = default)
    {
        // Appends are hash-chained (each event commits the previous event's hash) so that a
        // deleted or edited audit file breaks the chain and can be detected by VerifyChainAsync,
        // instead of audit history being silently alterable like a plain JSON file would be.
        await using var chainLock = await _locks.AcquireAsync(layout.AuditChainLock, evt.ActorWindowsUser, ct: ct);

        var chain = await _json.ReadAsync<AuditChainState>(layout.AuditChainFile, ct) ?? new AuditChainState();
        evt.PreviousHash = chain.LastHash;
        evt.Hash = ComputeHash(evt);

        var day = evt.TimestampUtc.UtcDateTime.ToString("yyyy/MM/dd");
        var dir = Path.Combine(layout.Audit, day.Replace('/', Path.DirectorySeparatorChar));
        var file = Path.Combine(dir, $"{evt.TimestampUtc:HHmmssfff}-{evt.Id}.json");
        await _json.WriteAsync(file, evt, ct);

        chain.LastHash = evt.Hash;
        await _json.WriteAsync(layout.AuditChainFile, chain, ct);
    }

    public async Task<AuditChainVerificationResult> VerifyChainAsync(SharedLayout layout, CancellationToken ct = default)
    {
        if (!Directory.Exists(layout.Audit))
            return new AuditChainVerificationResult(true, null);

        var events = new List<AuditEvent>();
        foreach (var file in Directory.EnumerateFiles(layout.Audit, "*.json", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var evt = await _json.ReadAsync<AuditEvent>(file, ct);
            if (evt is not null) events.Add(evt);
        }

        events = events.OrderBy(x => x.TimestampUtc).ToList();

        var expectedPrevious = string.Empty;
        foreach (var evt in events)
        {
            ct.ThrowIfCancellationRequested();

            if (!string.Equals(evt.PreviousHash, expectedPrevious, StringComparison.Ordinal) ||
                !string.Equals(ComputeHash(evt), evt.Hash, StringComparison.Ordinal))
                return new AuditChainVerificationResult(false, evt.Id);

            expectedPrevious = evt.Hash;
        }

        return new AuditChainVerificationResult(true, null);
    }

    private static string ComputeHash(AuditEvent evt)
    {
        var canonical = string.Join('\n',
            evt.PreviousHash,
            evt.Id,
            evt.TimestampUtc.ToString("O"),
            evt.ActorWindowsUser,
            evt.Action,
            evt.EntityType,
            evt.EntityId,
            evt.ProjectId,
            evt.TargetWindowsUser ?? string.Empty,
            evt.TargetRole ?? string.Empty,
            evt.Message);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    public async Task<IReadOnlyList<AuditEvent>> ReadSinceAsync(
        SharedLayout layout,
        DateTimeOffset sinceUtc,
        CancellationToken ct = default)
    {
        if (!Directory.Exists(layout.Audit))
            return Array.Empty<AuditEvent>();

        var firstDay = sinceUtc.UtcDateTime.Date.AddDays(-1);
        var events = new List<AuditEvent>();

        foreach (var file in Directory.EnumerateFiles(layout.Audit, "*.json", SearchOption.AllDirectories))
        {
            var info = new FileInfo(file);
            if (info.LastWriteTimeUtc < firstDay) continue;

            var evt = await _json.ReadAsync<AuditEvent>(file, ct);
            if (evt is not null && evt.TimestampUtc > sinceUtc)
                events.Add(evt);
        }

        return events.OrderBy(x => x.TimestampUtc).ToList();
    }
}

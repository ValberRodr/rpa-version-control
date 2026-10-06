using RpaVersionControl.Core.Models;

namespace RpaVersionControl.Core.Services;

public sealed class ChangeStore
{
    private sealed class SequenceDocument { public int LastValue { get; set; } }

    private readonly AtomicJsonStore _json;
    private readonly NetworkLockService _locks;

    public ChangeStore(AtomicJsonStore json, NetworkLockService locks)
    {
        _json = json;
        _locks = locks;
    }

    public async Task<string> NextIdAsync(SharedLayout layout, string owner, CancellationToken ct = default)
    {
        await using var lease = await _locks.AcquireAsync(layout.SequenceLock, owner, ct: ct);
        var sequence = await _json.ReadAsync<SequenceDocument>(layout.SequenceFile, ct) ?? new SequenceDocument();
        sequence.LastValue++;
        await _json.WriteAsync(layout.SequenceFile, sequence, ct);
        return $"CHG-{sequence.LastValue:000000}";
    }

    public Task SaveAsync(SharedLayout layout, ChangeRequest change, CancellationToken ct = default)
    {
        change.UpdatedAtUtc = DateTimeOffset.UtcNow;
        return _json.WriteAsync(layout.ChangeFile(change.Id), change, ct);
    }

    public Task<ChangeRequest?> GetAsync(SharedLayout layout, string changeId, CancellationToken ct = default) =>
        _json.ReadAsync<ChangeRequest>(layout.ChangeFile(changeId), ct);

    public async Task<IReadOnlyList<ChangeRequest>> ListAsync(SharedLayout layout, CancellationToken ct = default)
    {
        if (!Directory.Exists(layout.Changes))
            return Array.Empty<ChangeRequest>();

        var result = new List<ChangeRequest>();
        foreach (var file in Directory.EnumerateFiles(layout.Changes, "change.json", SearchOption.AllDirectories))
        {
            var change = await _json.ReadAsync<ChangeRequest>(file, ct);
            if (change is not null) result.Add(change);
        }

        return result.OrderByDescending(x => x.UpdatedAtUtc).ToList();
    }
}

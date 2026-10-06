using RpaVersionControl.Core.Models;

namespace RpaVersionControl.Core.Services;

public sealed class AuditStore
{
    private readonly AtomicJsonStore _json;

    public AuditStore(AtomicJsonStore json) => _json = json;

    public async Task AppendAsync(SharedLayout layout, AuditEvent evt, CancellationToken ct = default)
    {
        var day = evt.TimestampUtc.UtcDateTime.ToString("yyyy/MM/dd");
        var dir = Path.Combine(layout.Audit, day.Replace('/', Path.DirectorySeparatorChar));
        var file = Path.Combine(dir, $"{evt.TimestampUtc:HHmmssfff}-{evt.Id}.json");
        await _json.WriteAsync(file, evt, ct);
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

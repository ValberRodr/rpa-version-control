using System.Text;

namespace RpaVersionControl.Core.Services;

public sealed class NetworkLockService
{
    public async Task<IAsyncDisposable> AcquireAsync(
        string lockPath,
        string owner,
        TimeSpan? timeout = null,
        TimeSpan? staleAfter = null,
        CancellationToken ct = default)
    {
        timeout ??= TimeSpan.FromSeconds(20);
        staleAfter ??= TimeSpan.FromMinutes(5);
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);

        var started = DateTimeOffset.UtcNow;
        var delay = TimeSpan.FromMilliseconds(150);
        var reclaimAttempted = false;

        while (DateTimeOffset.UtcNow - started < timeout)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var stream = new FileStream(
                    lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None,
                    4 * 1024, FileOptions.WriteThrough);

                stream.SetLength(0);
                var payload = Encoding.UTF8.GetBytes($"{owner}\n{DateTimeOffset.UtcNow:O}");
                await stream.WriteAsync(payload, ct);
                await stream.FlushAsync(ct);
                stream.Flush(true);

                return new Lease(stream, lockPath);
            }
            catch (IOException)
            {
                // A lock file left behind by a process that died without releasing it (crash,
                // forced shutdown of the machine) would otherwise block this path forever.
                // Reclaim it exactly once per call if it is older than `staleAfter`; the delete
                // only succeeds when no live process still holds the file open, so a genuinely
                // active lock is never disturbed.
                if (!reclaimAttempted)
                {
                    reclaimAttempted = true;
                    if (TryGetLockAge(lockPath) is { } age && age > staleAfter.Value)
                    {
                        TryReclaimStaleLock(lockPath);
                        continue;
                    }
                }

                await Task.Delay(delay, ct);
                delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 1.6, 1500));
            }
        }

        throw new TimeoutException($"Não foi possível obter o lock: {lockPath}");
    }

    private static TimeSpan? TryGetLockAge(string lockPath)
    {
        try
        {
            if (!File.Exists(lockPath)) return null;
            return DateTimeOffset.UtcNow - File.GetLastWriteTimeUtc(lockPath);
        }
        catch
        {
            return null;
        }
    }

    private static void TryReclaimStaleLock(string lockPath)
    {
        try { File.Delete(lockPath); } catch { /* still held by a live process */ }
    }

    private sealed class Lease : IAsyncDisposable
    {
        private readonly FileStream _stream;
        private readonly string _path;
        private bool _disposed;

        public Lease(FileStream stream, string path)
        {
            _stream = stream;
            _path = path;
        }

        public ValueTask DisposeAsync()
        {
            if (_disposed) return ValueTask.CompletedTask;
            _disposed = true;
            _stream.Dispose();
            try { File.Delete(_path); } catch { }
            return ValueTask.CompletedTask;
        }
    }
}

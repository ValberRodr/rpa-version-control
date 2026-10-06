using System.Text;

namespace RpaVersionControl.Core.Services;

public sealed class NetworkLockService
{
    public async Task<IAsyncDisposable> AcquireAsync(
        string lockPath,
        string owner,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        timeout ??= TimeSpan.FromSeconds(20);
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);

        var started = DateTimeOffset.UtcNow;
        var delay = TimeSpan.FromMilliseconds(150);

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
                await Task.Delay(delay, ct);
                delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 1.6, 1500));
            }
        }

        throw new TimeoutException($"Não foi possível obter o lock: {lockPath}");
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

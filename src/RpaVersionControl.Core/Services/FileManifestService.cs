using System.Security.Cryptography;
using System.Text;
using RpaVersionControl.Core.Models;

namespace RpaVersionControl.Core.Services;

public sealed class FileManifestService
{
    public async Task<ManifestDocument> BuildAsync(
        string root,
        IEnumerable<string> ignorePatterns,
        CancellationToken ct = default)
    {
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException(root);

        var matcher = new IgnoreMatcher(ignorePatterns);
        var entries = new List<ManifestEntry>();

        foreach (var path in EnumerateControlledFiles(root, matcher, ct))
        {
            ct.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');

            var info = new FileInfo(path);
            entries.Add(new ManifestEntry
            {
                RelativePath = relative,
                Length = info.Length,
                Sha256 = await ComputeSha256Async(path, ct)
            });
        }

        entries = entries.OrderBy(x => x.RelativePath, StringComparer.OrdinalIgnoreCase).ToList();
        return new ManifestDocument
        {
            Files = entries,
            AggregateHash = ComputeAggregate(entries)
        };
    }

    public async Task CopyControlledFilesAsync(
        string sourceRoot,
        string destinationRoot,
        IEnumerable<string> ignorePatterns,
        CancellationToken ct = default)
    {
        var matcher = new IgnoreMatcher(ignorePatterns);
        Directory.CreateDirectory(destinationRoot);

        foreach (var path in EnumerateControlledFiles(sourceRoot, matcher, ct))
        {
            ct.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(sourceRoot, path).Replace('\\', '/');

            var destination = Path.Combine(destinationRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await CopyFileAsync(path, destination, ct);
        }
    }

    public async Task ReplaceControlledTreeAsync(
        string snapshotRoot,
        string targetRoot,
        IEnumerable<string> previousControlledPaths,
        CancellationToken ct = default)
    {
        var snapshotFiles = Directory
            .EnumerateFiles(snapshotRoot, "*", SearchOption.AllDirectories)
            .Select(x => Path.GetRelativePath(snapshotRoot, x).Replace('\\', '/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var oldRelative in previousControlledPaths)
        {
            ct.ThrowIfCancellationRequested();
            if (snapshotFiles.Contains(oldRelative))
                continue;

            var target = Path.Combine(targetRoot, oldRelative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(target))
                File.Delete(target);
        }

        foreach (var relative in snapshotFiles)
        {
            ct.ThrowIfCancellationRequested();

            var source = Path.Combine(snapshotRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            var destination = Path.Combine(targetRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

            var temp = destination + "." + Guid.NewGuid().ToString("N") + ".rvc-tmp";
            await CopyFileAsync(source, temp, ct);
            File.Move(temp, destination, overwrite: true);
        }
    }

    public static bool Equivalent(ManifestDocument left, ManifestDocument right) =>
        string.Equals(left.AggregateHash, right.AggregateHash, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> EnumerateControlledFiles(
        string root,
        IgnoreMatcher matcher,
        CancellationToken ct)
    {
        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var current = stack.Pop();

            foreach (var directory in Directory.EnumerateDirectories(current))
            {
                ct.ThrowIfCancellationRequested();
                var attrs = File.GetAttributes(directory);
                if ((attrs & FileAttributes.ReparsePoint) != 0)
                    continue;

                var relative = Path.GetRelativePath(root, directory).Replace('\\', '/');
                if (!matcher.IsIgnoredDirectory(relative))
                    stack.Push(directory);
            }

            foreach (var file in Directory.EnumerateFiles(current))
            {
                ct.ThrowIfCancellationRequested();
                var attrs = File.GetAttributes(file);
                if ((attrs & FileAttributes.ReparsePoint) != 0)
                    continue;

                var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (!matcher.IsIgnored(relative))
                    yield return file;
            }
        }
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(
            path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);

        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, ct);
        return Convert.ToHexString(hash);
    }

    private static string ComputeAggregate(IEnumerable<ManifestEntry> entries)
    {
        using var sha = SHA256.Create();
        var canonical = string.Join('\n',
            entries.Select(x => $"{x.RelativePath}\t{x.Length}\t{x.Sha256}"));
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(canonical)));
    }

    private static async Task CopyFileAsync(string source, string destination, CancellationToken ct)
    {
        await using var input = new FileStream(
            source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);

        await using var output = new FileStream(
            destination, FileMode.Create, FileAccess.Write, FileShare.None,
            128 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough);

        await input.CopyToAsync(output, ct);
        await output.FlushAsync(ct);
        output.Flush(true);
    }
}

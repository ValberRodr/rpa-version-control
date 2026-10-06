using RpaVersionControl.Core.Services;

namespace RpaVersionControl.Core.Tests;

public sealed class FileManifestServiceTests
{
    [Fact]
    public async Task ManifestIgnoresLogsAndIsStable()
    {
        var root = Path.Combine(Path.GetTempPath(), "rvc-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "main.py"), "print('ok')");
            Directory.CreateDirectory(Path.Combine(root, "logs"));
            await File.WriteAllTextAsync(Path.Combine(root, "logs", "run.log"), "noise");

            var service = new FileManifestService();
            var first = await service.BuildAsync(root, IgnoreMatcher.DefaultPatterns);
            var second = await service.BuildAsync(root, IgnoreMatcher.DefaultPatterns);

            Assert.Single(first.Files);
            Assert.Equal("main.py", first.Files[0].RelativePath);
            Assert.Equal(first.AggregateHash, second.AggregateHash);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}

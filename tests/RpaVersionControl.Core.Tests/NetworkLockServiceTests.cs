using RpaVersionControl.Core.Services;

namespace RpaVersionControl.Core.Tests;

public sealed class NetworkLockServiceTests
{
    [Fact]
    public async Task SecondLeaseTimesOutWhileFirstIsHeld()
    {
        var file = Path.Combine(Path.GetTempPath(), "rvc-lock-" + Guid.NewGuid().ToString("N") + ".lock");
        var service = new NetworkLockService();

        await using var first = await service.AcquireAsync(file, "first", TimeSpan.FromSeconds(1));

        await Assert.ThrowsAsync<TimeoutException>(() =>
            service.AcquireAsync(file, "second", TimeSpan.FromMilliseconds(350)));
    }
}

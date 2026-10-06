using System.Text.Json;
using RpaVersionControl.Core.Models;
using RpaVersionControl.Core.Services;
using Xunit;

namespace RpaVersionControl.Core.Tests;

public sealed class AuditStoreTests
{
    private static SharedLayout NewLayout()
    {
        var root = Path.Combine(Path.GetTempPath(), "rvc-audit-" + Guid.NewGuid().ToString("N"));
        var layout = new SharedLayout(root);
        layout.Ensure();
        return layout;
    }

    [Fact]
    public async Task UntamperedChainVerifies()
    {
        var layout = NewLayout();
        var store = new AuditStore(new AtomicJsonStore(), new NetworkLockService());

        await store.AppendAsync(layout, new AuditEvent { ActorWindowsUser = "dev1", Action = "A", Message = "first" });
        await store.AppendAsync(layout, new AuditEvent { ActorWindowsUser = "qa1", Action = "B", Message = "second" });

        var result = await store.VerifyChainAsync(layout);
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task TamperedEventBreaksChain()
    {
        var layout = NewLayout();
        var store = new AuditStore(new AtomicJsonStore(), new NetworkLockService());

        await store.AppendAsync(layout, new AuditEvent { ActorWindowsUser = "dev1", Action = "A", Message = "first" });
        await store.AppendAsync(layout, new AuditEvent { ActorWindowsUser = "qa1", Action = "B", Message = "second" });

        var file = Directory.EnumerateFiles(layout.Audit, "*.json", SearchOption.AllDirectories).First();
        var tampered = JsonSerializer.Deserialize<AuditEvent>(await File.ReadAllTextAsync(file))!;
        tampered.Message = "tampered by someone with filesystem access";
        await File.WriteAllTextAsync(file, JsonSerializer.Serialize(tampered));

        var result = await store.VerifyChainAsync(layout);
        Assert.False(result.IsValid);
    }
}

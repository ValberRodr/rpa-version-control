using Xunit;
using RpaVersionControl.Core.Services;

namespace RpaVersionControl.Core.Tests;

public sealed class IgnoreMatcherTests
{
    [Theory]
    [InlineData("logs/20261005.log")]
    [InlineData("nested/logs/20261005.log")]
    [InlineData("__pycache__/main.pyc")]
    [InlineData("src/__pycache__/helper.pyc")]
    [InlineData("anywhere/process.log")]
    public void DefaultRulesIgnoreOperationalFiles(string path)
    {
        var matcher = new IgnoreMatcher(IgnoreMatcher.DefaultPatterns);
        Assert.True(matcher.IsIgnored(path));
    }

    [Theory]
    [InlineData("main.py")]
    [InlineData("config.json")]
    [InlineData("requirements.txt")]
    [InlineData("resources/input.csv")]
    public void DefaultRulesDoNotIgnorePotentialSourceAssets(string path)
    {
        var matcher = new IgnoreMatcher(IgnoreMatcher.DefaultPatterns);
        Assert.False(matcher.IsIgnored(path));
    }
}

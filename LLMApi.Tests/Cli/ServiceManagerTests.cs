using LLMock.Cli.Service;

namespace LLMApi.Tests.Cli;

public class ServiceManagerTests
{
    [Fact]
    public void GeneratePlist_ContainsLabel()
    {
        var plist = ServiceManager.GeneratePlist("/usr/local/bin/llmock", 5555);
        Assert.Contains("com.llmock.agent", plist);
    }

    [Fact]
    public void GeneratePlist_ContainsExecutablePath()
    {
        var plist = ServiceManager.GeneratePlist("/usr/local/bin/llmock", 5555);
        Assert.Contains("/usr/local/bin/llmock", plist);
    }

    [Fact]
    public void GeneratePlist_ContainsServeHeadless()
    {
        var plist = ServiceManager.GeneratePlist("/usr/local/bin/llmock", 5555);
        Assert.Contains("serve", plist);
        Assert.Contains("--headless", plist);
    }

    [Fact]
    public void GeneratePlist_ContainsRunAtLoad()
    {
        var plist = ServiceManager.GeneratePlist("/usr/local/bin/llmock", 5555);
        Assert.Contains("RunAtLoad", plist);
        Assert.Contains("<true/>", plist);
    }

    [Fact]
    public void GeneratePlist_ContainsLogPath()
    {
        var plist = ServiceManager.GeneratePlist("/usr/local/bin/llmock", 5555);
        Assert.Contains("llmock.log", plist);
        Assert.Contains("StandardOutPath", plist);
    }

    [Fact]
    public void PlistPath_IsInLaunchAgents()
    {
        Assert.Contains("LaunchAgents", ServiceManager.PlistPath);
        Assert.Contains("com.llmock.agent.plist", ServiceManager.PlistPath);
    }
}

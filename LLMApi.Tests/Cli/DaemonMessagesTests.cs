using System.Text.Json;
using LLMock.Cli.Daemon;

namespace LLMApi.Tests.Cli;

public class DaemonMessagesTests
{
    [Fact]
    public void StatsEvent_SerializesAndDeserializes()
    {
        var evt = new StatsEvent(
            DateTime.UtcNow, RequestsPerSec: 5, ActiveContexts: 3,
            TotalRequests: 142, ErrorCount: 0, AvgLatencyMs: 47.2);

        var json = JsonSerializer.Serialize(evt);
        var deserialized = JsonSerializer.Deserialize<StatsEvent>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(5, deserialized.RequestsPerSec);
        Assert.Equal(3, deserialized.ActiveContexts);
        Assert.Equal(142, deserialized.TotalRequests);
        Assert.Equal(47.2, deserialized.AvgLatencyMs);
    }

    [Fact]
    public void LogEvent_SerializesAndDeserializes()
    {
        var evt = new LogEvent(DateTime.UtcNow, "INF", "Server started on :5555");

        var json = JsonSerializer.Serialize(evt);
        var deserialized = JsonSerializer.Deserialize<LogEvent>(json);

        Assert.NotNull(deserialized);
        Assert.Equal("INF", deserialized.Level);
        Assert.Equal("Server started on :5555", deserialized.Message);
    }

    [Fact]
    public void StatusResponse_SerializesAndDeserializes()
    {
        var resp = new StatusResponse(
            Running: true, Version: "2.4.0",
            Uptime: TimeSpan.FromMinutes(42), ActivePack: "wordpress-rest", Port: 5555);

        var json = JsonSerializer.Serialize(resp);
        var deserialized = JsonSerializer.Deserialize<StatusResponse>(json);

        Assert.NotNull(deserialized);
        Assert.True(deserialized.Running);
        Assert.Equal("2.4.0", deserialized.Version);
        Assert.Equal("wordpress-rest", deserialized.ActivePack);
        Assert.Equal(5555, deserialized.Port);
    }

    [Fact]
    public void DaemonController_SocketPath_IsInLLMockDir()
    {
        var socketPath = DaemonController.SocketPath;
        Assert.Contains(".llmock", socketPath);
        Assert.EndsWith("llmock.sock", socketPath);
    }

    [Fact]
    public void DaemonController_PidPath_IsInLLMockDir()
    {
        var pidPath = DaemonController.PidFilePath;
        Assert.Contains(".llmock", pidPath);
        Assert.EndsWith("llmock.pid", pidPath);
    }
}

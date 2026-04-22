namespace LLMock.Cli.Dashboard;

public record ContextSnapshot(string Name, int Calls, DateTime LastUsed)
{
    public int SecondsSinceLastUse => (int)(DateTime.UtcNow - LastUsed).TotalSeconds;
}

public record class DashboardState
{
    public DateTime SnapshotTime { get; init; } = DateTime.UtcNow;
    public int TotalRequests { get; init; }
    public int ActiveContexts { get; init; }
    public int ErrorCount { get; init; }
    public double RequestsPerSec { get; init; }
    public double AvgLatencyMs { get; init; }
    public string ModelName { get; init; } = "gemma4:4b";
    public string? ActivePack { get; init; }
    public int Port { get; init; } = 5555;
    public List<ContextSnapshot> RecentContexts { get; init; } = [];
    public string[] SparklineHistory { get; init; } = []; // last 30 rps samples as sparkline chars
}

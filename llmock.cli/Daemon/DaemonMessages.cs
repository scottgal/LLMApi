namespace LLMock.Cli.Daemon;

public record StatsEvent(
    DateTime Timestamp,
    int RequestsPerSec,
    int ActiveContexts,
    int TotalRequests,
    int ErrorCount,
    double AvgLatencyMs);

public record LogEvent(
    DateTime Timestamp,
    string Level,
    string Message);

public record ShutdownCommand(string Reason = "user-request");

public record StatusResponse(
    bool Running,
    string Version,
    TimeSpan Uptime,
    string? ActivePack,
    int Port);

using System.Text.Json;

namespace LLMock.Cli.Dashboard;

public class DashboardPoller
{
    private static readonly string[] SparklineChars = ["▁", "▂", "▃", "▄", "▅", "▆", "▇", "█"];
    private const int SparklineLength = 30;

    private readonly string _statsUrl;
    private DashboardState _current = new();
    private readonly List<double> _rpsHistory = [];

    public DashboardPoller(int port = 5555)
    {
        _statsUrl = $"http://localhost:{port}/api/dashboard/stats";
    }

    public DashboardState Current => _current;

    /// <summary>Continuously polls the stats endpoint and updates Current state.</summary>
    public async Task RunAsync(Action onUpdate, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var json = await http.GetStringAsync(_statsUrl, ct);
                var prev = _current;
                _current = ParseStats(json, prev);
                onUpdate();

                await Task.Delay(500, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch { /* server not yet ready, retry */ }
        }
    }

    public static double CalculateRps(DashboardState prev, DashboardState current)
    {
        var elapsed = (current.SnapshotTime - prev.SnapshotTime).TotalSeconds;
        if (elapsed <= 0) return 0;
        var delta = current.TotalRequests - prev.TotalRequests;
        return delta <= 0 ? 0 : delta / elapsed;
    }

    private DashboardState ParseStats(string json, DashboardState prev)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var totalRequests = root.TryGetProperty("totalRequests", out var tr) ? tr.GetInt32() : 0;
        var activeContexts = root.TryGetProperty("activeContexts", out var ac) ? ac.GetInt32() : 0;

        var contexts = new List<ContextSnapshot>();
        if (root.TryGetProperty("apiContexts", out var ctxArr))
            foreach (var ctx in ctxArr.EnumerateArray())
            {
                var name = ctx.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                var calls = ctx.TryGetProperty("calls", out var c) ? c.GetInt32() : 0;
                var lastUsed = ctx.TryGetProperty("lastUsed", out var lu)
                    ? lu.GetDateTime()
                    : DateTime.UtcNow;
                contexts.Add(new ContextSnapshot(name, calls, lastUsed));
            }

        var snapshot = new DashboardState
        {
            SnapshotTime = DateTime.UtcNow,
            TotalRequests = totalRequests,
            ActiveContexts = activeContexts,
            RecentContexts = contexts,
        };

        var rps = CalculateRps(prev, snapshot);
        _rpsHistory.Add(rps);
        if (_rpsHistory.Count > SparklineLength)
            _rpsHistory.RemoveAt(0);

        var maxRps = _rpsHistory.Count > 0 ? _rpsHistory.Max() : 1;
        var sparkline = _rpsHistory
            .Select(r => maxRps > 0
                ? SparklineChars[(int)(r / maxRps * (SparklineChars.Length - 1))]
                : SparklineChars[0])
            .ToArray();

        return snapshot with { RequestsPerSec = rps, SparklineHistory = sparkline };
    }
}

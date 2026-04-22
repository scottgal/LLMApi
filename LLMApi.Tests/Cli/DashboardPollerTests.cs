using LLMock.Cli.Dashboard;

namespace LLMApi.Tests.Cli;

public class DashboardPollerTests
{
    [Fact]
    public void DashboardState_Default_HasZeroValues()
    {
        var state = new DashboardState();
        Assert.Equal(0, state.TotalRequests);
        Assert.Equal(0, state.ActiveContexts);
        Assert.Equal(0.0, state.RequestsPerSec);
        Assert.Empty(state.RecentContexts);
    }

    [Fact]
    public void CalculateRequestsPerSec_WithGrowingCount_ReturnsPositive()
    {
        var prev = new DashboardState { TotalRequests = 100, SnapshotTime = DateTime.UtcNow.AddSeconds(-1) };
        var current = new DashboardState { TotalRequests = 105, SnapshotTime = DateTime.UtcNow };

        var rps = DashboardPoller.CalculateRps(prev, current);

        Assert.True(rps > 0);
        Assert.True(rps <= 10); // 5 req in ~1 sec
    }

    [Fact]
    public void CalculateRequestsPerSec_WithNoChange_ReturnsZero()
    {
        var prev = new DashboardState { TotalRequests = 100, SnapshotTime = DateTime.UtcNow.AddSeconds(-1) };
        var current = new DashboardState { TotalRequests = 100, SnapshotTime = DateTime.UtcNow };

        var rps = DashboardPoller.CalculateRps(prev, current);

        Assert.Equal(0.0, rps);
    }

    [Fact]
    public void ContextSnapshot_PopulatesFromApiContext()
    {
        var snapshot = new ContextSnapshot("test-context", 42, DateTime.UtcNow.AddSeconds(-5));
        Assert.Equal("test-context", snapshot.Name);
        Assert.Equal(42, snapshot.Calls);
        Assert.True(snapshot.SecondsSinceLastUse >= 4);
    }
}

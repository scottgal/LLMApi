using mostlylucid.mockllmapi;
using mostlylucid.mockllmapi.Models;

namespace LLMApi.Tests;

public class ContinuousStreamingTests
{
    [Fact]
    public void ContinuousStreaming_DefaultDisabled()
    {
        // Arrange
        var options = new LLMockApiOptions();

        // Assert
        Assert.False(options.EnableContinuousStreaming);
    }

    [Fact]
    public void ContinuousStreaming_CanBeEnabled()
    {
        // Arrange
        var options = new LLMockApiOptions
        {
            EnableContinuousStreaming = true
        };

        // Assert
        Assert.True(options.EnableContinuousStreaming);
    }

    [Fact]
    public void ContinuousStreaming_DefaultInterval()
    {
        // Arrange
        var options = new LLMockApiOptions();

        // Assert
        Assert.Equal(2000, options.ContinuousStreamingIntervalMs);
    }

    [Theory]
    [InlineData(1000)]
    [InlineData(3000)]
    [InlineData(5000)]
    public void ContinuousStreaming_CustomInterval(int interval)
    {
        // Arrange
        var options = new LLMockApiOptions
        {
            ContinuousStreamingIntervalMs = interval
        };

        // Assert
        Assert.Equal(interval, options.ContinuousStreamingIntervalMs);
    }

    [Fact]
    public void ContinuousStreaming_DefaultMaxDuration()
    {
        // Arrange
        var options = new LLMockApiOptions();

        // Assert
        Assert.Equal(300, options.ContinuousStreamingMaxDurationSeconds);
    }

    [Theory]
    [InlineData(0)] // Unlimited
    [InlineData(60)]
    [InlineData(600)]
    public void ContinuousStreaming_CustomMaxDuration(int duration)
    {
        // Arrange
        var options = new LLMockApiOptions
        {
            ContinuousStreamingMaxDurationSeconds = duration
        };

        // Assert
        Assert.Equal(duration, options.ContinuousStreamingMaxDurationSeconds);
    }

    [Fact]
    public void ContinuousStreaming_AllSettingsConfigurable()
    {
        // Arrange
        var options = new LLMockApiOptions
        {
            EnableContinuousStreaming = true,
            ContinuousStreamingIntervalMs = 1500,
            ContinuousStreamingMaxDurationSeconds = 120
        };

        // Assert
        Assert.True(options.EnableContinuousStreaming);
        Assert.Equal(1500, options.ContinuousStreamingIntervalMs);
        Assert.Equal(120, options.ContinuousStreamingMaxDurationSeconds);
    }

    [Fact]
    public void ContinuousStreaming_WorksWithAllSseModes()
    {
        // Arrange & Assert - No exceptions should be thrown
        var options1 = new LLMockApiOptions
        {
            EnableContinuousStreaming = true,
            SseMode = SseMode.LlmTokens
        };
        Assert.Equal(SseMode.LlmTokens, options1.SseMode);

        var options2 = new LLMockApiOptions
        {
            EnableContinuousStreaming = true,
            SseMode = SseMode.CompleteObjects
        };
        Assert.Equal(SseMode.CompleteObjects, options2.SseMode);

        var options3 = new LLMockApiOptions
        {
            EnableContinuousStreaming = true,
            SseMode = SseMode.ArrayItems
        };
        Assert.Equal(SseMode.ArrayItems, options3.SseMode);
    }

    [Fact]
    public void ContinuousStreaming_BackwardCompatible()
    {
        // Arrange - Default options should not enable continuous streaming
        var options = new LLMockApiOptions();

        // Assert - Continuous streaming is opt-in
        Assert.False(options.EnableContinuousStreaming);

        // Assert - Default SSE mode is still LlmTokens
        Assert.Equal(SseMode.LlmTokens, options.SseMode);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(500)]
    [InlineData(2000)]
    [InlineData(10000)]
    public void ContinuousStreaming_AcceptsVariousIntervals(int interval)
    {
        // Arrange
        var options = new LLMockApiOptions
        {
            ContinuousStreamingIntervalMs = interval
        };

        // Assert
        Assert.Equal(interval, options.ContinuousStreamingIntervalMs);
        Assert.True(options.ContinuousStreamingIntervalMs >= 0);
    }

    [Fact]
    public void ContinuousStreaming_ZeroMaxDurationMeansUnlimited()
    {
        // Arrange
        var options = new LLMockApiOptions
        {
            EnableContinuousStreaming = true,
            ContinuousStreamingMaxDurationSeconds = 0
        };

        // Assert
        Assert.Equal(0, options.ContinuousStreamingMaxDurationSeconds);
    }

    [Fact]
    public void ContinuousStreaming_Configuration_Documentation()
    {
        // This test documents the expected configuration structure
        var options = new LLMockApiOptions
        {
            // Continuous streaming disabled by default for backward compatibility
            EnableContinuousStreaming = false,

            // Default interval: 2 seconds between events
            ContinuousStreamingIntervalMs = 2000,

            // Default max duration: 5 minutes
            ContinuousStreamingMaxDurationSeconds = 300,

            // Works with all SSE modes
            SseMode = SseMode.CompleteObjects
        };

        Assert.False(options.EnableContinuousStreaming);
        Assert.Equal(2000, options.ContinuousStreamingIntervalMs);
        Assert.Equal(300, options.ContinuousStreamingMaxDurationSeconds);
        Assert.Equal(SseMode.CompleteObjects, options.SseMode);
    }
}
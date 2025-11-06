# Continuous SSE Streaming

LLMock API now supports **continuous SSE streaming mode**, allowing SSE endpoints to work just like SignalR hubs - keeping connections open and continuously generating new data at regular intervals.

## Overview

| Feature | Regular SSE | Continuous SSE |For the SSE 
|---------|-------------|----------------|
| **Connection** | One-shot, closes after response | Stays open, continuous updates |
| **Use Case** | Single data generation | Real-time dashboards, live feeds |
| **Behavior** | Generate once → close | Generate → wait → generate → wait... |
| **Similar To** | REST API | SignalR/WebSocket |

## Why Continuous Streaming?

While SSE traditionally streams data once and closes, many real-world scenarios require **long-lived connections** that continuously send updates:

- 📊 **Live Dashboards** - Stock tickers, analytics, monitoring
- 🔔 **Real-Time Notifications** - Alerts, status updates
- 🌡️ **IoT Sensors** - Temperature, pressure, environmental data
- 📈 **Market Data** - Prices, trades, order books
- 🚨 **Event Streams** - Logs, audit trails, system events

Continuous streaming bridges the gap between SSE and SignalR, letting you test long-lived SSE connections without running a full SignalR hub.

## Quick Start

### Enable Via Query Parameter

```http
GET /api/mock/stream/stocks?continuous=true&interval=2000&sseMode=CompleteObjects
```

### Enable Via Header

```http
GET /api/mock/stream/stocks
X-Continuous-Streaming: true
```

### Enable Via Shape JSON

```http
GET /api/mock/stream/stocks?shape={"$continuous":true,"ticker":"AAPL","price":150.25}
```

## Configuration

### Global Settings (appsettings.json)

```json
{
  "MockLlmApi": {
    "EnableContinuousStreaming": false,  // Default: disabled for backward compatibility
    "ContinuousStreamingIntervalMs": 2000,  // 2 seconds between events
    "ContinuousStreamingMaxDurationSeconds": 300  // 5 minutes max duration
  }
}
```

### Per-Request Override

**Query Parameters:**
```http
?continuous=true
  &interval=3000              // Milliseconds between events
  &maxDuration=600            // Seconds (0 = unlimited)
  &sseMode=CompleteObjects    // Works with all three SSE modes
```

**HTTP Headers:**
```http
X-Continuous-Streaming: true
```

## How It Works

### Event Flow

```
1. Client connects to /api/mock/stream/stocks?continuous=true&interval=2000
2. Server sends INFO event with configuration
3. Server generates new data
4. Server sends data event
5. Server waits 2000ms
6. Go to step 3 (repeat until max duration or client disconnects)
7. Server sends END event
```

### Event Types

Continuous streaming introduces special event types:

**INFO Event** (sent at connection start):
```json
{
  "type": "info",
  "message": "Continuous streaming started",
  "mode": "CompleteObjects",
  "intervalMs": 2000,
  "maxDurationSeconds": 300
}
```

**Data Events** (normal SSE events with additional metadata):
```json
{
  "data": { "ticker": "AAPL", "price": 150.32 },
  "index": 0,
  "timestamp": "2025-01-06T12:34:56Z",
  "done": false
}
```

**ERROR Event** (if generation fails):
```json
{
  "type": "error",
  "message": "LLM timeout",
  "eventCount": 5
}
```

**END Event** (when max duration reached or manually stopped):
```json
{
  "type": "end",
  "message": "Max duration reached",
  "eventCount": 150,
  "done": true
}
```

## Working with All Three SSE Modes

Continuous streaming works seamlessly with all SSE modes:

### LlmTokens Mode + Continuous

```http
GET /api/mock/stream/chat?continuous=true&interval=5000&sseMode=LlmTokens
```

**Behavior:**
- Generates complete message every 5 seconds
- Streams each message token-by-token
- Sends final event for each batch
- Includes `batchNumber` in events

**Use Case:** Testing chat interfaces with multiple responses

### CompleteObjects Mode + Continuous

```http
GET /api/mock/stream/stocks?continuous=true&interval=1000&sseMode=CompleteObjects&shape={"ticker":"AAPL","price":150.0}
```

**Behavior:**
- Generates new stock price every 1 second
- Each event contains complete stock object
- Includes `timestamp` for real-time ordering

**Use Case:** Stock tickers, dashboards, real-time feeds

### ArrayItems Mode + Continuous

```http
GET /api/mock/stream/sensors?continuous=true&interval=3000&sseMode=ArrayItems&shape={"sensors":[{"id":"s1","temp":72}]}
```

**Behavior:**
- Generates sensor batch every 3 seconds
- Streams each sensor as separate event
- Includes `batchNumber` to group related items

**Use Case:** IoT sensor dashboards, multi-item monitoring

## Client Examples

### JavaScript (EventSource)

```javascript
const eventSource = new EventSource(
  '/api/mock/stream/stocks?continuous=true&interval=2000&sseMode=CompleteObjects'
);

eventSource.onmessage = (event) => {
  const data = JSON.parse(event.data);

  // Handle special event types
  if (data.type === 'info') {
    console.log(`Streaming started: ${data.mode}, interval: ${data.intervalMs}ms`);
    return;
  }

  if (data.type === 'end') {
    console.log(`Stream ended after ${data.eventCount} events`);
    eventSource.close();
    return;
  }

  if (data.type === 'error') {
    console.error(`Error at event ${data.eventCount}: ${data.message}`);
    return;
  }

  // Handle data events (CompleteObjects mode)
  if (data.data) {
    console.log(`Stock update #${data.index}:`, data.data);
    updateDashboard(data.data);
  }
};

eventSource.onerror = (error) => {
  console.error('Connection error:', error);
  eventSource.close();
};

// Stop after 60 seconds
setTimeout(() => eventSource.close(), 60000);
```

### C# (.NET Client)

```csharp
using var client = new HttpClient();
using var request = new HttpRequestMessage(HttpMethod.Get,
    "http://localhost:5000/api/mock/stream/stocks?continuous=true&interval=2000");

using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
using var stream = await response.Content.ReadAsStreamAsync();
using var reader = new StreamReader(stream);

while (!reader.EndOfStream)
{
    var line = await reader.ReadLineAsync();
    if (string.IsNullOrWhiteSpace(line)) continue;

    if (line.StartsWith("data: "))
    {
        var json = line.Substring(6);
        var data = JsonSerializer.Deserialize<JsonElement>(json);

        if (data.TryGetProperty("type", out var typeEl))
        {
            var type = typeEl.GetString();
            if (type == "info")
            {
                Console.WriteLine($"Streaming started");
            }
            else if (type == "end")
            {
                Console.WriteLine("Stream ended");
                break;
            }
        }
        else if (data.TryGetProperty("data", out var dataEl))
        {
            Console.WriteLine($"Stock update: {dataEl}");
        }
    }
}
```

## Configuration Examples

### Stock Ticker Dashboard

```json
{
  "MockLlmApi": {
    "EnableContinuousStreaming": true,
    "ContinuousStreamingIntervalMs": 1000,
    "ContinuousStreamingMaxDurationSeconds": 3600,
    "SseMode": "CompleteObjects"
  }
}
```

**Request:**
```http
GET /api/mock/stream/stocks?shape={"ticker":"AAPL","price":150.0,"change":2.5}
```

### IoT Sensor Monitoring

```json
{
  "MockLlmApi": {
    "EnableContinuousStreaming": false,  // Opt-in per request
    "ContinuousStreamingIntervalMs": 5000,
    "SseMode": "ArrayItems"
  }
}
```

**Request:**
```http
GET /api/mock/stream/sensors?continuous=true&interval=5000&shape={"sensors":[{"id":"s1","temp":72,"humidity":45}]}
```

### Live Log Streaming

```json
{
  "MockLlmApi": {
    "EnableContinuousStreaming": true,
    "ContinuousStreamingIntervalMs": 2000,
    "ContinuousStreamingMaxDurationSeconds": 600,
    "SseMode": "ArrayItems"
  }
}
```

**Request:**
```http
GET /api/mock/stream/logs?shape={"logs":[{"level":"info","message":"string","timestamp":"2025-01-06T12:00:00Z"}]}
```

## Advanced Features

### Unlimited Duration

Set `maxDuration=0` for unlimited streaming (use with caution):

```http
GET /api/mock/stream/data?continuous=true&maxDuration=0
```

### Variable Intervals

Adjust interval per request:

```http
# Fast updates (100ms)
GET /api/mock/stream/fast?continuous=true&interval=100

# Slow updates (30s)
GET /api/mock/stream/slow?continuous=true&interval=30000
```

### Combined with Context Tracking

```http
GET /api/mock/stream/data?continuous=true&contextName=session-123
```

Maintains consistency across all generated events in the stream.

### Combined with Backend Selection

```http
GET /api/mock/stream/data?continuous=true&backend=ollama-mistral-nemo
```

Use specific LLM backend for continuous generation.

## Performance Considerations

### Resource Management

**Memory:**
- Each continuous connection holds a thread/task
- Limit concurrent connections appropriately
- Set reasonable `maxDuration` to prevent leaks

**LLM Usage:**
- Each event generates new LLM request
- High-frequency intervals (< 500ms) may overwhelm LLM
- Consider using multiple backends for load distribution

**Recommended Intervals:**
- **Fast updates:** 500-1000ms (0.5-1 second)
- **Normal updates:** 2000-5000ms (2-5 seconds)
- **Slow updates:** 10000-30000ms (10-30 seconds)

### Scaling

For production-like testing with many concurrent streams:

```json
{
  "MockLlmApi": {
    "LlmBackends": [
      { "Name": "ollama-1", "Provider": "ollama", "Weight": 3 },
      { "Name": "ollama-2", "Provider": "ollama", "Weight": 3 },
      { "Name": "ollama-3", "Provider": "ollama", "Weight": 3 }
    ],
    "EnableContinuousStreaming": true,
    "ContinuousStreamingIntervalMs": 2000,
    "ContinuousStreamingMaxDurationSeconds": 600
  }
}
```

## Use Case Examples

### Real-Time Stock Dashboard

```javascript
// Connect to continuous stock feed
const stocks = ['AAPL', 'GOOGL', 'MSFT', 'AMZN'];
const connections = stocks.map(ticker => {
  const es = new EventSource(
    `/api/mock/stream/stocks/${ticker}?continuous=true&interval=1000&sseMode=CompleteObjects`
  );

  es.onmessage = (event) => {
    const data = JSON.parse(event.data);
    if (data.data) {
      updateStockWidget(ticker, data.data);
    }
  };

  return es;
});

// Cleanup after 5 minutes
setTimeout(() => connections.forEach(es => es.close()), 300000);
```

### IoT Temperature Monitoring

```http
GET /api/mock/stream/temperature?continuous=true&interval=5000&sseMode=ArrayItems&shape={"rooms":[{"id":"living-room","temp":72,"humidity":45}]}
```

Updates every 5 seconds with new temperature/humidity readings for all rooms.

### System Log Aggregation

```http
GET /api/mock/stream/logs?continuous=true&interval=3000&sseMode=ArrayItems&shape={"logs":[{"level":"info","service":"api","message":"string"}]}
```

Generates new log entries every 3 seconds from multiple services.

## Troubleshooting

### Connection Keeps Closing

**Problem:** Stream ends immediately

**Solutions:**
1. Verify `continuous=true` is in URL or header
2. Check `maxDuration` isn't too low
3. Ensure client keeps connection open (doesn't close EventSource)

### Too Many Events

**Problem:** Overwhelming number of events

**Solutions:**
1. Increase `interval` parameter
2. Set appropriate `maxDuration`
3. Use `stopStream()` or close EventSource when done

### Events Not Varying

**Problem:** Same data in every event

**Solutions:**
1. Continuous mode adds timestamp/event count to prompts for variation
2. LLM may need more explicit instructions in shape
3. Try different SSE mode or shape structure

### Memory Issues

**Problem:** High memory usage with continuous streams

**Solutions:**
1. Set `maxDuration` to prevent infinite connections
2. Limit concurrent connections
3. Use load balancing with multiple backends

## Comparison: SSE vs SignalR

| Feature | Regular SSE | Continuous SSE | SignalR |
|---------|-------------|----------------|---------|
| **Connection Type** | One-shot | Long-lived | Long-lived |
| **Bidirectional** | No | No | Yes |
| **Setup Complexity** | Low | Low | Medium |
| **Browser Support** | Wide | Wide | Wide |
| **Resource Usage** | Low | Medium | Medium |
| **Data Generation** | Once | Continuous | Continuous |

**When to use Continuous SSE:**
- Testing SSE clients with live data feeds
- Mocking real-time APIs without SignalR overhead
- Simulating long-polling or streaming endpoints
- Need server-to-client only (no client commands needed)

**When to use SignalR:**
- Need bidirectional communication
- Client needs to send commands/requests
- Hub-based architecture with multiple subscriptions

## Migration from Regular SSE

Continuous mode is **100% backward compatible**:

**Before:**
```http
GET /api/mock/stream/users
# Generates once, closes
```

**After (opt-in):**
```http
GET /api/mock/stream/users?continuous=true
# Generates continuously, stays open
```

**No code changes needed** - just add the parameter when you want continuous behavior!

## API Reference

### Query Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `continuous` | boolean | false | Enable continuous streaming |
| `interval` | integer | 2000 | Milliseconds between events |
| `maxDuration` | integer | 300 | Max duration in seconds (0 = unlimited) |
| `sseMode` | string | LlmTokens | SSE streaming mode |

### HTTP Headers

| Header | Value | Description |
|--------|-------|-------------|
| `X-Continuous-Streaming` | "true"/"false" | Enable continuous mode |

### Configuration Options

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `EnableContinuousStreaming` | bool | false | Global enable/disable |
| `ContinuousStreamingIntervalMs` | int | 2000 | Default interval |
| `ContinuousStreamingMaxDurationSeconds` | int | 300 | Default max duration |

## Testing

Run continuous streaming tests:

```bash
dotnet test --filter "FullyQualifiedName~ContinuousStreamingTests"
```

**Test Coverage:**
- ✅ 19 continuous streaming tests
- ✅ Configuration validation
- ✅ All SSE modes compatibility
- ✅ Backward compatibility verification

## Related Documentation

- **[SSE Streaming Modes](./SSE_STREAMING_MODES.md)** - Complete SSE mode guide
- **[Configuration Reference](./CONFIGURATION_REFERENCE.md)** - All config options
- **[HTTP Examples](../LLMApi/SSE_Streaming.http)** - Ready-to-run examples
- **[Multiple LLM Backends](./MULTIPLE_LLM_BACKENDS.md)** - Backend selection

---

**Version:** 2.1.0
**Date:** 2025-01-06
**Status:** Production Ready ✅

# Rate Limiting & Batching

**Added in:** v2.1.0
**Use case:** Test client rate limit handling, backoff strategies, concurrent requests, and timeout scenarios

## Overview

MockLLMApi now supports **rate limiting simulation** and **n-completions batching** to help you test how your applications handle rate-limited APIs, slow responses, and multiple completion variants.

### Key Features

- 🎯 **Per-endpoint statistics tracking** with moving averages
- ⏱️ **Configurable delay ranges** for simulating rate limits
- 🔄 **Multiple batching strategies** (Auto, Sequential, Parallel, Streaming)
- 📊 **Detailed response headers** with timing information
- 🚀 **N-completions support** for generating multiple response variants
- 🔧 **Fully backward compatible** - opt-in feature

## Table of Contents

- [Quick Start](#quick-start)
- [Configuration](#configuration)
- [Usage Examples](#usage-examples)
- [Batching Strategies](#batching-strategies)
- [Response Headers](#response-headers)
- [Response Format](#response-format)
- [Use Cases](#use-cases)
- [API Reference](#api-reference)

---

## Quick Start

### 1. Enable in Configuration

```json
{
  "MockLlmApi": {
    "EnableRateLimiting": true,
    "RateLimitDelayRange": "500-2000",
    "RateLimitStrategy": "Auto",
    "EnableRateLimitStatistics": true
  }
}
```

### 2. Request Multiple Completions

```http
GET /api/mock/users?n=3&rateLimit=500-2000
```

### 3. Check Response Headers

```
X-RateLimit-Limit: 25
X-RateLimit-Remaining: 24
X-LLMApi-Avg-Time: 2100
X-LLMApi-Delay-Applied: 1500
```

---

## Configuration

Add these settings to your `appsettings.json` under the `MockLlmApi` section:

```json
{
  "MockLlmApi": {
    // Enable rate limiting simulation (default: false)
    "EnableRateLimiting": false,

    // Delay range in milliseconds (default: null = disabled)
    // Format: "min-max" (e.g., "500-4000"), "max", "avg", or a fixed number
    "RateLimitDelayRange": null,

    // Batching strategy for n-completions (default: Auto)
    // Options: Auto, Sequential, Parallel, Streaming
    "RateLimitStrategy": "Auto",

    // Enable per-endpoint statistics tracking (default: true)
    "EnableRateLimitStatistics": true,

    // Window size for moving average (default: 10)
    "RateLimitStatsWindowSize": 10
  }
}
```

### Configuration Options Explained

#### `EnableRateLimiting`
- **Type:** `boolean`
- **Default:** `false`
- **Description:** Master switch for rate limiting features. When disabled, no delays are applied regardless of other settings.

#### `RateLimitDelayRange`
- **Type:** `string` or `null`
- **Default:** `null`
- **Description:** Controls how delays are calculated:
  - `"500-4000"` - Random delay between 500ms and 4000ms
  - `"max"` - Delay matches actual LLM response time (doubles total response time)
  - `"avg"` - Delay matches endpoint's moving average response time
  - `"1500"` - Fixed delay of 1500ms
  - `null` - No delay (same as `EnableRateLimiting: false`)

#### `RateLimitStrategy`
- **Type:** `enum` (`Auto`, `Sequential`, `Parallel`, `Streaming`)
- **Default:** `Auto`
- **Description:** Strategy for executing n-completions (see [Batching Strategies](#batching-strategies))

#### `EnableRateLimitStatistics`
- **Type:** `boolean`
- **Default:** `true`
- **Description:** Tracks per-endpoint response times for calculating realistic rate limits

#### `RateLimitStatsWindowSize`
- **Type:** `int`
- **Default:** `10`
- **Description:** Number of recent requests to include in moving average calculation

---

## Usage Examples

### Basic N-Completions

Request 3 variations of the same response:

```http
GET /api/mock/users?n=3
```

Response:
```json
{
  "completions": [
    {
      "index": 0,
      "content": [{"id": 1, "name": "Alice Johnson"}],
      "timing": {"requestTimeMs": 2340, "delayAppliedMs": null}
    },
    {
      "index": 1,
      "content": [{"id": 2, "name": "Bob Smith"}],
      "timing": {"requestTimeMs": 2280, "delayAppliedMs": null}
    },
    {
      "index": 2,
      "content": [{"id": 3, "name": "Charlie Davis"}],
      "timing": {"requestTimeMs": 2410, "delayAppliedMs": null}
    }
  ],
  "meta": {
    "strategy": "Parallel",
    "totalRequestTimeMs": 7030,
    "totalDelayMs": 0,
    "totalElapsedMs": 2410,
    "averageRequestTimeMs": 2343
  }
}
```

### With Rate Limiting

Add delays between completions:

```http
GET /api/mock/users?n=5&rateLimit=500-2000&strategy=sequential
```

This will:
1. Generate first completion
2. Wait 500-2000ms (random)
3. Generate second completion
4. Wait 500-2000ms
5. Repeat for all 5 completions

### Match LLM Response Time

Use `"max"` to simulate APIs that rate limit based on processing time:

```http
GET /api/mock/products?n=3&rateLimit=max
```

If each LLM request takes ~2000ms, the delay will also be ~2000ms, effectively doubling the response time.

### Header-Based Configuration

Override config via headers:

```http
GET /api/mock/orders?n=4
X-Rate-Limit-Delay: 1000-3000
X-Rate-Limit-Strategy: parallel
```

**Precedence order:**
1. Query parameters (`?rateLimit=`, `?strategy=`)
2. HTTP headers (`X-Rate-Limit-Delay`, `X-Rate-Limit-Strategy`)
3. Global configuration (`appsettings.json`)

### Per-Request Override

Enable rate limiting for a single request even if globally disabled:

```http
GET /api/mock/users?n=2&rateLimit=1000-2000
```

Disable for a single request even if globally enabled:

```http
GET /api/mock/users?n=2&rateLimit=0
```

---

## Batching Strategies

Choose how multiple completions are executed:

### Auto (Default)

System automatically selects the best strategy based on `n`:

- **n = 1**: No batching (single request)
- **n = 2-5**: `Parallel` (fastest for small batches)
- **n > 5**: `Streaming` (most efficient for large batches)

**Example:**
```http
GET /api/mock/users?n=10&strategy=auto
# Automatically uses Streaming strategy
```

### Sequential

Execute requests one at a time with delays between each.

**Pattern:** Request 1 → Delay → Request 2 → Delay → Request 3

**Best for:**
- Testing backoff strategies
- Predictable timing requirements
- Sequential dependency validation

**Example:**
```http
GET /api/mock/users?n=3&rateLimit=1000-2000&strategy=sequential
```

**Timeline:**
```
0ms:    Start Request 1
2340ms: Complete Request 1
2340ms: Apply delay (1500ms)
3840ms: Start Request 2
6120ms: Complete Request 2
6120ms: Apply delay (1200ms)
7320ms: Start Request 3
9730ms: Complete Request 3
Total: 9730ms
```

### Parallel

Start all requests simultaneously, stagger response delivery with delays.

**Pattern:** Start all → Complete independently → Apply delays → Return in order

**Best for:**
- Fast completion with simulated rate limiting
- Testing concurrent request handling
- Maximum throughput testing

**Example:**
```http
GET /api/mock/users?n=4&rateLimit=500-1500&strategy=parallel
```

**Timeline:**
```
0ms:    Start all 4 requests in parallel
2340ms: Complete Request 1, delay 1000ms, deliver at 3340ms
2380ms: Complete Request 2, delay 1200ms, deliver at 3580ms
2410ms: Complete Request 3, delay 1100ms, deliver at 3510ms
2360ms: Complete Request 4, delay 1300ms, deliver at 3660ms
Total: ~3660ms (vs 9490ms sequential)
```

### Streaming

Return results as they complete with rate-limited delays between each.

**Pattern:** Complete Request 1 → Deliver → Delay → Complete Request 2 → Deliver → Delay

**Best for:**
- Real-time UIs with SSE
- Large batch processing (n > 5)
- Progressive result delivery

**Example:**
```http
GET /api/mock/users?n=6&rateLimit=500-1000&strategy=streaming
```

**Timeline:**
```
0ms:     All requests start via native n-completions API
2400ms:  First result available, deliver immediately
3400ms:  Second result (1000ms delay applied)
4300ms:  Third result (900ms delay applied)
5200ms:  Fourth result (900ms delay applied)
...
```

---

## Response Headers

All responses include detailed timing and rate limit information:

### Standard Rate Limit Headers

```
X-RateLimit-Limit: 25
X-RateLimit-Remaining: 24
X-RateLimit-Reset: 1704067200
```

- **X-RateLimit-Limit**: Maximum requests allowed per minute (calculated from avg response time)
- **X-RateLimit-Remaining**: Requests remaining in current window (simulated)
- **X-RateLimit-Reset**: Unix timestamp when rate limit resets

### Custom Timing Headers

```
X-LLMApi-Request-Time: 2340
X-LLMApi-Avg-Time: 2100
X-LLMApi-Total-Elapsed: 11520
X-LLMApi-Delay-Applied: 1500
```

- **X-LLMApi-Request-Time**: This request's LLM response time in milliseconds
- **X-LLMApi-Avg-Time**: Moving average response time for this endpoint
- **X-LLMApi-Total-Elapsed**: Total time including LLM + delays
- **X-LLMApi-Delay-Applied**: Total artificial delay applied (for n-completions, sum of all delays)

### Example Response

```http
HTTP/1.1 200 OK
Content-Type: application/json
X-RateLimit-Limit: 25
X-RateLimit-Remaining: 24
X-RateLimit-Reset: 1704067200
X-LLMApi-Request-Time: 2340
X-LLMApi-Avg-Time: 2100
X-LLMApi-Total-Elapsed: 11520
X-LLMApi-Delay-Applied: 4500

{...response body...}
```

---

## Response Format

### Single Completion (n=1 or n not specified)

Standard JSON response without wrapper:

```json
[
  {"id": 1, "name": "Alice Johnson"},
  {"id": 2, "name": "Bob Smith"}
]
```

### Multiple Completions (n>1)

Structured response with timing metadata:

```json
{
  "completions": [
    {
      "index": 0,
      "content": [
        {"id": 1, "name": "Alice Johnson"}
      ],
      "timing": {
        "requestTimeMs": 2340,
        "delayAppliedMs": 1500
      }
    },
    {
      "index": 1,
      "content": [
        {"id": 2, "name": "Bob Smith"}
      ],
      "timing": {
        "requestTimeMs": 2280,
        "delayAppliedMs": 1200
      }
    },
    {
      "index": 2,
      "content": [
        {"id": 3, "name": "Charlie Davis"}
      ],
      "timing": {
        "requestTimeMs": 2410,
        "delayAppliedMs": 1300
      }
    }
  ],
  "meta": {
    "strategy": "Sequential",
    "totalRequestTimeMs": 7030,
    "totalDelayMs": 4000,
    "totalElapsedMs": 11030,
    "averageRequestTimeMs": 2343
  }
}
```

#### Field Descriptions

**completions[]:**
- `index`: Zero-based index of the completion
- `content`: The generated JSON content (parsed object)
- `timing.requestTimeMs`: LLM processing time for this specific completion
- `timing.delayAppliedMs`: Artificial delay applied after this completion (null if none)

**meta:**
- `strategy`: The batching strategy used (Auto resolves to actual strategy)
- `totalRequestTimeMs`: Sum of all LLM processing times
- `totalDelayMs`: Sum of all artificial delays
- `totalElapsedMs`: Total wall-clock time for the request
- `averageRequestTimeMs`: Average LLM processing time per completion

---

## Use Cases

### 1. Testing Rate Limit Handling

Simulate 429 responses and verify your backoff logic:

```http
GET /api/mock/users?n=10&rateLimit=100-500&strategy=sequential
```

Monitor response times to ensure your client handles delays appropriately.

### 2. Timeout Testing

Test how your app handles slow APIs:

```http
GET /api/mock/products?n=1&rateLimit=5000
# Adds 5 second delay to test timeout behavior
```

### 3. Concurrent Request Testing

Generate multiple completions in parallel:

```http
GET /api/mock/orders?n=5&strategy=parallel
```

Verify your app can handle multiple simultaneous responses.

### 4. Response Variation Testing

Generate diverse mock data for the same request:

```http
GET /api/mock/users?shape={"name":"string","age":"number"}&n=5
```

Each completion will have different names and ages, simulating real API variance.

### 5. Performance Testing

Measure how rate limiting impacts your app's performance:

```bash
# No rate limiting
curl "/api/mock/users?n=10" -w "Time: %{time_total}s\n"

# With rate limiting
curl "/api/mock/users?n=10&rateLimit=500-1000" -w "Time: %{time_total}s\n"
```

### 6. Backoff Strategy Validation

Test exponential backoff implementations:

```http
# First request - fast
GET /api/mock/users?rateLimit=100-200

# Subsequent requests - simulate increasing delays
GET /api/mock/users?rateLimit=500-1000
GET /api/mock/users?rateLimit=2000-4000
```

---

## API Reference

### Query Parameters

| Parameter | Type | Description | Example |
|-----------|------|-------------|---------|
| `n` | integer | Number of completions to generate | `?n=5` |
| `rateLimit` | string | Delay range override | `?rateLimit=500-2000` |
| `strategy` | enum | Batching strategy override | `?strategy=parallel` |

### Request Headers

| Header | Type | Description | Example |
|--------|------|-------------|---------|
| `X-Rate-Limit-Delay` | string | Delay range override | `X-Rate-Limit-Delay: 1000-3000` |
| `X-Rate-Limit-Strategy` | enum | Strategy override | `X-Rate-Limit-Strategy: sequential` |

### Response Headers

| Header | Type | Description |
|--------|------|-------------|
| `X-RateLimit-Limit` | integer | Max requests per minute |
| `X-RateLimit-Remaining` | integer | Requests remaining |
| `X-RateLimit-Reset` | integer | Unix timestamp for reset |
| `X-LLMApi-Request-Time` | integer | LLM processing time (ms) |
| `X-LLMApi-Avg-Time` | integer | Endpoint average time (ms) |
| `X-LLMApi-Total-Elapsed` | integer | Total response time (ms) |
| `X-LLMApi-Delay-Applied` | integer | Artificial delay applied (ms) |

### Configuration Schema

```typescript
interface RateLimitConfig {
  EnableRateLimiting: boolean;           // default: false
  RateLimitDelayRange?: string | null;  // "min-max", "max", "avg", or null
  RateLimitStrategy: "Auto" | "Sequential" | "Parallel" | "Streaming";
  EnableRateLimitStatistics: boolean;    // default: true
  RateLimitStatsWindowSize: number;      // default: 10
}
```

---

## Advanced Scenarios

### Dynamic Rate Limit Adjustment

Test how your app adapts to changing rate limits:

```bash
# Start with generous limits
curl "/api/mock/users?n=5&rateLimit=100-500"

# Gradually increase pressure
curl "/api/mock/users?n=5&rateLimit=500-1500"
curl "/api/mock/users?n=5&rateLimit=1500-3000"

# Simulate rate limit exhaustion
curl "/api/mock/users?n=5&rateLimit=5000-10000"
```

### Load Testing with Rate Limits

Combine with tools like Apache Bench:

```bash
# Test sustained load with rate limiting
ab -n 100 -c 10 "http://localhost:5116/api/mock/users?n=2&rateLimit=500-1000"
```

### Mixed Strategy Testing

Different strategies for different endpoints:

```bash
# Fast completions for critical data
curl "/api/mock/orders?n=3&strategy=parallel"

# Sequential for less critical data
curl "/api/mock/analytics?n=10&strategy=sequential&rateLimit=1000-2000"
```

---

## Performance Considerations

### Memory Usage

- **Statistics tracking**: Each endpoint maintains a queue of recent response times (default: 10 entries)
- **N-completions**: Memory scales linearly with `n` (each completion held in memory)
- **Recommendation**: For `n > 20`, consider using `Streaming` strategy

### CPU Impact

- **Sequential**: Low CPU, one request at a time
- **Parallel**: High CPU burst during concurrent execution
- **Streaming**: Moderate CPU, balanced approach
- **Auto**: Automatically optimizes based on batch size

### Network Considerations

All batching strategies use the same total bandwidth (n requests to LLM), but differ in timing:

- **Sequential**: Bandwidth spread over longer time period
- **Parallel**: Bandwidth burst at start, staggered delivery
- **Streaming**: Balanced bandwidth usage over time

---

## Troubleshooting

### Rate Limiting Not Working

**Problem:** Delays not being applied

**Checklist:**
1. ✅ `EnableRateLimiting: true` in config
2. ✅ `RateLimitDelayRange` is not `null`
3. ✅ `n > 1` in request (single requests don't apply delays by default)
4. ✅ Check logs for any errors

### Unexpected Response Format

**Problem:** Getting wrapped response when expecting single object

**Solution:** N-completions (n>1) always return the wrapped format. For single completions, omit `n` or use `n=1`.

### Statistics Not Tracking

**Problem:** `X-LLMApi-Avg-Time` header missing

**Checklist:**
1. ✅ `EnableRateLimitStatistics: true`
2. ✅ Make multiple requests to same endpoint (stats need data)
3. ✅ Check that endpoint path is consistent (query params don't affect path tracking)

### Performance Issues

**Problem:** Slow responses with large `n` values

**Solutions:**
1. Use `strategy=parallel` for faster completion
2. Reduce `n` to manageable size (n ≤ 10 recommended)
3. Increase `MaxContextWindow` and `TimeoutSeconds` in config
4. Consider using faster LLM model

---

## Migration Guide

### From v2.0 to v2.1

No breaking changes! Rate limiting is disabled by default.

**To enable:**

```json
{
  "MockLlmApi": {
    "EnableRateLimiting": true,
    "RateLimitDelayRange": "500-2000"
  }
}
```

### Backward Compatibility

- All existing endpoints work unchanged
- New headers added to all responses (minimal overhead)
- Configuration is purely additive

---

## Examples Repository

See the `LLMApi/LLMApi.http` file for complete examples of all rate limiting and batching scenarios.

## Related Documentation

- [Main README](../README.md) - Project overview
- [Multiple LLM Backends](./MULTIPLE_LLM_BACKENDS.md) - Backend configuration
- [Error Simulation](../CLAUDE.md#error-simulation) - Error handling features

---

## Feedback & Support

Found a bug or have a feature request?
Open an issue: https://github.com/scottgal/mostlylucid.mockllmapi/issues

## License

This feature is part of MockLLMApi and is released under the Unlicense.

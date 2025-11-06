# SSE Streaming Modes

LLMock API supports three distinct **Server-Sent Events (SSE)** streaming modes, each designed to emulate different real-world API streaming patterns. This allows you to test your SSE clients against various streaming behaviors without needing multiple backend services.

## Overview

| Mode | Use Case | Event Format | Best For |
|------|----------|--------------|----------|
| **LlmTokens** | AI Chat Interfaces | Token-by-token text streaming | Testing chatbot UIs, LLM apps |
| **CompleteObjects** | REST API Feeds | Complete JSON objects per event | Twitter/X API, stock tickers, news feeds |
| **ArrayItems** | Paginated Results | Array items with metadata | Bulk exports, search results, large datasets |

## Mode Details

### 1. LlmTokens Mode (Default)

**Purpose:** Emulates Large Language Model (LLM) streaming behavior where text is generated token-by-token.

**Event Format:**
```
data: {"chunk":"{","accumulated":"{","done":false}

data: {"chunk":"\"id\"","accumulated":"{\"id\"","done":false}

data: {"chunk":":","accumulated":"{\"id\":","done":false}

data: {"chunk":"123","accumulated":"{\"id\":123","done":false}

data: {"content":"{\"id\":123,\"name\":\"John\"}","done":true}
```

**Fields:**
- `chunk`: The new text token/fragment
- `accumulated`: Full text built up so far
- `done`: Boolean indicating if streaming is complete
- `content`: (final event only) Complete generated text

**Use Cases:**
- Testing AI chatbot user interfaces
- LLM-powered applications (ChatGPT-style)
- Progressive text generation displays
- Token-by-token animation

**Example Request:**
```http
GET /api/mock/stream/chat?shape={"message":"Hello!"}
# OR
GET /api/mock/stream/chat?sseMode=LlmTokens&shape={"message":"Hello!"}
```

**Client Code:**
```javascript
const eventSource = new EventSource('/api/mock/stream/chat?shape={"message":"Hello"}');

eventSource.onmessage = (event) => {
    const data = JSON.parse(event.data);

    if (data.done) {
        console.log('Final content:', data.content);
        eventSource.close();
    } else {
        // Append chunk to display
        displayArea.textContent += data.chunk;
    }
};
```

---

### 2. CompleteObjects Mode

**Purpose:** Emulates realistic REST API streaming where complete, independent JSON objects are sent as separate events.

**Event Format:**
```
data: {"data":{"id":1,"name":"Alice","email":"alice@example.com"},"index":0,"total":3,"done":false}

data: {"data":{"id":2,"name":"Bob","email":"bob@example.com"},"index":1,"total":3,"done":false}

data: {"data":{"id":3,"name":"Charlie","email":"charlie@example.com"},"index":2,"total":3,"done":true}
```

**Fields:**
- `data`: The complete JSON object for this event
- `index`: Zero-based index of this object
- `total`: Total number of objects in the stream
- `done`: Boolean indicating if this is the last event

**Use Cases:**
- Real-time social media feeds (Twitter/X streaming API)
- Stock market tickers
- Live sports scores
- News/alert streams
- IoT sensor data streams
- Real-time notifications
- Application log streaming

**Example Request:**
```http
GET /api/mock/stream/users?sseMode=CompleteObjects&shape={"users":[{"id":1,"name":"string","email":"string"}]}
```

**Client Code:**
```javascript
const eventSource = new EventSource('/api/mock/stream/users?sseMode=CompleteObjects');

eventSource.onmessage = (event) => {
    const response = JSON.parse(event.data);

    if (response.done) {
        console.log('Stream complete!');
        eventSource.close();
    } else {
        const user = response.data;
        console.log(`User ${response.index + 1}/${response.total}:`, user);
        // Add user to UI list
        userList.appendChild(createUserElement(user));
    }
};
```

**Real-World Example: Stock Ticker**
```http
GET /api/mock/stream/stocks?sseMode=CompleteObjects&shape={"ticker":"AAPL","price":150.25,"change":2.5,"volume":1000000}
```

Response stream:
```
data: {"data":{"ticker":"AAPL","price":150.32,"change":2.57,"volume":1023450},"index":0,"total":100,"done":false}

data: {"data":{"ticker":"AAPL","price":150.45,"change":2.70,"volume":1045230},"index":1,"total":100,"done":false}
```

---

### 3. ArrayItems Mode

**Purpose:** Emulates paginated API responses being streamed one item at a time with rich metadata.

**Event Format:**
```
data: {"item":{"id":1,"name":"Alice"},"index":0,"total":100,"arrayName":"users","hasMore":true,"done":false}

data: {"item":{"id":2,"name":"Bob"},"index":1,"total":100,"arrayName":"users","hasMore":true,"done":false}

data: {"item":{"id":100,"name":"Zara"},"index":99,"total":100,"arrayName":"users","hasMore":false,"done":true}
```

**Fields:**
- `item`: The array element for this event
- `index`: Zero-based index in the array
- `total`: Total items in the array
- `arrayName`: Name of the array property (if detected)
- `hasMore`: Boolean indicating if more items follow
- `done`: Boolean indicating if this is the last item

**Use Cases:**
- Large result set pagination
- Bulk data exports (customers, orders, products)
- Search results streaming
- Database query results
- File listings
- Audit log exports

**Example Request:**
```http
GET /api/mock/stream/search?sseMode=ArrayItems&shape={"results":[{"id":"string","title":"string","score":0.95}]}
```

**Client Code:**
```javascript
const eventSource = new EventSource('/api/mock/stream/search?sseMode=ArrayItems');
const results = [];

eventSource.onmessage = (event) => {
    const response = JSON.parse(event.data);

    results.push(response.item);
    updateProgressBar(response.index + 1, response.total);

    if (response.done) {
        console.log(`Received all ${response.total} ${response.arrayName}:`, results);
        eventSource.close();
    } else if (response.hasMore) {
        console.log(`Loaded ${response.index + 1}/${response.total} items...`);
    }
};
```

**Real-World Example: Bulk Export**
```http
GET /api/mock/stream/export-customers?sseMode=ArrayItems&shape={"customers":[{"id":"string","name":"string","email":"string"}]}
```

---

## Configuration

### Global Default (appsettings.json)

Set the default SSE mode for all streaming requests:

```json
{
  "MockLlmApi": {
    "SseMode": "CompleteObjects",
    "StreamingChunkDelayMinMs": 100,
    "StreamingChunkDelayMaxMs": 500
  }
}
```

**Options:**
- `"LlmTokens"` - Token-by-token streaming (default, backward compatible)
- `"CompleteObjects"` - Complete objects per event
- `"ArrayItems"` - Array items with metadata

### Per-Request Override (Query Parameter)

Override the default mode for specific requests:

```http
GET /api/mock/stream/users?sseMode=CompleteObjects
GET /api/mock/stream/products?sseMode=ArrayItems
GET /api/mock/stream/chat?sseMode=LlmTokens
```

**Case-Insensitive:** `?sseMode=completeobjects` works too!

### Environment Variable

```bash
# Linux/macOS/Windows
export MockLlmApi__SseMode="CompleteObjects"
set MockLlmApi__SseMode=CompleteObjects
$env:MockLlmApi__SseMode="CompleteObjects"

# Docker
docker run -e MockLlmApi__SseMode=CompleteObjects myapp

# Kubernetes
env:
  - name: MockLlmApi__SseMode
    value: "CompleteObjects"
```

---

## Comparison Chart

### Event Count

For a shape generating 10 users:

| Mode | Events Sent | Event Type |
|------|-------------|------------|
| LlmTokens | 50-200+ | Text fragments |
| CompleteObjects | 10 | Complete user objects |
| ArrayItems | 10 | User objects with metadata |

### Data Per Event

| Mode | Typical Size | Parsing Complexity |
|------|--------------|-------------------|
| LlmTokens | 1-20 bytes | Medium (accumulate) |
| CompleteObjects | 100-500 bytes | Low (complete JSON) |
| ArrayItems | 150-600 bytes | Low (complete JSON + metadata) |

### Best Use Cases Summary

```
┌─────────────────────────────────────────────────────────────┐
│ Need to test...                │ Use this mode              │
├────────────────────────────────┼────────────────────────────┤
│ AI chatbot UI                  │ LlmTokens                  │
│ Token animation                │ LlmTokens                  │
│ Progressive text display       │ LlmTokens                  │
├────────────────────────────────┼────────────────────────────┤
│ Twitter/X API client           │ CompleteObjects            │
│ Stock ticker display           │ CompleteObjects            │
│ Real-time notifications        │ CompleteObjects            │
│ Live feed/timeline             │ CompleteObjects            │
│ IoT sensor dashboard           │ CompleteObjects            │
├────────────────────────────────┼────────────────────────────┤
│ Search results streaming       │ ArrayItems                 │
│ Bulk data export               │ ArrayItems                 │
│ Paginated API responses        │ ArrayItems                 │
│ Large dataset processing       │ ArrayItems                 │
│ Progress-tracked operations    │ ArrayItems                 │
└────────────────────────────────┴────────────────────────────┘
```

---

## Advanced Examples

### Combining with Backend Selection

```http
# Use fast local model for simple data
GET /api/mock/stream/sensors?sseMode=CompleteObjects&backend=ollama-llama3

# Use Mistral-Nemo 128k for massive datasets
GET /api/mock/stream/bulk-export?sseMode=ArrayItems&backend=ollama-mistral-nemo
```

### Combining with Context Tracking

```http
# Maintain consistency across stream sessions
GET /api/mock/stream/users?sseMode=CompleteObjects&contextName=user-session-123
```

### Combining with Streaming Delays

Configure realistic network latency:

```json
{
  "MockLlmApi": {
    "SseMode": "CompleteObjects",
    "StreamingChunkDelayMinMs": 50,
    "StreamingChunkDelayMaxMs": 200
  }
}
```

### Error Handling

All modes support error simulation:

```http
# Error via query parameter
GET /api/mock/stream/users?sseMode=CompleteObjects&error=503

# Error via header
GET /api/mock/stream/data?sseMode=ArrayItems
X-Error-Code: 429
X-Error-Message: Rate limit exceeded
```

---

## Migration Guide

### Upgrading from Previous Versions

The default mode is `LlmTokens` for **backward compatibility**. Existing applications continue to work without changes.

**Before (v1.7.x):**
```http
GET /api/mock/stream/users
# Always used token-by-token streaming
```

**After (v1.8.0+):**
```http
# Same behavior (LlmTokens is default)
GET /api/mock/stream/users

# Or explicitly specify for clarity
GET /api/mock/stream/users?sseMode=LlmTokens
```

### Switching to Realistic Mode

**Step 1:** Test with query parameter:
```http
GET /api/mock/stream/users?sseMode=CompleteObjects
```

**Step 2:** If it works, update global default:
```json
{
  "MockLlmApi": {
    "SseMode": "CompleteObjects"
  }
}
```

**Step 3:** Update client code to handle new event format (see examples above).

---

## Testing Matrix

Verify your SSE client handles all modes correctly:

| Test Case | Mode | Expected Behavior |
|-----------|------|-------------------|
| Basic streaming | All | Events arrive incrementally |
| Parse events | CompleteObjects, ArrayItems | JSON.parse() succeeds on each event |
| Accumulate text | LlmTokens | Progressive text building works |
| Detect completion | All | `done=true` triggers cleanup |
| Handle errors | All | Error events handled gracefully |
| Network interruption | All | Reconnection logic works |
| Large datasets | ArrayItems | Progress tracking accurate |

---

## Troubleshooting

### Events Not Arriving

**Problem:** No SSE events received

**Solutions:**
1. Verify `Accept: text/event-stream` header (usually automatic with EventSource)
2. Check browser console for CORS errors
3. Ensure endpoint starts with `/api/mock/stream/`
4. Verify SSE mode is valid: `LlmTokens`, `CompleteObjects`, or `ArrayItems`

### Parse Errors with CompleteObjects/ArrayItems

**Problem:** `JSON.parse()` fails on event data

**Solutions:**
1. Check `data.data` or `data.item` properties exist
2. Verify shape parameter generates valid JSON
3. Enable verbose logging: `"EnableVerboseLogging": true`

### Too Many Events with LlmTokens

**Problem:** Hundreds of events for small responses

**Solution:** Switch to `CompleteObjects` or `ArrayItems` mode for non-LLM use cases

### Wrong Event Format

**Problem:** Expecting complete objects but getting text fragments

**Solution:** Check `SseMode` configuration - it may still be set to `LlmTokens` (default)

---

## References

- [SSE Specification](https://html.spec.whatwg.org/multipage/server-sent-events.html)
- [EventSource API (MDN)](https://developer.mozilla.org/en-US/docs/Web/API/EventSource)
- [Configuration Reference](./CONFIGURATION_REFERENCE.md)
- [HTTP Examples](../LLMApi/SSE_Streaming.http)
- [Multiple LLM Backends](./MULTIPLE_LLM_BACKENDS.md)

---

## Summary

LLMock API's three SSE modes provide comprehensive testing coverage:

- ✅ **LlmTokens**: Test AI chat interfaces with realistic token-by-token streaming
- ✅ **CompleteObjects**: Test REST API clients with realistic object-per-event streaming
- ✅ **ArrayItems**: Test paginated results with rich metadata

Choose the mode that matches your target API's behavior, or test all three to ensure your client handles various streaming patterns correctly!

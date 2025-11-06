# Multiple LLM Backend Configuration

**Version:** 1.8.0+
**Status:** Production Ready

## Overview

The Mock LLM API supports connecting to multiple LLM backends simultaneously, allowing you to:

- Use different LLM providers (Ollama, OpenAI, LM Studio) in the same application
- Route requests to specific backends via headers or query parameters
- Configure multiple instances of the same provider
- Maintain backward compatibility with legacy single-backend configuration

## Supported Providers

| Provider | Description | Authentication | Endpoint Format |
|----------|-------------|----------------|-----------------|
| **ollama** | Local LLM server (default) | Optional | `http://localhost:11434/v1/` |
| **openai** | Official OpenAI API | Required (API key) | `https://api.openai.com/v1/` |
| **lmstudio** | Local LM Studio server | Optional | `http://localhost:1234/v1/` |

## Configuration Examples

### Option 1: Legacy Configuration (Backward Compatible)

**No changes required!** Your existing configuration continues to work:

```json
{
  "LLMockApi": {
    "BaseUrl": "http://localhost:11434/v1/",
    "ModelName": "llama3",
    "Temperature": 1.2,
    "TimeoutSeconds": 30
  }
}
```

This automatically creates a single Ollama backend with the specified settings.

### Option 2: Single Backend (New Format)

Configure a single backend explicitly:

```json
{
  "LLMockApi": {
    "Temperature": 1.2,
    "TimeoutSeconds": 30,
    "Backends": [
      {
        "Name": "local-ollama",
        "Provider": "ollama",
        "BaseUrl": "http://localhost:11434/v1/",
        "ModelName": "llama3",
        "Enabled": true,
        "Weight": 1
      }
    ]
  }
}
```

### Option 3: Multiple Backends (Recommended)

Configure multiple backends with different providers:

```json
{
  "LLMockApi": {
    "Temperature": 1.2,
    "TimeoutSeconds": 30,
    "EnableRetryPolicy": true,
    "MaxRetryAttempts": 3,
    "EnableCircuitBreaker": true,
    "CircuitBreakerFailureThreshold": 5,
    "Backends": [
      {
        "Name": "ollama-llama3",
        "Provider": "ollama",
        "BaseUrl": "http://localhost:11434/v1/",
        "ModelName": "llama3",
        "Enabled": true,
        "Weight": 1
      },
      {
        "Name": "ollama-mistral",
        "Provider": "ollama",
        "BaseUrl": "http://localhost:11434/v1/",
        "ModelName": "mistral",
        "Enabled": true,
        "Weight": 1
      },
      {
        "Name": "openai-gpt4",
        "Provider": "openai",
        "BaseUrl": "https://api.openai.com/v1/",
        "ModelName": "gpt-4",
        "ApiKey": "sk-your-api-key-here",
        "Enabled": true,
        "Weight": 1
      },
      {
        "Name": "lmstudio-local",
        "Provider": "lmstudio",
        "BaseUrl": "http://localhost:1234/v1/",
        "ModelName": "local-model",
        "Enabled": true,
        "Weight": 1
      }
    ]
  }
}
```

## Backend Configuration Properties

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `Name` | string | Yes | Unique identifier for this backend (used for per-request selection) |
| `Provider` | string | Yes | Provider type: "ollama", "openai", or "lmstudio" |
| `BaseUrl` | string | Yes | Full base URL including `/v1/` suffix |
| `ModelName` | string | Yes | Model identifier (e.g., "llama3", "gpt-4", "mistral") |
| `ApiKey` | string | No | API key (required for OpenAI, optional for others) |
| `Enabled` | boolean | No | Whether this backend is active (default: true) |
| `Weight` | int | No | Load balancing weight (default: 1, higher = more requests) |
| `MaxTokens` | int | No | Maximum output tokens for this backend (overrides global setting) |

## Per-Request Backend Selection

### Using HTTP Header

Specify which backend to use via the `X-LLM-Backend` header:

```http
GET /api/mock/users?count=5
X-LLM-Backend: openai-gpt4
```

### Using Query Parameter

Specify which backend to use via the `backend` query parameter:

```http
GET /api/mock/users?count=5&backend=ollama-mistral
```

### Example: Switching Between Providers

```http
### Request 1: Use Ollama Llama3
GET http://localhost:5116/api/mock/products?count=3
X-LLM-Backend: ollama-llama3

### Request 2: Use OpenAI GPT-4
GET http://localhost:5116/api/mock/products?count=3
X-LLM-Backend: openai-gpt4

### Request 3: Use LM Studio
GET http://localhost:5116/api/mock/products?count=3
X-LLM-Backend: lmstudio-local

### Request 4: Use Query Parameter
GET http://localhost:5116/api/mock/products?count=3&backend=ollama-mistral
```

**Priority:** If both header and query parameter are specified, the header takes precedence.

## Tuning Per-Backend Token Limits

Different models have different token capabilities. Use the `MaxTokens` property to tune each backend:

### Example: Mixed Token Limits

```json
{
  "LLMockApi": {
    "Backends": [
      {
        "Name": "openai-gpt4-turbo",
        "Provider": "openai",
        "BaseUrl": "https://api.openai.com/v1/",
        "ModelName": "gpt-4-turbo",
        "ApiKey": "sk-...",
        "MaxTokens": 32768,
        "Enabled": true
      },
      {
        "Name": "openai-gpt35",
        "Provider": "openai",
        "BaseUrl": "https://api.openai.com/v1/",
        "ModelName": "gpt-3.5-turbo",
        "ApiKey": "sk-...",
        "MaxTokens": 4096,
        "Enabled": true
      },
      {
        "Name": "ollama-llama3-8b",
        "Provider": "ollama",
        "BaseUrl": "http://localhost:11434/v1/",
        "ModelName": "llama3",
        "MaxTokens": 8192,
        "Enabled": true
      },
      {
        "Name": "ollama-mistral",
        "Provider": "ollama",
        "BaseUrl": "http://localhost:11434/v1/",
        "ModelName": "mistral",
        "MaxTokens": 8192,
        "Enabled": true
      }
    ]
  }
}
```

### Common Model Token Limits

| Model | Provider | Typical MaxTokens |
|-------|----------|-------------------|
| GPT-4 Turbo | OpenAI | 32768 |
| GPT-4 | OpenAI | 8192 |
| GPT-3.5 Turbo | OpenAI | 4096 |
| Llama 3 8B | Ollama | 8192 |
| Llama 3 70B | Ollama | 8192 |
| Mistral 7B | Ollama | 8192 |
| Mixtral 8x7B | Ollama | 32768 |
| CodeLlama | Ollama | 16384 |

**Note:** If `MaxTokens` is not set on a backend, it uses the global `MaxOutputTokens` from `LLMockApiOptions`.

## Provider-Specific Configuration

### Ollama Provider

**Default provider** - runs locally without API key:

```json
{
  "Name": "my-ollama",
  "Provider": "ollama",
  "BaseUrl": "http://localhost:11434/v1/",
  "ModelName": "llama3",
  "Enabled": true
}
```

**Features:**
- No authentication required (optional API key supported)
- Supports streaming responses
- Supports n-completions (multiple responses)
- OpenAI-compatible API format

### OpenAI Provider

**Official OpenAI API** - requires API key:

```json
{
  "Name": "my-openai",
  "Provider": "openai",
  "BaseUrl": "https://api.openai.com/v1/",
  "ModelName": "gpt-4",
  "ApiKey": "sk-your-api-key-here",
  "Enabled": true
}
```

**Features:**
- API key **required** (throws exception if missing)
- Full streaming support
- Supports n-completions
- Official OpenAI endpoint

**Important:** Keep API keys secure! Use environment variables or user secrets:

```bash
# In production, use environment variables
export LLMockApi__Backends__0__ApiKey="sk-your-key-here"
```

### LM Studio Provider

**Local LM Studio server** - OpenAI-compatible:

```json
{
  "Name": "my-lmstudio",
  "Provider": "lmstudio",
  "BaseUrl": "http://localhost:1234/v1/",
  "ModelName": "local-model",
  "Enabled": true
}
```

**Features:**
- No authentication required (optional API key supported)
- OpenAI-compatible API
- Supports any model loaded in LM Studio
- Good for testing with local models

## Backward Compatibility

### Legacy Configuration Still Works

✅ **Your old configs work without changes!**

```json
{
  "LLMockApi": {
    "BaseUrl": "http://localhost:11434/v1/",
    "ModelName": "llama3"
  }
}
```

This is automatically converted to:

```json
{
  "LLMockApi": {
    "Backends": [
      {
        "Name": "default",
        "Provider": "ollama",
        "BaseUrl": "http://localhost:11434/v1/",
        "ModelName": "llama3",
        "Enabled": true,
        "Weight": 1
      }
    ]
  }
}
```

### Migration Path

**No breaking changes!** Migrate gradually:

1. **Start:** Keep using legacy config
2. **Step 1:** Add `Backends` array with one backend
3. **Step 2:** Add more backends as needed
4. **Step 3:** Remove legacy `BaseUrl`/`ModelName` when ready

Both formats can coexist during migration.

## Load Balancing & Failover

### Current Behavior

When multiple backends are configured:

1. **Default Selection:** First enabled backend is selected
2. **Per-Request Selection:** Use `X-LLM-Backend` header or `?backend=` parameter
3. **Weight Property:** Reserved for future round-robin implementation

### Future Features (Planned)

- Round-robin load balancing based on `Weight` property
- Automatic failover to healthy backends
- Health checks and backend monitoring
- Request-level timeout configuration

## Resilience & Reliability

### Retry Policy

Applies to **all backends**:

```json
{
  "EnableRetryPolicy": true,
  "MaxRetryAttempts": 3,
  "RetryBaseDelaySeconds": 1
}
```

- Exponential backoff with jitter
- Retries on network errors and timeouts
- Works across all provider types

### Circuit Breaker

Applies to **all backends**:

```json
{
  "EnableCircuitBreaker": true,
  "CircuitBreakerFailureThreshold": 5,
  "CircuitBreakerDurationSeconds": 60
}
```

- Opens after consecutive failures
- Prevents cascading failures
- Automatic recovery testing

## Complete Configuration Example

```json
{
  "LLMockApi": {
    "Temperature": 1.2,
    "TimeoutSeconds": 30,
    "EnableVerboseLogging": false,

    "EnableRetryPolicy": true,
    "MaxRetryAttempts": 3,
    "RetryBaseDelaySeconds": 1,

    "EnableCircuitBreaker": true,
    "CircuitBreakerFailureThreshold": 5,
    "CircuitBreakerDurationSeconds": 60,

    "EnableCaching": true,
    "CacheKeyHashLength": 8,
    "CacheSlidingExpirationMinutes": 15,

    "EnableChunking": true,
    "ChunkSize": 50,

    "Backends": [
      {
        "Name": "ollama-llama3",
        "Provider": "ollama",
        "BaseUrl": "http://localhost:11434/v1/",
        "ModelName": "llama3",
        "MaxTokens": 8192,
        "Enabled": true,
        "Weight": 3,
        "Description": "Primary local model for most requests"
      },
      {
        "Name": "ollama-mistral",
        "Provider": "ollama",
        "BaseUrl": "http://localhost:11434/v1/",
        "ModelName": "mistral",
        "MaxTokens": 8192,
        "Enabled": true,
        "Weight": 2,
        "Description": "Faster alternative for simple requests"
      },
      {
        "Name": "openai-gpt4",
        "Provider": "openai",
        "BaseUrl": "https://api.openai.com/v1/",
        "ModelName": "gpt-4",
        "ApiKey": "sk-your-api-key-here",
        "MaxTokens": 8192,
        "Enabled": false,
        "Weight": 1,
        "Description": "Cloud fallback (disabled by default due to cost)"
      },
      {
        "Name": "lmstudio-experimental",
        "Provider": "lmstudio",
        "BaseUrl": "http://localhost:1234/v1/",
        "ModelName": "experimental-model",
        "MaxTokens": 8192,
        "Enabled": true,
        "Weight": 1,
        "Description": "Testing new models"
      }
    ]
  }
}
```

## Logging

Backend selection is logged for debugging:

```
[Information] Using requested backend: openai-gpt4 (openai, https://api.openai.com/v1/)
[Information] Selected backend: ollama-llama3 (ollama, http://localhost:11434/v1/)
```

Enable verbose logging for detailed output:

```json
{
  "EnableVerboseLogging": true
}
```

## Troubleshooting

### "No LLM backend available"

**Cause:** No backends configured or all backends disabled

**Solution:**
- Check `Backends` array is not empty
- Verify at least one backend has `"Enabled": true`
- If using legacy config, check `BaseUrl` is set

### "Unknown provider 'xxx', falling back to ollama"

**Cause:** Invalid provider name in configuration

**Solution:** Use one of: "ollama", "openai", "lmstudio"

### "OpenAI provider requires an API key"

**Cause:** OpenAI backend configured without `ApiKey`

**Solution:** Add `"ApiKey": "sk-your-key"` to the backend config

### Backend not selected via header

**Cause:** Backend name doesn't match or backend is disabled

**Solution:**
- Verify `Name` matches exactly (case-insensitive)
- Check backend has `"Enabled": true`
- Check logs for "Using requested backend" message

## Best Practices

### 1. Use Named Backends

Give backends descriptive names:

```json
{
  "Name": "ollama-llama3-8b",  // Good: Clear and specific
  "Name": "backend1"            // Avoid: Unclear purpose
}
```

### 2. Disable Expensive Backends by Default

```json
{
  "Name": "openai-gpt4",
  "Enabled": false,  // Enable only when needed
  "ApiKey": "..."
}
```

### 3. Use Environment Variables for Secrets

```bash
# Never commit API keys to source control!
export LLMockApi__Backends__0__ApiKey="sk-prod-key"
```

### 4. Test Per-Request Selection

```http
### Verify backend selection works
GET /api/mock/test
X-LLM-Backend: your-backend-name
```

### 5. Monitor Backend Health

Enable logging to track backend selection and failures:

```json
{
  "EnableVerboseLogging": true
}
```

## Migration Examples

### Example 1: Single → Multiple Backends

**Before:**
```json
{
  "BaseUrl": "http://localhost:11434/v1/",
  "ModelName": "llama3"
}
```

**After (gradual migration):**
```json
{
  "BaseUrl": "http://localhost:11434/v1/",
  "ModelName": "llama3",
  "Backends": [
    {
      "Name": "ollama-mistral",
      "Provider": "ollama",
      "BaseUrl": "http://localhost:11434/v1/",
      "ModelName": "mistral",
      "Enabled": true
    }
  ]
}
```

Both backends work! Legacy becomes "default" backend.

### Example 2: Add Cloud Fallback

Keep local primary, add cloud backup:

```json
{
  "Backends": [
    {
      "Name": "local-primary",
      "Provider": "ollama",
      "BaseUrl": "http://localhost:11434/v1/",
      "ModelName": "llama3",
      "Enabled": true,
      "Weight": 10
    },
    {
      "Name": "cloud-fallback",
      "Provider": "openai",
      "BaseUrl": "https://api.openai.com/v1/",
      "ModelName": "gpt-3.5-turbo",
      "ApiKey": "sk-...",
      "Enabled": true,
      "Weight": 1
    }
  ]
}
```

Use header to route to cloud when needed:
```http
X-LLM-Backend: cloud-fallback
```

## See Also

- [Main README](../README.md) - Overview and quick start
- [Chunking and Caching](CHUNKING_AND_CACHING.md) - Request optimization
- [Error Simulation](../README.md#error-simulation) - Testing error handling
- [Release Notes](../RELEASE_NOTES.md) - Version history

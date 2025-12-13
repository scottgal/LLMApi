# Ollama Model Configurations

This document provides recommended configurations for various Ollama models with the LLMock API library (v2.0+).

## Configuration Property Reference

- **ModelName**: The Ollama model identifier
- **Temperature**: Controls response creativity/randomness (0.0-2.0)
  - `0.6-0.8`: More deterministic, structured responses
  - `1.0-1.5`: Balanced creativity and structure (recommended for mock data)
  - `1.5-2.0`: Maximum creativity and variety
- **MaxContextWindow**: Total token budget for input + output combined
  - System automatically allocates 75% for input, 25% for output
  - Replaces the old `MaxInputTokens` and `MaxOutputTokens` settings

## Recommended Models

### Ministral 3B (Best for Most Use Cases - Default)
**Hardware**: 3-6GB GPU | **Context**: 256K tokens | **Quality**: Excellent

```json
{
  "ModelName": "ministral-3b",
  "Temperature": 1.2,
  "MaxContextWindow": 262144
}
```

- **FASTEST** response times for 3B class
- Excellent at following JSON shapes and code structures
- Trained specifically for code/structured data
- **MASSIVE** 256K context window - handles extremely complex nested data
- Minimal hallucinations
- Runs well on low-end machines (3-4GB RAM)
- **Default model for the library**

### Llama 3.2 3B (Alternative)
**Hardware**: 4-8GB GPU | **Context**: 128K tokens | **Quality**: Excellent

```json
{
  "ModelName": "llama3.2:3b",
  "Temperature": 1.2,
  "MaxContextWindow": 131072
}
```

- Fast, high-quality responses
- Excellent at following JSON shapes
- Great for chunking large requests
- Large context window for complex nested data

### Llama 3 / Llama 3.1 8B (Production Recommended)
**Hardware**: 8-16GB GPU | **Context**: 8K tokens | **Quality**: Excellent

```json
{
  "ModelName": "llama3",
  "Temperature": 1.2,
  "MaxContextWindow": 8192
}
```

- Best balance of speed and quality
- Excellent instruction following
- Good for production demos
- Stable and reliable

### Mistral Nemo 12B (High Quality)
**Hardware**: 16GB+ GPU | **Context**: 128K tokens | **Quality**: Excellent

```json
{
  "ModelName": "mistral-nemo",
  "Temperature": 1.2,
  "MaxContextWindow": 131072
}
```

- Premium quality responses
- Excellent at complex nested structures
- Best for showcasing library capabilities
- Large context window

### Gemma 2 4B (GPU Budget-Friendly)
**Hardware**: 6-8GB GPU | **Context**: 8K tokens | **Quality**: Very Good

```json
{
  "ModelName": "gemma2:4b",
  "Temperature": 1.2,
  "MaxContextWindow": 8192
}
```

- Great for lower-end GPUs
- Fast responses
- Good quality mock data
- Efficient memory usage

### TinyLlama 1.1B (Speed Optimized)
**Hardware**: 2-4GB GPU | **Context**: 2K tokens | **Quality**: Basic

```json
{
  "ModelName": "tinyllama",
  "Temperature": 0.8,
  "MaxContextWindow": 2048
}
```

- Fastest response times
- Minimal hardware requirements
- Basic but functional mock data
- Good for development/testing
- Lower temperature recommended for better structure

## Specialized Models

### Qwen 2.5 14B (Complex Data - Legacy)
**Hardware**: 16GB+ GPU | **Context**: 128K tokens | **Quality**: Excellent

```json
{
  "ModelName": "qwen2.5:14b",
  "Temperature": 1.2,
  "MaxContextWindow": 131072
}
```

- Excellent for complex GraphQL schemas
- Great at nested object structures
- Strong multilingual support
- **Note**: Consider Ministral 3B for better performance

### Mistral 7B (Classic Choice)
**Hardware**: 8-12GB GPU | **Context**: 8K tokens | **Quality**: Excellent

```json
{
  "ModelName": "mistral",
  "Temperature": 1.2,
  "MaxContextWindow": 8192
}
```

- Reliable and stable
- Well-tested and documented
- Good for production environments

### Phi-3 3.8B (Microsoft)
**Hardware**: 6-8GB GPU | **Context**: 4K tokens | **Quality**: Very Good

```json
{
  "ModelName": "phi3",
  "Temperature": 1.2,
  "MaxContextWindow": 4096
}
```

- Compact and efficient
- Good instruction following
- Suitable for smaller datasets

## Temperature Guidelines

**For Mock API Data:**
- **1.0-1.5**: Recommended for varied, realistic data
- **0.6-0.9**: More structured, deterministic responses
- **1.5-2.0**: Maximum creativity for diverse datasets

**For Specific Use Cases:**
- **REST APIs**: 1.2 (balanced variety)
- **GraphQL**: 1.0-1.3 (structured but creative)
- **Chunking**: 1.0-1.2 (better instruction following)
- **SignalR Streaming**: 1.3-1.5 (varied real-time data)

## Context Window Sizing

The `MaxContextWindow` setting controls the total token budget. The system automatically allocates:
- **75% for input**: Prompt, shape, context history, examples
- **25% for output**: Generated JSON response

### Common Configurations:

| Use Case | Recommended Window | Notes |
|----------|-------------------|-------|
| Simple REST endpoints | 4096 | Basic CRUD operations |
| Complex nested data | 8192-16384 | GraphQL, nested objects |
| Large arrays (chunking) | 8192+ | Automatic chunking enabled |
| Continuous streaming | 4096-8192 | Real-time data generation |
| Multiple contexts | 16384+ | Complex API specifications |

## Multi-Backend Configuration (v1.8.0+)

You can configure multiple models/backends simultaneously:

```json
{
  "Backends": [
    {
      "Name": "fast",
      "Provider": "ollama",
      "BaseUrl": "http://localhost:11434/v1/",
      "ModelName": "tinyllama",
      "MaxTokens": 2048
    },
    {
      "Name": "quality",
      "Provider": "ollama",
      "BaseUrl": "http://localhost:11434/v1/",
      "ModelName": "llama3",
      "MaxTokens": 8192
    },
    {
      "Name": "premium",
      "Provider": "ollama",
      "BaseUrl": "http://localhost:11434/v1/",
      "ModelName": "mistral-nemo",
      "MaxTokens": 131072
    }
  ]
}
```

Then select per-request:
```http
GET /api/mock/users?backend=quality
X-LLM-Backend: premium
```

## Installing Models with Ollama

```bash
# Fast models (< 5 seconds per request)
ollama pull tinyllama
ollama pull llama3.2:3b
ollama pull gemma2:4b

# Balanced models (5-15 seconds per request)
ollama pull llama3
ollama pull mistral

# Premium models (15-30 seconds per request)
ollama pull mistral-nemo
ollama pull qwen2.5:14b
```

## Troubleshooting

**Responses are too similar/repetitive:**
- Increase `Temperature` to 1.3-1.5
- Try a larger model
- Add `randomSeed` to shape (automatic in v2.0+)

**Responses don't follow shape:**
- Decrease `Temperature` to 0.8-1.0
- Try llama3 or mistral (better instruction following)
- Increase `MaxContextWindow` if shape is complex

**Chunking fails with invalid JSON:**
- Use llama3 or llama3.2 (best chunking support)
- Decrease `Temperature` to 1.0-1.2
- Increase `MaxContextWindow` per chunk

**Timeouts or slow responses:**
- Use smaller models (tinyllama, gemma2:4b)
- Reduce `MaxContextWindow`
- Increase `TimeoutSeconds` in configuration
- Check Ollama server performance

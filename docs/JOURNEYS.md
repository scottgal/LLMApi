# Journeys - Multi-Step User Flow Simulation

**Version:** v2.3.0+

Journeys define sequences of API calls that simulate realistic user behavior. Each journey consists of multiple steps, each with its own shape, context hints, and prompt guidance. This system enables LLMs to later decide on journey paths based on their own decisions.

## Table of Contents

- [Overview](#overview)
- [Quick Start](#quick-start)
- [Configuration](#configuration)
- [Management API](#management-api)
- [Journey Templates](#journey-templates)
- [Journey Sessions](#journey-sessions)
- [Prompt Influence](#prompt-influence)
- [Integration](#integration)
- [Examples](#examples)

---

## Overview

### What are Journeys?

Journeys model multi-step user interactions with your API. Instead of treating each API call in isolation, journeys provide:

1. **Sequential Context**: Each step builds on previous steps
2. **Consistent Data**: Shared variables maintain referential integrity across steps
3. **Prompt Guidance**: Per-step hints shape LLM response generation
4. **LLM-Driven Selection**: Future capability for LLMs to autonomously select journeys

### Key Concepts

| Concept | Description |
|---------|-------------|
| **JourneyTemplate** | Defines the structure of a journey (name, steps, modality, prompt hints) |
| **JourneyStepTemplate** | A single step in a journey (method, path, shape, body template) |
| **JourneyInstance** | An active journey for a specific session with resolved variables |
| **JourneyModality** | Classification of journey type (Rest, GraphQL, Auth, Scanner, Other) |
| **PromptHints** | Guidance for LLM response generation at journey and step levels |

### Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    Configuration Layer                       │
│  appsettings.json → JourneysConfig → JourneyTemplateConfig  │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                    Registry Layer                            │
│  JourneyRegistry: Loads, stores, and manages templates       │
│  - GetJourney(name)                                          │
│  - GetJourneysByModality(modality)                          │
│  - SelectRandomJourney(modality?, weights)                  │
│  - RegisterJourney / RemoveJourney                          │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                    Session Layer                             │
│  JourneySessionManager: Manages active journey instances     │
│  - CreateJourneyInstance(sessionId, template, variables)     │
│  - AdvanceJourney(sessionId)                                │
│  - ResolveStepForRequest(instance, method, path)            │
│  - EndJourney(sessionId)                                    │
└─────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│                    Prompt Layer                              │
│  JourneyPromptInfluencer: Builds prompt influence data       │
│  - BuildJourneyPromptInfluence(instance, step, context)      │
│  - FormatInfluenceForPrompt(influence)                      │
│  - GenerateRandomnessSeed(session, method, path, step)       │
└─────────────────────────────────────────────────────────────┘
```

---

## Quick Start

### 1. Enable Journeys in Configuration

```json
{
  "MockLlmApi": {
    "Journeys": {
      "Enabled": true,
      "DefaultVariables": {
        "appName": "MyApp"
      },
      "Journeys": [
        {
          "Name": "simple-browse",
          "Modality": "Rest",
          "Weight": 1.0,
          "Steps": [
            {
              "Method": "GET",
              "Path": "/api/products",
              "Description": "Browse products"
            }
          ]
        }
      ]
    }
  }
}
```

### 2. Map Journey Management Endpoints

```csharp
// In Program.cs
app.MapLLMockJourneyManagement("/api/journeys");
```

### 3. Start a Journey via API

```http
POST /api/journeys/sessions/my-session-123/start
Content-Type: application/json

{
  "journeyName": "simple-browse",
  "variables": {
    "userId": "456"
  }
}
```

### 4. Check Session Status

```http
GET /api/journeys/sessions/my-session-123
```

---

## Configuration

### Root Configuration (`JourneysConfig`)

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Enabled` | bool | `false` | Whether the journey system is enabled |
| `DefaultVariables` | Dictionary | `{}` | Variables available to all journeys |
| `Journeys` | List | `[]` | List of journey template configurations |

### Journey Template Configuration (`JourneyTemplateConfig`)

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Name` | string | required | Unique identifier for the journey |
| `Modality` | string | `"Rest"` | Journey type: Rest, GraphQL, Auth, Scanner, Other |
| `Weight` | double | `1.0` | Selection weight for random journey selection |
| `PromptHints` | object | `null` | Journey-level prompt guidance |
| `Steps` | List | required | Ordered list of journey steps |

### Journey Step Configuration (`JourneyStepConfig`)

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Method` | string | `"GET"` | HTTP method |
| `Path` | string | required | URL path (supports `{{variable}}` templates) |
| `ShapeJson` | string | `null` | Expected response shape |
| `BodyTemplateJson` | string | `null` | Request body template (supports `{{variable}}` templates) |
| `Description` | string | `null` | Human-readable step description |
| `PromptHints` | object | `null` | Step-level prompt guidance |

### Prompt Hints Configuration

**Journey-Level (`JourneyPromptHintsConfig`):**

| Property | Type | Description |
|----------|------|-------------|
| `Scenario` | string | High-level scenario description |
| `DataStyle` | string | Data generation style guidance |
| `RiskFlavor` | string | Security/risk aspects to include |
| `RandomnessProfile` | string | Variation control (e.g., "medium-variation") |

**Step-Level (`JourneyStepPromptHintsConfig`):**

| Property | Type | Description |
|----------|------|-------------|
| `HighlightFields` | List | Fields to emphasize in generation |
| `ContextKeys` | List | Context keys relevant to this step |
| `PromoteKeys` | List | Context keys to prioritize (MUST be consistent) |
| `DemoteKeys` | List | Context keys that may vary |
| `LureFields` | List | Tempting but harmless fields to include |
| `Tone` | string | Response tone guidance |
| `RandomnessSeed` | string | Custom seed for deterministic generation |
| `AdditionalInstructions` | string | Free-form LLM instructions |

---

## Management API

All endpoints are available at `/api/journeys` (configurable).

### Template Management

#### List All Templates
```http
GET /api/journeys/templates
```

Response:
```json
{
  "enabled": true,
  "count": 4,
  "templates": [
    {
      "name": "ecommerce-browse-purchase",
      "modality": "Rest",
      "stepCount": 4,
      "weight": 3.0
    }
  ]
}
```

#### Get Template Details
```http
GET /api/journeys/templates/{name}
```

Response:
```json
{
  "name": "ecommerce-browse-purchase",
  "modality": "Rest",
  "weight": 3.0,
  "promptHints": {
    "scenario": "E-commerce shopping session",
    "dataStyle": "Varied product data"
  },
  "steps": [
    {
      "index": 0,
      "method": "GET",
      "path": "/api/mock/products",
      "description": "Browse product catalog",
      "hasShape": true,
      "hasBody": false
    }
  ]
}
```

#### Create Template (Runtime)
```http
POST /api/journeys/templates
Content-Type: application/json

{
  "name": "custom-journey",
  "modality": "Rest",
  "weight": 2.0,
  "steps": [
    {
      "method": "GET",
      "path": "/api/items",
      "description": "Get items"
    }
  ]
}
```

#### Delete Template
```http
DELETE /api/journeys/templates/{name}
```

#### Get Templates by Modality
```http
GET /api/journeys/templates/by-modality/GraphQL
```

### Session Management

#### Start a Journey
```http
POST /api/journeys/sessions/{sessionId}/start
Content-Type: application/json

{
  "journeyName": "ecommerce-browse-purchase",
  "variables": {
    "userId": "123",
    "productId": "456"
  }
}
```

Response:
```json
{
  "sessionId": "session-123",
  "journeyName": "ecommerce-browse-purchase",
  "modality": "Rest",
  "currentStep": 0,
  "totalSteps": 4,
  "isComplete": false,
  "currentStepDetails": {
    "method": "GET",
    "path": "/api/mock/products",
    "description": "Browse product catalog"
  },
  "variables": {
    "sessionId": "session-123",
    "userId": "123",
    "productId": "456"
  }
}
```

#### Start Random Journey
```http
POST /api/journeys/sessions/{sessionId}/start-random
Content-Type: application/json

{
  "modality": "Rest",
  "variables": {}
}
```

#### Get Session Status
```http
GET /api/journeys/sessions/{sessionId}
```

#### Advance Journey
```http
POST /api/journeys/sessions/{sessionId}/advance
```

#### End Journey
```http
DELETE /api/journeys/sessions/{sessionId}
```

### System Status

#### Get System Status
```http
GET /api/journeys/status
```

Response:
```json
{
  "enabled": true,
  "templateCount": 4,
  "templatesByModality": {
    "Rest": 2,
    "GraphQL": 1,
    "Auth": 1
  },
  "modalities": ["Rest", "GraphQL", "Auth", "Scanner", "Other"]
}
```

---

## Journey Templates

### Template Variable Substitution

Use `{{variableName}}` syntax in paths and body templates:

```json
{
  "Steps": [
    {
      "Method": "GET",
      "Path": "/api/users/{{userId}}/orders",
      "BodyTemplateJson": "{\"userId\": \"{{userId}}\", \"filter\": \"{{filter}}\"}"
    }
  ]
}
```

Variables are resolved from:
1. Session-provided variables (highest priority)
2. Default variables from configuration
3. Auto-injected `sessionId` variable

### Modality Types

| Modality | Use Case |
|----------|----------|
| `Rest` | Standard REST API flows |
| `GraphQL` | GraphQL query sequences |
| `Auth` | Authentication/authorization flows |
| `Scanner` | API discovery patterns |
| `Other` | Custom modalities |

### Weighted Selection

When selecting random journeys, weights determine probability:

```json
{
  "Journeys": [
    { "Name": "common-flow", "Weight": 10.0 },
    { "Name": "rare-flow", "Weight": 1.0 }
  ]
}
```

In this example, "common-flow" will be selected ~10x more often than "rare-flow".

---

## Journey Sessions

### Session Lifecycle

```
┌──────────────┐     ┌──────────────┐     ┌──────────────┐
│   Created    │ ──▶ │   Active     │ ──▶ │  Completed   │
│  (Step 0)    │     │  (Step N)    │     │  (All done)  │
└──────────────┘     └──────────────┘     └──────────────┘
       │                    │                    │
       │                    │                    │
       ▼                    ▼                    ▼
   Variables            Advance              Auto-expire
   Resolved             Journey              or EndJourney
```

### Session Storage

Sessions are stored in `IMemoryCache` with sliding expiration (same as `ContextExpirationMinutes`). Sessions auto-expire after inactivity.

### Step Resolution

When a request arrives, `ResolveStepForRequest` matches against:
1. Current step (exact match)
2. Future steps (look-ahead for flexible progression)

Path matching supports wildcards:
- `*` matches any segment (e.g., `/api/*/items`)
- `**` matches any path (e.g., `/api/**`)

---

## Prompt Influence

### Building Prompt Influence

```csharp
var influencer = serviceProvider.GetRequiredService<JourneyPromptInfluencer>();

// Get journey instance for session
var instance = sessionManager.GetJourneyForSession(sessionId);
if (instance?.CurrentStep == null) return;

// Build context snapshot from existing shared data
var contextSnapshot = JourneyPromptInfluencer.BuildContextSnapshot(
    sharedData: existingSharedData,
    promoteKeys: new[] { "userId", "cartId" },
    demoteKeys: new[] { "timestamp" });

// Generate stable seed for reproducibility
var seed = JourneyPromptInfluencer.GenerateRandomnessSeed(
    sessionId, method, path, instance.CurrentStepIndex);

// Build influence
var influence = influencer.BuildJourneyPromptInfluence(
    instance,
    instance.CurrentStep,
    contextSnapshot,
    seed);

// Format for prompt
var promptSection = JourneyPromptInfluencer.FormatInfluenceForPrompt(influence);
```

### Influence Properties

| Property | Source | Description |
|----------|--------|-------------|
| `JourneyName` | Template | Name of the journey |
| `Modality` | Template | Journey type classification |
| `Scenario` | Journey hints | High-level scenario |
| `DataStyle` | Journey hints | Data generation style |
| `RiskFlavor` | Journey hints | Security aspects |
| `RandomnessProfile` | Journey hints | Variation control |
| `StepDescription` | Step | Current step description |
| `Tone` | Step hints | Response tone |
| `RandomnessSeed` | Step hints or generated | Deterministic seed |
| `PromotedContext` | Filtered from context | MUST be consistent |
| `DemotedContext` | Filtered from context | May vary |
| `HighlightFields` | Step hints | Fields to emphasize |
| `LureFields` | Step hints | Tempting fields |
| `RawStepHints` | Step hints | Full hints as JSON |

---

## Integration

### With Request Handlers

```csharp
// In your request handler
public async Task HandleRequest(HttpContext context)
{
    var sessionId = context.Request.Headers["X-Session-Id"].FirstOrDefault();

    if (!string.IsNullOrEmpty(sessionId))
    {
        var instance = _sessionManager.GetJourneyForSession(sessionId);
        if (instance != null)
        {
            var step = _sessionManager.ResolveStepForRequest(
                instance,
                context.Request.Method,
                context.Request.Path);

            if (step != null)
            {
                // Use step.ShapeJson, step.PromptHints, etc.
                // to influence response generation
            }
        }
    }
}
```

### With PromptBuilder

```csharp
// Build prompt with journey influence
var promptBuilder = new StringBuilder();

// Add journey context if available
if (journeyInfluence != null)
{
    promptBuilder.AppendLine("=== Journey Context ===");
    promptBuilder.AppendLine(JourneyPromptInfluencer.FormatInfluenceForPrompt(journeyInfluence));
    promptBuilder.AppendLine();
}

// Continue with regular prompt building...
```

---

## Examples

### E-Commerce Journey

```json
{
  "Name": "ecommerce-checkout",
  "Modality": "Rest",
  "Weight": 3.0,
  "PromptHints": {
    "Scenario": "Customer shopping session",
    "DataStyle": "Realistic e-commerce data",
    "RiskFlavor": "Include payment info",
    "RandomnessProfile": "medium-variation"
  },
  "Steps": [
    {
      "Method": "GET",
      "Path": "/api/products",
      "ShapeJson": "{\"products\":[{\"id\":0,\"name\":\"string\",\"price\":0.0}]}",
      "Description": "Browse products",
      "PromptHints": {
        "HighlightFields": ["name", "price"],
        "Tone": "engaging"
      }
    },
    {
      "Method": "POST",
      "Path": "/api/cart",
      "BodyTemplateJson": "{\"productId\":\"{{productId}}\"}",
      "ShapeJson": "{\"cartId\":\"string\",\"items\":[]}",
      "Description": "Add to cart",
      "PromptHints": {
        "PromoteKeys": ["productId", "cartId"]
      }
    },
    {
      "Method": "POST",
      "Path": "/api/checkout",
      "BodyTemplateJson": "{\"cartId\":\"{{cartId}}\"}",
      "ShapeJson": "{\"orderId\":\"string\",\"total\":0.0}",
      "Description": "Complete checkout",
      "PromptHints": {
        "LureFields": ["paymentToken"],
        "Tone": "confirmation"
      }
    }
  ]
}
```

### GraphQL Journey

```json
{
  "Name": "social-exploration",
  "Modality": "GraphQL",
  "Weight": 2.0,
  "PromptHints": {
    "Scenario": "Social platform browsing",
    "DataStyle": "Rich nested relationships"
  },
  "Steps": [
    {
      "Method": "POST",
      "Path": "/graphql",
      "BodyTemplateJson": "{\"query\":\"{ users { id name } }\"}",
      "Description": "List users"
    },
    {
      "Method": "POST",
      "Path": "/graphql",
      "BodyTemplateJson": "{\"query\":\"{ user(id: {{userId}}) { posts { id title } } }\"}",
      "Description": "View user posts",
      "PromptHints": {
        "ContextKeys": ["userId"]
      }
    }
  ]
}
```

### Authentication Journey

```json
{
  "Name": "auth-flow",
  "Modality": "Auth",
  "Weight": 1.0,
  "PromptHints": {
    "Scenario": "Authentication testing",
    "RiskFlavor": "Include tokens and sessions"
  },
  "Steps": [
    {
      "Method": "POST",
      "Path": "/api/auth/login",
      "BodyTemplateJson": "{\"username\":\"test\",\"password\":\"test\"}",
      "ShapeJson": "{\"token\":\"string\",\"refreshToken\":\"string\"}",
      "Description": "Login",
      "PromptHints": {
        "LureFields": ["internalUserId", "sessionSecret"]
      }
    },
    {
      "Method": "GET",
      "Path": "/api/auth/me",
      "Description": "Get current user",
      "PromptHints": {
        "PromoteKeys": ["userId"]
      }
    }
  ]
}
```

---

## Services Reference

### JourneyRegistry

**Registration:** `services.AddSingleton<JourneyRegistry>()`

| Method | Description |
|--------|-------------|
| `IsEnabled` | Whether journeys are enabled |
| `GetAllJourneys()` | Get all registered templates |
| `GetJourney(name)` | Get template by name |
| `GetJourneysByModality(modality)` | Get templates by modality |
| `SelectRandomJourney(modality?, random?)` | Weighted random selection |
| `RegisterJourney(template)` | Add template programmatically |
| `RemoveJourney(name)` | Remove template |
| `GetJourneySummaries()` | Get template summaries |

### JourneySessionManager

**Registration:** `services.AddScoped<JourneySessionManager>()`

| Method | Description |
|--------|-------------|
| `GetJourneyForSession(sessionId)` | Get active instance |
| `CreateJourneyInstance(sessionId, name/template, variables)` | Create instance |
| `CreateRandomJourneyInstance(sessionId, modality?, variables)` | Create random |
| `AdvanceJourney(sessionId)` | Move to next step |
| `ResolveStepForRequest(instance, method, path)` | Match step to request |
| `EndJourney(sessionId)` | Remove instance |

### JourneyPromptInfluencer

**Registration:** `services.AddScoped<JourneyPromptInfluencer>()`

| Method | Description |
|--------|-------------|
| `BuildJourneyPromptInfluence(instance, step, context, seed)` | Build influence |
| `GenerateRandomnessSeed(sessionId, method, path, stepIndex)` | Generate seed |
| `BuildContextSnapshot(sharedData, promote, demote)` | Build context |
| `FormatInfluenceForPrompt(influence)` | Format for prompt |

---

## Future Enhancements

1. **LLM-Driven Journey Selection**: LLMs autonomously select journeys based on request patterns
2. **Branching Journeys**: Conditional step execution based on responses
3. **Journey Analytics**: Track journey completion rates and patterns
4. **Journey Composition**: Combine smaller journeys into larger flows

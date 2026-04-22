# API Holodeck Packs — Design Spec
**Date:** 2026-04-22  
**Status:** Approved

---

## Overview

Five workstreams to evolve LLMApi into a production-ready **API Holodeck**: a stealth honeypot that fools bots/scanners by serving LLM-generated, realistic API responses indistinguishable from a real service.

Workstreams:
1. **gemma4:4b defaults** — replace llama3 everywhere
2. **API Holodeck Packs** — YAML bundle system for API personas
3. **Prompt breakout hardening** — close sanitization gaps in journey config
4. **Homebrew tap** — `brew install llmock` on macOS
5. **API Holodeck promotion** — README, docs, demo packs

---

## 1. gemma4:4b as Default

Replace all `llama3` references with `gemma4:4b`.

**Files to update:**
- `mostlylucid.mockllmapi/LLMockApiOptions.cs` — `ModelName` default property
- `llmock.cli/Program.cs` — two hardcoded fallback defaults (lines ~179, ~204)
- `llmock.cli/appsettings.json` — backend model config
- `docker-appsettings.json` — Docker model config
- `.env.example` — `MockLlmApi__ModelName`
- `README.md` — all model recommendations, quick-start examples
- `llmock.cli/README.md` — CLI examples
- `llmock.cli/examples.http` — example requests

**Context window config** (in `LLMockApiOptions.cs` or equivalent):
```csharp
// gemma4:4b has 128K context window
"gemma4:4b"  => 128_000,
"gemma4:2b"  => 128_000,
"gemma4:12b" => 128_000,
```

**Why gemma4:4b over 2b:** 2b fails on complex nested JSON graphs. 4b handles them reliably while remaining fast enough for local deployment.

---

## 2. API Holodeck Packs

### 2.1 Concept

A **pack** is a YAML bundle that defines an API persona — the stage the Holodeck performs on. The LLM improvises all data dynamically; packs never contain static values that could be fingerprinted.

A pack defines:
- **API surface**: paths + HTTP methods to expose
- **Prompt personality**: system prompt additions giving the LLM domain vocabulary and style
- **Response shapes**: structural templates (no data values) per path pattern
- **Journey patterns**: how bots typically explore this API type
- **Timing profile**: realistic response delays
- **Model hints**: temperature/max_tokens overrides for this domain

### 2.2 Pack YAML Schema

```yaml
id: wordpress-rest                          # unique identifier
name: WordPress REST API                    # display name
description: Simulates WordPress 6.x REST API v2

prompt_personality: |
  You are a WordPress 6.x REST API. Use WordPress terminology:
  user IDs are integers, post slugs are lowercase-hyphenated,
  dates are ISO 8601. Never reference AI, LLM, or mock data.

api_surface:
  - path: /wp-json/wp/v2/posts
    methods: [GET, POST]
    description: Post listing and creation
  - path: /wp-json/wp/v2/posts/{id}
    methods: [GET, PUT, DELETE]
  - path: /wp-json/wp/v2/users
    methods: [GET]
  - path: /wp-json/wp/v2/users/{id}
    methods: [GET]
  - path: /wp-json/wp/v2/categories
    methods: [GET]
  - path: /wp-json
    methods: [GET]
    description: API root/discovery endpoint

response_shapes:
  - path_pattern: /wp-json/wp/v2/posts
    shape: |
      [{"id":0,"date":"","slug":"","status":"","title":{"rendered":""},"content":{"rendered":""},"author":0}]
  - path_pattern: /wp-json/wp/v2/users/{id}
    shape: |
      {"id":0,"name":"","slug":"","description":"","registered_date":"","roles":[""]}

journey_patterns:
  - name: recon
    description: Initial reconnaissance sweep
    steps:
      - GET /wp-json
      - GET /wp-json/wp/v2
      - GET /wp-json/wp/v2/users
      - GET /wp-json/wp/v2/posts
  - name: user-enum
    description: User enumeration attempt
    steps:
      - GET /wp-json/wp/v2/users?per_page=100
      - GET /wp-json/wp/v2/users/1
      - GET /wp-json/wp/v2/users/2

timing_profile:
  min_ms: 120
  max_ms: 600
  jitter_ms: 80

model_hints:
  temperature: 1.1
  max_tokens: 2048
```

### 2.3 Built-in Packs (shipped as embedded assembly resources)

| Pack ID | Persona | Bot target |
|---------|---------|------------|
| `wordpress-rest` | WordPress 6.x REST API | WP scanners, user enumeration |
| `ecommerce` | Generic shop API (products/orders/customers) | Cart stuffing, price scrapers |
| `banking` | Internal fintech API (accounts/transactions) | Credential stuffing, recon |
| `devops` | CI/CD / internal tooling API | Secret theft, pipeline recon |

### 2.4 Pack Loading Architecture

```
IPackRegistry (interface)
  GetPack(string id) → HoldeckPack?
  GetAllPacks() → IReadOnlyList<HoldeckPack>
  GetActivePack() → HoldeckPack?

PackLoader (static utility)
  LoadFromYaml(string yaml) → HoldeckPack
  LoadEmbedded() → IReadOnlyList<HoldeckPack>     ← assembly resources
  LoadFromDirectory(string path) → IReadOnlyList<HoldeckPack>  ← ~/.llmock/packs/

InMemoryPackRegistry : IPackRegistry
  Constructor: loads embedded + ~/.llmock/packs/ (silently skipped if missing) + any --pack-dir override
  Selection: config → CLI flag → X-Pack header
  No active pack = no persona (raw LLMApi behavior, fully backward compatible)
```

**DI registration** in `ServiceCollectionExtensions`:
```csharp
services.TryAddSingleton<IPackRegistry>(sp => {
    var opts = sp.GetRequiredService<IOptions<LLMockApiOptions>>().Value;
    return new InMemoryPackRegistry(opts.ActivePackId, opts.PackDirectory, logger);
});
```

**Pack selection precedence** (lowest → highest):
1. First enabled pack in config
2. `ActivePackId` in `LLMockApiOptions`
3. `--pack <id>` CLI flag
4. `X-Pack: <id>` request header (per-request override)

### 2.5 Pack Integration Points

**PromptBuilder**: When a pack is active, prepend `prompt_personality` to the system prompt before user request context.

**ShapeExtractor**: When a pack is active and no explicit shape provided, use pack `response_shapes` for the matched path pattern (before autoshape fallback).

**RegularRequestHandler / StreamingRequestHandler**: Apply pack `timing_profile` delay before responding.

**JourneyRegistry**: Pack `journey_patterns` are registered as journey templates (merged with any config-defined journeys).

**LLMockApiOptions** new properties:
```csharp
public string? ActivePackId { get; set; }
public string? PackDirectory { get; set; }  // defaults to ~/.llmock/packs/
```

### 2.6 File Layout

```
mostlylucid.mockllmapi/
  Packs/
    HoldeckPack.cs          ← C# record (maps from YAML)
    PackLoader.cs           ← YAML deserialization + embedded resource loading
    InMemoryPackRegistry.cs ← singleton registry
    IPackRegistry.cs        ← interface
    BuiltIn/
      wordpress-rest.yaml
      ecommerce.yaml
      banking.yaml
      devops.yaml
```

---

## 3. Prompt Breakout Hardening

### 3.1 Gap Analysis

Two paths currently bypass `InputValidationService`:

| Source | Current state | Risk |
|--------|--------------|------|
| `JourneyPromptInfluencer.AdditionalInstructions` | Embedded raw into prompt | Trusted config, but if loaded from user-supplied YAML, could contain delimiters |
| Template variables from user HTTP requests | Resolved via regex replace, no sanitization | Untrusted — direct prompt injection vector |

### 3.2 Fix: Two-tier sanitization

**Tier 1 — `ConfigInputSanitizer`** (new, lightweight): For trusted config sources (pack personality, journey `AdditionalInstructions`, response shapes from pack YAML).
- Strip LLM delimiter sequences: `<`, `>`, `"""`, `---`, `[[`, `]]`, `>>`, backtick-sequences
- No pattern-based injection detection (config is trusted, just neutralize delimiters)
- Applied in `PackLoader` on load and in `JourneyPromptInfluencer` before embedding `AdditionalInstructions`

**Tier 2 — existing `InputValidationService`** (already has 8 patterns + sanitizer): For untrusted user input.
- Wire up template variable resolution in `JourneySessionManager.ResolveTemplate()` to sanitize variable *values* before substitution
- One-line fix: call `_inputValidationService.SanitizeForPrompt(value)` on each variable value before returning from `ResolveTemplate`

### 3.3 Pack personality hardening

Pack `prompt_personality` from embedded resources is trusted. Pack `prompt_personality` from user-supplied YAML in `~/.llmock/packs/` is semi-trusted. Apply `ConfigInputSanitizer` to all pack personalities on load regardless of source.

---

## 4. Homebrew Tap

### 4.1 Approach

Create a **GitHub Homebrew tap** repository: `scottgal/homebrew-llmock`

Formula downloads the correct pre-built binary from GitHub releases (already built by `release-cli.yml`), verifies SHA256, and installs.

```ruby
# Formula/llmock.rb
class Llmock < Formula
  desc "LLMock CLI — API Holodeck powered by local LLMs"
  homepage "https://github.com/scottgal/LLMApi"
  version "X.Y.Z"

  on_macos do
    if Hardware::CPU.arm?
      url "https://github.com/scottgal/LLMApi/releases/download/llmock-vX.Y.Z/llmock-osx-arm64.tar.gz"
      sha256 "..."
    else
      url "https://github.com/scottgal/LLMApi/releases/download/llmock-vX.Y.Z/llmock-osx-x64.tar.gz"
      sha256 "..."
    end
  end

  def install
    bin.install "llmock"
  end

  test do
    system "#{bin}/llmock", "--help"
  end
end
```

**Install UX:**
```bash
brew tap scottgal/llmock
brew install llmock
llmock serve --pack wordpress-rest
```

### 4.2 Automation

Update `release-cli.yml` to add a step after release creation:
- Compute SHA256 of macOS x64 + ARM64 tarballs
- Check out `scottgal/homebrew-llmock` tap repo
- Update `Formula/llmock.rb` with new version + SHA256s
- Commit and push

This requires a `HOMEBREW_TAP_TOKEN` GitHub secret with write access to the tap repo.

---

## 5. API Holodeck Promotion

### 5.1 README additions

New top-level section **"API Holodeck"** in `README.md`:
- What it is: stealth LLM-powered honeypot
- How it works: bots probe, Holodeck responds with convincing generated data, you learn bot behavior
- Quick demo: `llmock serve --pack wordpress-rest` → watch bots enumerate fake WP users
- Pack gallery: table of built-in packs

### 5.2 Dedicated doc

`docs/api-holodeck.md`:
- Architecture overview
- Pack format reference (full YAML schema)
- Writing custom packs
- Prompt personality tips (what makes a convincing persona)
- Analyzing captured journeys to understand bot behavior

### 5.3 Demo HTTP file

`llmock.cli/holodeck-demo.http` — ready-to-run requests hitting a WordPress-rest Holodeck:
```http
### Bot recon sweep
GET http://localhost:5555/wp-json

### User enumeration
GET http://localhost:5555/wp-json/wp/v2/users?per_page=100

### Post scrape
GET http://localhost:5555/wp-json/wp/v2/posts
```

---

## 6. Context Continuity in Packs

### 6.1 Why this matters

The Holodeck must be *consistent*, not just realistic. If a bot fetches `/wp-json/wp/v2/users/1` and gets back `{"id":1,"name":"Alice Johnson","slug":"alice-johnson"}`, any subsequent request referencing user 1 (posts by that author, comments, etc.) must return the same name and slug. Otherwise the bot detects the inconsistency and fingerprints the honeypot.

LLMApi already has an **API context memory** system (`ContextExpirationMinutes`, shared context store) that carries key-value pairs across requests within a session. Packs need to explicitly declare which context keys matter and how to extract them.

### 6.2 Pack context schema additions

```yaml
context_schema:
  # Keys this pack extracts from responses and stores in session context
  - key: user.{id}.name          # dotted path, {id} captures path param
    extract_from: $.name          # JSONPath into the response body
    scope: session                # 'session' = per-bot-session, 'global' = across all sessions
  - key: user.{id}.slug
    extract_from: $.slug
    scope: session
  - key: post.{id}.author_id
    extract_from: $.author
    scope: session

  # Keys to seed at session start (LLM generates once, reused throughout)
  seed_keys:
    - key: site.name              # e.g. "TechBlog Pro"
    - key: site.description
    - key: admin.email
```

### 6.3 How it integrates

**On response**: `PackContextExtractor` (new, small service) inspects the response JSON, matches `extract_from` JSONPath expressions, and writes extracted values into the existing context store under the declared keys.

**On request**: `PromptBuilder` already injects context keys into prompts via the existing context injection. Pack context keys get promoted (shown prominently in the prompt) so the LLM reuses them. No new mechanism needed — just ensure pack-declared keys are in the `PromoteKeys` list for the active journey step.

**Session seeding**: When a new session starts with an active pack, `PackContextSeeder` makes one LLM call to generate `seed_keys` values (site name, admin email, etc.) and stores them in the context. All subsequent requests in that session receive these as context.

### 6.4 File additions

```
mostlylucid.mockllmapi/
  Packs/
    PackContextExtractor.cs   ← extracts values from responses, writes to context store
    PackContextSeeder.cs      ← seeds session-start context from pack schema
```

These are wired into `RegularRequestHandler` and `StreamingRequestHandler` post-response hooks (same integration point as autoshape storage).

---

## Decisions Summary

| Decision | Choice | Reason |
|----------|--------|--------|
| Default model | `gemma4:4b` | 2b too weak for complex JSON, 4b handles it, stays local |
| Pack storage | Embedded + `~/.llmock/packs/` | Works out of box, still user-extensible |
| Pack data | Structure only, no values | Prevent fingerprinting |
| Config sanitization | Delimiter-strip only | Config is trusted; full injection detection is overkill |
| User var sanitization | Full `InputValidationService` | Untrusted input, needs full protection |
| Homebrew | Tap repo `scottgal/homebrew-llmock` | Standard pattern, auto-updated by CI |
| Context continuity | Pack declares extract keys + seed keys | Reuses existing context store, no new mechanism |

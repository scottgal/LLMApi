# CLI Overhaul — Design Spec
**Date:** 2026-04-22  
**Status:** Approved

---

## Overview

Three new subsystems bolt onto the existing `llmock.cli` to deliver a zero-dependency, btop-style developer experience:

1. **EmbeddedLlmProvider** — LlamaSharp + Qwen3.5-0.8B auto-download. `brew install llmock && llmock serve` works out of the box, no Ollama required.
2. **DashboardRenderer** — XenoAtom.Terminal.UI retained-mode btop-style live dashboard.
3. **DaemonController** — Unix socket IPC + launchd service install/uninstall.

If Ollama (or another backend) is configured, it takes precedence. The embedded LLM is the fallback of last resort.

---

## 1. Zero-Dependency Embedded LLM

### 1.1 Model

**Qwen3.5-0.8B Q4_K_M** (~533 MB GGUF). Chosen for:
- Reliable JSON generation at sub-1B scale
- Fast on Apple Silicon Metal backend (~40–60ms inference)
- Small enough for `brew install` without user complaint

Model stored at `~/.llmock/models/qwen3.5-0.8b-q4_k_m.gguf`.

### 1.2 EmbeddedLlmProvider

Implements `ILlmProvider` (existing interface). Registered in DI as a fallback — activated only when no Ollama/OpenAI backend is reachable.

```csharp
// llmock.cli/Embedded/EmbeddedLlmProvider.cs
public class EmbeddedLlmProvider : ILlmProvider
{
    string Name => "embedded";
    Task<string> GetCompletionAsync(string prompt, ...);
    IAsyncEnumerable<string> GetStreamingCompletionAsync(string prompt, ...);
}
```

**Metal detection:**
```csharp
var usesMetal = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
             && RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
```

Loads `LlamaSharp.Backend.Metal` on ARM macOS, `LlamaSharp.Backend.Cpu` otherwise.

### 1.3 ModelDownloader

Streams model from GitHub Releases (or configured URL), shows live progress, verifies SHA256.

**First-run flow:**
```
✦ LLMock v2.4.0
  Checking for embedded model... not found
  Downloading Qwen3.5-0.8B Q4_K_M (533 MB)
  [████████████░░░░░░░░] 62% · 4.2 MB/s · 48s remaining
  Verifying checksum... ✓
  Model ready. Starting server on :5555...
```

If download fails, prints a helpful error with manual download instructions and exits cleanly.

---

## 2. btop-Style Dashboard

### 2.1 Library

**XenoAtom.Terminal.UI** — retained-mode reactive terminal UI framework for .NET 10. Chosen for its React-like paradigm: state changes trigger targeted redraws, not full-screen repaints. Already viable since `llmock.cli` targets .NET 10.

### 2.2 Layout

```
┌─ LLMock ─────────────────────── :5555 ─── [q]uit ──────────────┐
│ Requests/s  ████▂▃█▅▂▁▃▅       Active Pack: wordpress-rest      │
│ Contexts    3 active · 142 total requests · 0 errors            │
│ Model       Qwen3.5-0.8B · Metal ✓ · 47ms avg · 99.2% success  │
├─ Recent Requests ──────────────────────────────────────────────-┤
│  GET /wp-json/wp/v2/users    200  43ms  [recon]                 │
│  GET /wp-json/wp/v2/posts    200  61ms  [recon]                 │
│  GET /wp-json/wp/v2/users/1  200  38ms  [user-enum]             │
│  POST /wp-json/wp/v2/posts   201  82ms                          │
├─ Active Contexts ───────────────────────────────────────────────┤
│  wordpress-recon   12 calls   last: 2s ago                      │
│  user-enum-bot      8 calls   last: 0s ago                      │
│  anonymous          3 calls   last: 14s ago                     │
└─────────────────────────────────────────────────────────────────┘
```

**Panels:**
- **Top bar**: requests/sec sparkline (last 60s), active pack name, port
- **Stats row**: context count, total requests, error count, model info, avg latency
- **Recent Requests**: rolling last 20 requests (method, path, status, latency, journey tag if active)
- **Active Contexts**: table of named API contexts with call count and last-used time

**Update cadence**: 500ms poll of `/api/dashboard/stats` SSE endpoint.

**Keyboard:** `q` to quit (sends graceful stop if running as daemon).

### 2.3 DashboardPoller

Lightweight background task inside `DashboardRenderer`. Polls `/api/dashboard/stats` over localhost HTTP. Feeds state into the XenoAtom UI component tree. No external dependency — reuses the existing stats endpoint already in `LLMApi/Program.cs`.

### 2.4 Modes

| Command | Dashboard | Server |
|---------|-----------|--------|
| `llmock serve` | Opens dashboard | Starts server |
| `llmock serve --headless` | No dashboard | Starts server |
| `llmock serve --daemon` | No dashboard | Starts server, detaches |
| `llmock dashboard` | Opens dashboard | Connects to existing daemon |

---

## 3. Daemon Mode + Service Management

### 3.1 DaemonController

Unix domain socket at `~/.llmock/llmock.sock`.

**Daemon side** (server): emits structured JSON events as the server runs.
**Client side** (`llmock status/logs/stop/dashboard`): connects to socket, reads events or sends commands.

**Message types** (`llmock.cli/Daemon/DaemonMessages.cs`):
```csharp
record StatsEvent(DateTime Timestamp, int RequestsPerSec, int ActiveContexts, int TotalRequests, int ErrorCount, double AvgLatencyMs);
record LogEvent(DateTime Timestamp, string Level, string Message);
record ShutdownCommand();
record StatusResponse(bool Running, string Version, TimeSpan Uptime, string? ActivePack, int Port);
```

**PID file**: `~/.llmock/llmock.pid`. Written on daemon start, removed on clean shutdown.

### 3.2 Command Behaviour

```bash
llmock serve              # foreground + dashboard
llmock serve --headless   # foreground, no dashboard (stdout logs)
llmock serve --daemon     # detach, write PID, open socket, log to ~/.llmock/llmock.log

llmock dashboard          # connect to daemon socket, open live UI
llmock status             # one-shot: Running | Port | Uptime | Pack | Req count
llmock logs               # tail LogEvents from socket (Ctrl-C to stop)
llmock stop               # send ShutdownCommand, wait for process exit, remove PID

llmock install-service    # write launchd plist + launchctl load
llmock uninstall-service  # launchctl unload + remove plist
```

### 3.3 launchd Integration

**Plist path**: `~/Library/LaunchAgents/com.llmock.agent.plist`

**Plist template** (generated by `ServiceManager`):
```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" ...>
<plist version="1.0">
<dict>
  <key>Label</key><string>com.llmock.agent</string>
  <key>ProgramArguments</key>
  <array>
    <string>/path/to/llmock</string>
    <string>serve</string>
    <string>--headless</string>
  </array>
  <key>RunAtLoad</key><true/>
  <key>KeepAlive</key><true/>
  <key>StandardOutPath</key><string>~/.llmock/llmock.log</string>
  <key>StandardErrorPath</key><string>~/.llmock/llmock.log</string>
</dict>
</plist>
```

**Install UX:**
```
$ llmock install-service
  Writing ~/Library/LaunchAgents/com.llmock.agent.plist
  Loading service via launchctl...
  ✓ LLMock will now start automatically on login.
  Run 'llmock status' to verify.
```

**Uninstall UX:**
```
$ llmock uninstall-service
  Unloading service...
  Removing ~/Library/LaunchAgents/com.llmock.agent.plist
  ✓ Service removed.
```

---

## 4. Model Management Commands

```bash
llmock models              # list downloaded models + active model
llmock models download     # force re-download / update to latest
```

**`llmock models` output:**
```
Downloaded models (~/.llmock/models/):
  ✓ qwen3.5-0.8b-q4_k_m.gguf   533 MB   [active - embedded]

Remote backend:
  ollama @ http://localhost:11434   gemma4:4b   [reachable]
```

---

## 5. File Layout

```
llmock.cli/
  Embedded/
    EmbeddedLlmProvider.cs    ← ILlmProvider impl using LlamaSharp
    ModelDownloader.cs        ← streaming download + SHA256 verify + progress
  Dashboard/
    DashboardRenderer.cs      ← XenoAtom.Terminal.UI panel layout + update loop
    DashboardPoller.cs        ← polls /api/dashboard/stats, feeds renderer
  Daemon/
    DaemonController.cs       ← Unix socket server (daemon) + client (CLI commands)
    DaemonMessages.cs         ← StatsEvent, LogEvent, ShutdownCommand, StatusResponse
  Service/
    ServiceManager.cs         ← launchd plist generation + launchctl invocation
  Commands/
    ServeCommand.cs           ← wires serve + dashboard + daemon modes
    DashboardCommand.cs       ← connects to daemon socket, opens UI
    StatusCommand.cs          ← one-shot status query
    LogsCommand.cs            ← tails daemon log stream
    StopCommand.cs            ← graceful shutdown
    ModelsCommand.cs          ← list + download models
    InstallServiceCommand.cs  ← launchd install
    UninstallServiceCommand.cs← launchd uninstall
  Program.cs                  ← add new commands, register EmbeddedLlmProvider fallback
  llmock.cli.csproj           ← add LlamaSharp, LlamaSharp.Backend.Metal, XenoAtom.Terminal.UI
```

---

## 6. Package Dependencies

| Package | Version | Purpose |
|---------|---------|---------|
| `LlamaSharp` | latest stable | llama.cpp .NET bindings |
| `LlamaSharp.Backend.Metal` | latest stable | Apple Silicon GPU acceleration |
| `LlamaSharp.Backend.Cpu` | latest stable | CPU fallback (x64, Linux) |
| `XenoAtom.Terminal.UI` | 1.3.0 | Retained-mode reactive terminal UI |

`XenoAtom.Terminal.UI` requires .NET 10 — `llmock.cli` already targets .NET 10 (added in pack system Task 2).

---

## 7. Decisions Summary

| Decision | Choice | Reason |
|----------|--------|--------|
| Embedded model | Qwen3.5-0.8B Q4_K_M | Reliable JSON at <1B, fast on Metal, ~533MB |
| Metal detection | `RuntimeInformation.IsOSPlatform(OSX) && Arm64` | Correct Apple Silicon check |
| Dashboard library | XenoAtom.Terminal.UI | Retained-mode reactive = btop-like targeted redraws, .NET 10 |
| Dashboard mode | On by default, `--headless` to suppress | Best UX for `brew install llmock && llmock serve` |
| IPC | Unix domain socket | Low overhead, works on macOS/Linux, no HTTP server needed |
| Service | launchd plist | Standard macOS pattern, survives reboots |
| Embedded LLM registration | Fallback ILlmProvider | No change to library; Ollama/OpenAI still take precedence |
| Model location | `~/.llmock/models/` | User-owned, survives CLI updates |

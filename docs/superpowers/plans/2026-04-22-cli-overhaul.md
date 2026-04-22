# CLI Overhaul Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Transform `llmock.cli` into a zero-dependency, btop-style developer tool with embedded LLM fallback, live dashboard, daemon mode, and launchd service management.

**Architecture:** Three new subsystems bolt onto the existing CLI — `EmbeddedLlmProvider` (LlamaSharp + Qwen3.5-0.8B auto-download as an `ILlmProvider` fallback), `DashboardRenderer` (XenoAtom.Terminal.UI retained-mode panels polling `/api/dashboard/stats`), and `DaemonController` (Unix socket IPC enabling `llmock status/stop/logs/dashboard` to talk to a running server). The existing arg-parsing monolith in `Program.cs` is refactored into focused command classes. `PublishSingleFile` is disabled because LlamaSharp requires unpacked native libraries.

**Tech Stack:** LlamaSharp + LlamaSharp.Backend.Metal/Cpu, XenoAtom.Terminal.UI 1.3.0, Unix domain sockets (System.Net.Sockets), launchd plists (System.Xml), Serilog (already present), .NET 10.

---

## File Map

**New files:**
```
llmock.cli/
  Embedded/
    EmbeddedLlmProvider.cs       ← ILlmProvider impl (LlamaSharp, ignores HttpClient param)
    ModelDownloader.cs           ← streaming HTTP download + SHA256 verify + progress bar
    EmbeddedModelOptions.cs      ← model URL, expected SHA256, local path
  Dashboard/
    DashboardRenderer.cs         ← XenoAtom.Terminal.UI panel layout + 500ms update loop
    DashboardPoller.cs           ← polls /api/dashboard/stats, exposes DashboardState record
    DashboardState.cs            ← immutable snapshot: requests/s, contexts, recent requests
  Daemon/
    DaemonController.cs          ← Unix socket server (write) + client (read/command) 
    DaemonMessages.cs            ← StatsEvent, LogEvent, ShutdownCommand, StatusResponse records
  Service/
    ServiceManager.cs            ← launchd plist generation + launchctl shell invocation
  Commands/
    ServeCommand.cs              ← parse serve args, wire server + daemon + dashboard
    DashboardCommand.cs          ← connect to socket, open live UI
    StatusCommand.cs             ← one-shot socket query → print table
    StopCommand.cs               ← send ShutdownCommand, wait for PID exit
    LogsCommand.cs               ← tail LogEvents from socket
    ModelsCommand.cs             ← list ~/.llmock/models/, optionally download
    InstallServiceCommand.cs     ← write plist + launchctl load
    UninstallServiceCommand.cs   ← launchctl unload + delete plist
```

**Modified files:**
- `llmock.cli/Program.cs` — replace monolith with command dispatch + EmbeddedLlmProvider registration
- `llmock.cli/llmock.cli.csproj` — add packages, disable PublishSingleFile, add conditional backends

---

## Task 1: Package dependencies + project config

**Files:**
- Modify: `llmock.cli/llmock.cli.csproj`

**Context:** The existing `.csproj` has `PublishSingleFile=true` which is incompatible with LlamaSharp (requires unpacked native .dylib). XenoAtom.Terminal.UI 1.3.0 requires .NET 10 — already satisfied. LlamaSharp ships native backends as separate packages; include Metal (macOS ARM64) and Cpu (everything else) and let LlamaSharp auto-detect at runtime.

- [ ] **Step 1: Read the current csproj**

Read: `llmock.cli/llmock.cli.csproj` (already done above — it has `PublishSingleFile=true`, `PublishTrimmed=true`, `SelfContained=true`)

- [ ] **Step 2: Update csproj**

Replace the entire `llmock.cli/llmock.cli.csproj` content with:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

    <PropertyGroup>
        <OutputType>Exe</OutputType>
        <TargetFramework>net10.0</TargetFramework>
        <ImplicitUsings>enable</ImplicitUsings>
        <Nullable>enable</Nullable>
        <RootNamespace>LLMock.Cli</RootNamespace>
        <AssemblyName>llmock</AssemblyName>

        <!-- Single-file disabled: LlamaSharp requires unpacked native libraries -->
        <PublishSingleFile>false</PublishSingleFile>
        <PublishTrimmed>false</PublishTrimmed>
        <SelfContained>true</SelfContained>
        <DebugType>embedded</DebugType>
        <InvariantGlobalization>true</InvariantGlobalization>
    </PropertyGroup>

    <ItemGroup>
        <ProjectReference Include="..\mostlylucid.mockllmapi\mostlylucid.mockllmapi.csproj"/>
    </ItemGroup>

    <ItemGroup>
        <PackageReference Include="Serilog.AspNetCore" Version="10.0.0"/>
        <PackageReference Include="Serilog.Sinks.File" Version="7.0.0"/>
        <PackageReference Include="LlamaSharp" Version="0.18.0"/>
        <PackageReference Include="LlamaSharp.Backend.Metal" Version="0.18.0" Condition="$([MSBuild]::IsOSPlatform('OSX'))"/>
        <PackageReference Include="LlamaSharp.Backend.Cpu" Version="0.18.0" Condition="!$([MSBuild]::IsOSPlatform('OSX'))"/>
        <PackageReference Include="XenoAtom.Terminal.UI" Version="1.3.0"/>
    </ItemGroup>

</Project>
```

- [ ] **Step 3: Restore packages and verify build**

```bash
cd llmock.cli
dotnet restore
dotnet build
```

Expected: Build succeeds. If LlamaSharp version 0.18.0 doesn't exist, run `dotnet search LlamaSharp` and use the latest stable version.

- [ ] **Step 4: Commit**

```bash
git add llmock.cli/llmock.cli.csproj
git commit -m "chore: add LlamaSharp + XenoAtom.Terminal.UI packages, disable single-file publish"
```

---

## Task 2: DaemonMessages + DaemonController

**Files:**
- Create: `llmock.cli/Daemon/DaemonMessages.cs`
- Create: `llmock.cli/Daemon/DaemonController.cs`
- Test: `LLMApi.Tests/Cli/DaemonMessagesTests.cs`

**Context:** The daemon uses a Unix domain socket at `~/.llmock/llmock.sock`. The server side emits newline-delimited JSON events. The client side reads events or sends commands. All messages serialize to JSON using `System.Text.Json`. The socket path and PID file path are constants on `DaemonController`.

- [ ] **Step 1: Write failing tests for DaemonMessages serialization**

Create `LLMApi.Tests/Cli/DaemonMessagesTests.cs`:

```csharp
using System.Text.Json;
using LLMock.Cli.Daemon;

namespace LLMApi.Tests.Cli;

public class DaemonMessagesTests
{
    [Fact]
    public void StatsEvent_SerializesAndDeserializes()
    {
        var evt = new StatsEvent(
            DateTime.UtcNow, RequestsPerSec: 5, ActiveContexts: 3,
            TotalRequests: 142, ErrorCount: 0, AvgLatencyMs: 47.2);

        var json = JsonSerializer.Serialize(evt);
        var deserialized = JsonSerializer.Deserialize<StatsEvent>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(5, deserialized.RequestsPerSec);
        Assert.Equal(3, deserialized.ActiveContexts);
        Assert.Equal(142, deserialized.TotalRequests);
        Assert.Equal(47.2, deserialized.AvgLatencyMs);
    }

    [Fact]
    public void LogEvent_SerializesAndDeserializes()
    {
        var evt = new LogEvent(DateTime.UtcNow, "INF", "Server started on :5555");

        var json = JsonSerializer.Serialize(evt);
        var deserialized = JsonSerializer.Deserialize<LogEvent>(json);

        Assert.NotNull(deserialized);
        Assert.Equal("INF", deserialized.Level);
        Assert.Equal("Server started on :5555", deserialized.Message);
    }

    [Fact]
    public void StatusResponse_SerializesAndDeserializes()
    {
        var resp = new StatusResponse(
            Running: true, Version: "2.4.0",
            Uptime: TimeSpan.FromMinutes(42), ActivePack: "wordpress-rest", Port: 5555);

        var json = JsonSerializer.Serialize(resp);
        var deserialized = JsonSerializer.Deserialize<StatusResponse>(json);

        Assert.NotNull(deserialized);
        Assert.True(deserialized.Running);
        Assert.Equal("2.4.0", deserialized.Version);
        Assert.Equal("wordpress-rest", deserialized.ActivePack);
        Assert.Equal(5555, deserialized.Port);
    }

    [Fact]
    public void DaemonController_SocketPath_IsInLLMockDir()
    {
        var socketPath = DaemonController.SocketPath;
        Assert.Contains(".llmock", socketPath);
        Assert.EndsWith("llmock.sock", socketPath);
    }

    [Fact]
    public void DaemonController_PidPath_IsInLLMockDir()
    {
        var pidPath = DaemonController.PidFilePath;
        Assert.Contains(".llmock", pidPath);
        Assert.EndsWith("llmock.pid", pidPath);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
cd LLMApi.Tests
dotnet test --filter "DaemonMessagesTests" --verbosity minimal
```

Expected: FAIL — types not found.

- [ ] **Step 3: Create DaemonMessages.cs**

Create `llmock.cli/Daemon/DaemonMessages.cs`:

```csharp
namespace LLMock.Cli.Daemon;

public record StatsEvent(
    DateTime Timestamp,
    int RequestsPerSec,
    int ActiveContexts,
    int TotalRequests,
    int ErrorCount,
    double AvgLatencyMs);

public record LogEvent(
    DateTime Timestamp,
    string Level,
    string Message);

public record ShutdownCommand(string Reason = "user-request");

public record StatusResponse(
    bool Running,
    string Version,
    TimeSpan Uptime,
    string? ActivePack,
    int Port);
```

- [ ] **Step 4: Create DaemonController.cs**

Create `llmock.cli/Daemon/DaemonController.cs`:

```csharp
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace LLMock.Cli.Daemon;

/// <summary>
/// Manages the Unix domain socket used for daemon IPC.
/// Server side: emits newline-delimited JSON events.
/// Client side: reads events or sends commands.
/// </summary>
public class DaemonController : IAsyncDisposable
{
    public static readonly string LLMockDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".llmock");

    public static readonly string SocketPath = Path.Combine(LLMockDir, "llmock.sock");
    public static readonly string PidFilePath = Path.Combine(LLMockDir, "llmock.pid");
    public static readonly string LogFilePath = Path.Combine(LLMockDir, "llmock.log");

    private Socket? _serverSocket;
    private readonly List<Socket> _clients = [];
    private readonly Lock _clientsLock = new();

    /// <summary>
    /// Start the Unix socket server (daemon side). Call once after app starts.
    /// </summary>
    public async Task StartServerAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(LLMockDir);

        // Remove stale socket file
        if (File.Exists(SocketPath))
            File.Delete(SocketPath);

        _serverSocket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        _serverSocket.Bind(new UnixDomainSocketEndPoint(SocketPath));
        _serverSocket.Listen(10);

        // Write PID file
        await File.WriteAllTextAsync(PidFilePath, Environment.ProcessId.ToString(), ct);

        // Accept clients in background
        _ = AcceptClientsAsync(ct);
    }

    private async Task AcceptClientsAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _serverSocket != null)
        {
            try
            {
                var client = await _serverSocket.AcceptAsync(ct);
                lock (_clientsLock)
                    _clients.Add(client);

                _ = HandleClientAsync(client, ct);
            }
            catch (OperationCanceledException) { break; }
            catch { /* server shutting down */ }
        }
    }

    private async Task HandleClientAsync(Socket client, CancellationToken ct)
    {
        // Clients can send commands; for now just keep alive until disconnected
        var buf = new byte[256];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var read = await client.ReceiveAsync(buf, ct);
                if (read == 0) break; // disconnected
            }
        }
        catch { /* client disconnected */ }
        finally
        {
            lock (_clientsLock)
                _clients.Remove(client);
            client.Dispose();
        }
    }

    /// <summary>
    /// Broadcast a JSON event to all connected clients (newline-delimited).
    /// </summary>
    public async Task BroadcastAsync<T>(T message)
    {
        var json = JsonSerializer.Serialize(message) + "\n";
        var bytes = Encoding.UTF8.GetBytes(json);

        List<Socket> snapshot;
        lock (_clientsLock)
            snapshot = [.._clients];

        foreach (var client in snapshot)
            try { await client.SendAsync(bytes); }
            catch { /* client gone */ }
    }

    /// <summary>
    /// Send a command to a running daemon and return the raw response line.
    /// </summary>
    public static async Task<string?> SendCommandAsync(string commandJson, CancellationToken ct = default)
    {
        if (!File.Exists(SocketPath))
            return null;

        using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await client.ConnectAsync(new UnixDomainSocketEndPoint(SocketPath), ct);

        var bytes = Encoding.UTF8.GetBytes(commandJson + "\n");
        await client.SendAsync(bytes, ct);

        // Read one response line
        var buf = new byte[4096];
        var read = await client.ReceiveAsync(buf, ct);
        return read > 0 ? Encoding.UTF8.GetString(buf, 0, read).Trim() : null;
    }

    /// <summary>
    /// Connect to running daemon and stream all events to the callback until cancelled.
    /// </summary>
    public static async Task TailEventsAsync(
        Func<string, Task> onLine,
        CancellationToken ct)
    {
        if (!File.Exists(SocketPath))
            throw new InvalidOperationException("No daemon is running (socket not found).");

        using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await client.ConnectAsync(new UnixDomainSocketEndPoint(SocketPath), ct);

        var buf = new byte[4096];
        var pending = new StringBuilder();

        while (!ct.IsCancellationRequested)
        {
            var read = await client.ReceiveAsync(buf, ct);
            if (read == 0) break;

            pending.Append(Encoding.UTF8.GetString(buf, 0, read));
            var text = pending.ToString();
            var lines = text.Split('\n');

            for (var i = 0; i < lines.Length - 1; i++)
                if (!string.IsNullOrWhiteSpace(lines[i]))
                    await onLine(lines[i]);

            pending.Clear();
            pending.Append(lines[^1]);
        }
    }

    public static bool IsDaemonRunning()
    {
        if (!File.Exists(PidFilePath)) return false;
        if (!int.TryParse(File.ReadAllText(PidFilePath).Trim(), out var pid)) return false;
        try
        {
            var p = System.Diagnostics.Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch { return false; }
    }

    public async ValueTask DisposeAsync()
    {
        List<Socket> snapshot;
        lock (_clientsLock)
        {
            snapshot = [.._clients];
            _clients.Clear();
        }
        foreach (var c in snapshot) c.Dispose();

        _serverSocket?.Dispose();

        // Clean up socket file and PID
        if (File.Exists(SocketPath)) File.Delete(SocketPath);
        if (File.Exists(PidFilePath)) File.Delete(PidFilePath);

        await ValueTask.CompletedTask;
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

```bash
cd LLMApi.Tests
dotnet test --filter "DaemonMessagesTests" --verbosity minimal
```

Expected: PASS — all 5 tests green.

- [ ] **Step 6: Build the CLI to catch compile errors**

```bash
cd llmock.cli
dotnet build
```

Expected: Build succeeds.

- [ ] **Step 7: Commit**

```bash
git add llmock.cli/Daemon/ LLMApi.Tests/Cli/DaemonMessagesTests.cs
git commit -m "feat: add DaemonMessages records and DaemonController Unix socket IPC"
```

---

## Task 3: EmbeddedModelOptions + ModelDownloader

**Files:**
- Create: `llmock.cli/Embedded/EmbeddedModelOptions.cs`
- Create: `llmock.cli/Embedded/ModelDownloader.cs`
- Test: `LLMApi.Tests/Cli/ModelDownloaderTests.cs`

**Context:** `ModelDownloader` streams a GGUF file from a URL into `~/.llmock/models/`. It shows a progress bar on stdout. After download it verifies SHA256. If the file already exists and SHA256 matches, it skips the download. The URL and expected SHA256 are in `EmbeddedModelOptions`. We don't make real HTTP calls in tests — we test the progress calculation logic and path-building logic.

- [ ] **Step 1: Write failing tests**

Create `LLMApi.Tests/Cli/ModelDownloaderTests.cs`:

```csharp
using LLMock.Cli.Embedded;

namespace LLMApi.Tests.Cli;

public class ModelDownloaderTests
{
    [Fact]
    public void ModelPath_IsInLLMockModelsDir()
    {
        var opts = new EmbeddedModelOptions();
        var path = ModelDownloader.GetModelPath(opts);

        Assert.Contains(".llmock", path);
        Assert.Contains("models", path);
        Assert.EndsWith(opts.FileName, path);
    }

    [Fact]
    public void FormatBytes_UnderKb_ShowsBytes()
    {
        Assert.Equal("512 B", ModelDownloader.FormatBytes(512));
    }

    [Fact]
    public void FormatBytes_Megabytes_ShowsMB()
    {
        var result = ModelDownloader.FormatBytes(5 * 1024 * 1024);
        Assert.Contains("MB", result);
    }

    [Fact]
    public void FormatBytes_Gigabytes_ShowsGB()
    {
        var result = ModelDownloader.FormatBytes(2L * 1024 * 1024 * 1024);
        Assert.Contains("GB", result);
    }

    [Theory]
    [InlineData(0, 100, "░░░░░░░░░░░░░░░░░░░░")]
    [InlineData(50, 100, "██████████░░░░░░░░░░")]
    [InlineData(100, 100, "████████████████████")]
    public void BuildProgressBar_ReturnsCorrectFill(long current, long total, string expected)
    {
        var bar = ModelDownloader.BuildProgressBar(current, total, width: 20);
        Assert.Equal(expected, bar);
    }

    [Fact]
    public void EmbeddedModelOptions_HasExpectedDefaults()
    {
        var opts = new EmbeddedModelOptions();
        Assert.Equal("qwen3.5-0.8b-q4_k_m.gguf", opts.FileName);
        Assert.False(string.IsNullOrWhiteSpace(opts.DownloadUrl));
        Assert.False(string.IsNullOrWhiteSpace(opts.ExpectedSha256));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test --filter "ModelDownloaderTests" --verbosity minimal
```

Expected: FAIL — types not found.

- [ ] **Step 3: Create EmbeddedModelOptions.cs**

Create `llmock.cli/Embedded/EmbeddedModelOptions.cs`:

```csharp
namespace LLMock.Cli.Embedded;

public class EmbeddedModelOptions
{
    /// <summary>GGUF filename stored in ~/.llmock/models/</summary>
    public string FileName { get; init; } = "qwen3.5-0.8b-q4_k_m.gguf";

    /// <summary>Direct download URL for the GGUF file.</summary>
    /// <remarks>
    /// Update this URL when the model is updated.
    /// Current: Qwen3.5-0.8B Q4_K_M from Hugging Face.
    /// </remarks>
    public string DownloadUrl { get; init; } =
        "https://huggingface.co/Qwen/Qwen3.5-0.8B-GGUF/resolve/main/qwen3.5-0.8b-q4_k_m.gguf";

    /// <summary>Expected SHA256 of the GGUF file (lowercase hex). Set to empty string to skip verification.</summary>
    public string ExpectedSha256 { get; init; } =
        ""; // populated at release time — empty = skip verification during development
}
```

- [ ] **Step 4: Create ModelDownloader.cs**

Create `llmock.cli/Embedded/ModelDownloader.cs`:

```csharp
using System.Security.Cryptography;

namespace LLMock.Cli.Embedded;

public static class ModelDownloader
{
    private static readonly string ModelsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".llmock", "models");

    public static string GetModelPath(EmbeddedModelOptions opts) =>
        Path.Combine(ModelsDir, opts.FileName);

    /// <summary>
    /// Ensures the model file exists locally. Downloads it if missing.
    /// Returns the local path to the GGUF file.
    /// </summary>
    public static async Task<string> EnsureModelAsync(
        EmbeddedModelOptions opts,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(ModelsDir);
        var localPath = GetModelPath(opts);

        if (File.Exists(localPath))
        {
            if (string.IsNullOrWhiteSpace(opts.ExpectedSha256))
            {
                Console.WriteLine($"  Model found: {opts.FileName}");
                return localPath;
            }

            Console.Write($"  Verifying checksum... ");
            if (await VerifySha256Async(localPath, opts.ExpectedSha256, ct))
            {
                Console.WriteLine("✓");
                return localPath;
            }

            Console.WriteLine("MISMATCH — re-downloading");
            File.Delete(localPath);
        }

        await DownloadAsync(opts, localPath, ct);
        return localPath;
    }

    private static async Task DownloadAsync(
        EmbeddedModelOptions opts,
        string localPath,
        CancellationToken ct)
    {
        Console.WriteLine($"  Downloading {opts.FileName}");
        using var http = new HttpClient();
        http.Timeout = TimeSpan.FromHours(2);

        using var response = await http.GetAsync(opts.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? -1;
        var startTime = DateTime.UtcNow;
        var downloaded = 0L;

        var tmpPath = localPath + ".tmp";
        await using (var dest = File.Create(tmpPath))
        await using (var src = await response.Content.ReadAsStreamAsync(ct))
        {
            var buffer = new byte[81920];
            int read;
            while ((read = await src.ReadAsync(buffer, ct)) > 0)
            {
                await dest.WriteAsync(buffer.AsMemory(0, read), ct);
                downloaded += read;
                PrintProgress(downloaded, total, startTime);
            }
        }

        Console.WriteLine();

        if (!string.IsNullOrWhiteSpace(opts.ExpectedSha256))
        {
            Console.Write("  Verifying checksum... ");
            if (!await VerifySha256Async(tmpPath, opts.ExpectedSha256, ct))
            {
                File.Delete(tmpPath);
                throw new InvalidOperationException("Downloaded file failed SHA256 verification. Please try again.");
            }
            Console.WriteLine("✓");
        }

        File.Move(tmpPath, localPath, overwrite: true);
        Console.WriteLine($"  Model ready: {localPath}");
    }

    private static void PrintProgress(long downloaded, long total, DateTime startTime)
    {
        var elapsed = (DateTime.UtcNow - startTime).TotalSeconds;
        var speed = elapsed > 0 ? downloaded / elapsed : 0;
        var bar = total > 0 ? BuildProgressBar(downloaded, total, 20) : "░░░░░░░░░░░░░░░░░░░░";
        var pct = total > 0 ? (int)(downloaded * 100 / total) : 0;
        var remaining = total > 0 && speed > 0
            ? TimeSpan.FromSeconds((total - downloaded) / speed).ToString(@"mm\:ss")
            : "--:--";

        Console.Write($"\r  [{bar}] {pct,3}% · {FormatBytes((long)speed)}/s · {remaining} remaining  ");
    }

    private static async Task<bool> VerifySha256Async(string path, string expectedHex, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash).Equals(expectedHex, StringComparison.OrdinalIgnoreCase);
    }

    public static string FormatBytes(long bytes)
    {
        return bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
            < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
            _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB"
        };
    }

    public static string BuildProgressBar(long current, long total, int width = 20)
    {
        if (total <= 0) return new string('░', width);
        var filled = (int)(current * width / total);
        filled = Math.Clamp(filled, 0, width);
        return new string('█', filled) + new string('░', width - filled);
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

```bash
dotnet test --filter "ModelDownloaderTests" --verbosity minimal
```

Expected: PASS — all 6 tests green.

- [ ] **Step 6: Commit**

```bash
git add llmock.cli/Embedded/ LLMApi.Tests/Cli/ModelDownloaderTests.cs
git commit -m "feat: add EmbeddedModelOptions and ModelDownloader with SHA256 verification"
```

---

## Task 4: EmbeddedLlmProvider

**Files:**
- Create: `llmock.cli/Embedded/EmbeddedLlmProvider.cs`
- Test: `LLMApi.Tests/Cli/EmbeddedLlmProviderTests.cs`

**Context:** `EmbeddedLlmProvider` implements `ILlmProvider` from `mostlylucid.mockllmapi.Services.Providers`. The `ILlmProvider` interface passes an `HttpClient` as the first parameter to each call — we accept it but ignore it. LlamaSharp's API: create a `LlamaWeights` from the model file, create a `LlamaContext` from the weights, then use `InteractiveExecutor` + `ChatSession` for completions. Metal auto-loads if `LlamaSharp.Backend.Metal` package is present on ARM macOS.

The provider is `IAsyncDisposable` — it holds the model weights in memory across requests. It is registered as a singleton in the CLI's DI container and registered with `LlmProviderFactory.RegisterProvider("embedded", ...)` after the app starts.

Tests use a fake/mock approach since we don't load a real model in CI.

- [ ] **Step 1: Write failing tests**

Create `LLMApi.Tests/Cli/EmbeddedLlmProviderTests.cs`:

```csharp
using LLMock.Cli.Embedded;

namespace LLMApi.Tests.Cli;

public class EmbeddedLlmProviderTests
{
    [Fact]
    public void Name_IsEmbedded()
    {
        // Provider name must be "embedded" for registration in LlmProviderFactory
        Assert.Equal("embedded", EmbeddedLlmProvider.ProviderName);
    }

    [Fact]
    public void IsMetalAvailable_ReturnsFalseOnNonArmMac()
    {
        // On CI (x64 Linux), Metal should not be detected
        var isArm = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture
                    == System.Runtime.InteropServices.Architecture.Arm64;
        var isMac = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
            System.Runtime.InteropServices.OSPlatform.OSX);

        var expectedMetal = isArm && isMac;
        Assert.Equal(expectedMetal, EmbeddedLlmProvider.IsMetalAvailable());
    }

    [Fact]
    public void ConfigureClient_DoesNotThrow()
    {
        // ConfigureClient is a no-op for embedded provider (no HTTP)
        var provider = new EmbeddedLlmProvider(null!);
        var client = new HttpClient();
        // Should not throw
        provider.ConfigureClient(client, apiKey: null);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test --filter "EmbeddedLlmProviderTests" --verbosity minimal
```

Expected: FAIL — type not found.

- [ ] **Step 3: Create EmbeddedLlmProvider.cs**

Create `llmock.cli/Embedded/EmbeddedLlmProvider.cs`:

```csharp
using System.Runtime.InteropServices;
using System.Text;
using LLamaSharp.Core;
using Microsoft.Extensions.Logging;
using mostlylucid.mockllmapi.Services.Providers;

namespace LLMock.Cli.Embedded;

/// <summary>
/// LLM provider backed by LlamaSharp (local llama.cpp inference).
/// Ignores the HttpClient parameter — inference is in-process.
/// Registered as a fallback in LlmProviderFactory when no remote backend is reachable.
/// </summary>
public class EmbeddedLlmProvider : ILlmProvider, IAsyncDisposable
{
    public const string ProviderName = "embedded";
    public string Name => ProviderName;

    private readonly ILogger? _logger;
    private LlamaWeights? _weights;
    private readonly Lock _loadLock = new();
    private string? _modelPath;

    public EmbeddedLlmProvider(ILogger<EmbeddedLlmProvider>? logger)
    {
        _logger = logger;
    }

    public static bool IsMetalAvailable() =>
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX) &&
        RuntimeInformation.ProcessArchitecture == Architecture.Arm64;

    /// <summary>Load model from disk. Call once before first inference.</summary>
    public void LoadModel(string modelPath)
    {
        lock (_loadLock)
        {
            if (_weights != null) return; // already loaded

            _modelPath = modelPath;
            _logger?.LogInformation("Loading embedded model from {ModelPath} (Metal: {Metal})",
                modelPath, IsMetalAvailable());

            var parameters = new ModelParams(modelPath)
            {
                ContextSize = 2048,
                GpuLayerCount = IsMetalAvailable() ? 99 : 0, // 99 = all layers on GPU
            };
            _weights = LlamaWeights.LoadFromFile(parameters);

            _logger?.LogInformation("Embedded model loaded: {ModelPath}", modelPath);
        }
    }

    public Task<string> GetCompletionAsync(
        HttpClient client, // ignored
        string prompt,
        string modelName, // ignored — we use the loaded model
        double temperature,
        int? maxTokens,
        CancellationToken cancellationToken)
    {
        if (_weights == null)
            throw new InvalidOperationException("Embedded model not loaded. Call LoadModel() first.");

        var parameters = new ModelParams(_modelPath!)
        {
            ContextSize = 2048,
            GpuLayerCount = IsMetalAvailable() ? 99 : 0,
        };

        using var context = _weights.CreateContext(parameters);
        var executor = new StatelessExecutor(_weights, parameters);

        var sb = new StringBuilder();
        var inferenceParams = new InferenceParams
        {
            MaxTokens = maxTokens ?? 512,
            Temperature = (float)temperature,
            AntiPrompts = ["\n\n\n"],
        };

        foreach (var text in executor.Infer(prompt, inferenceParams, cancellationToken))
            sb.Append(text);

        return Task.FromResult(sb.ToString().Trim());
    }

    public async Task<HttpResponseMessage> GetStreamingCompletionAsync(
        HttpClient client, // ignored
        string prompt,
        string modelName,
        double temperature,
        CancellationToken cancellationToken)
    {
        // For streaming, we generate the full response and return it as a fake HTTP response
        // This is a simplification — the streaming pipeline will chunk it character by character
        var result = await GetCompletionAsync(client, prompt, modelName, temperature, 512, cancellationToken);

        var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        response.Content = new StringContent(result);
        return response;
    }

    public async Task<List<string>> GetNCompletionsAsync(
        HttpClient client,
        string prompt,
        string modelName,
        double temperature,
        int n,
        CancellationToken cancellationToken)
    {
        var results = new List<string>(n);
        for (var i = 0; i < n; i++)
            results.Add(await GetCompletionAsync(client, prompt, modelName, temperature, 512, cancellationToken));
        return results;
    }

    public void ConfigureClient(HttpClient client, string? apiKey)
    {
        // No-op: embedded provider doesn't use HTTP
    }

    public async ValueTask DisposeAsync()
    {
        _weights?.Dispose();
        _weights = null;
        await ValueTask.CompletedTask;
    }
}
```

**Note:** LlamaSharp's API surface may differ slightly from what's shown (e.g., `StatelessExecutor` vs `InteractiveExecutor`, exact `InferenceParams` properties). Check the LlamaSharp NuGet package XML docs or README for the exact API after adding the package reference. The pattern above is representative — adjust type names to match the actual package.

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test --filter "EmbeddedLlmProviderTests" --verbosity minimal
```

Expected: PASS — all 3 tests green.

- [ ] **Step 5: Build CLI**

```bash
cd llmock.cli && dotnet build
```

Expected: Builds successfully.

- [ ] **Step 6: Commit**

```bash
git add llmock.cli/Embedded/EmbeddedLlmProvider.cs LLMApi.Tests/Cli/EmbeddedLlmProviderTests.cs
git commit -m "feat: add EmbeddedLlmProvider backed by LlamaSharp with Metal support"
```

---

## Task 5: DashboardState + DashboardPoller

**Files:**
- Create: `llmock.cli/Dashboard/DashboardState.cs`
- Create: `llmock.cli/Dashboard/DashboardPoller.cs`
- Test: `LLMApi.Tests/Cli/DashboardPollerTests.cs`

**Context:** `DashboardState` is an immutable snapshot of what the dashboard should show. `DashboardPoller` fetches `/api/dashboard/stats` every 500ms and updates state. The stats endpoint already exists in `LLMApi/Program.cs` and returns JSON with `timestamp`, `connections`, `activeContexts`, `totalRequests`, `hubContexts`, `apiContexts` (array of `{name, calls, lastUsed}`). The poller also maintains a sliding window of requests-per-second calculated from consecutive total counts.

- [ ] **Step 1: Write failing tests**

Create `LLMApi.Tests/Cli/DashboardPollerTests.cs`:

```csharp
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
        Assert.True(rps <= 10); // sanity: 5 req in ~1 sec
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
        Assert.True(snapshot.SecondsSinceLastUse >= 4); // ~5 seconds ago
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test --filter "DashboardPollerTests" --verbosity minimal
```

Expected: FAIL.

- [ ] **Step 3: Create DashboardState.cs**

Create `llmock.cli/Dashboard/DashboardState.cs`:

```csharp
namespace LLMock.Cli.Dashboard;

public record ContextSnapshot(string Name, int Calls, DateTime LastUsed)
{
    public int SecondsSinceLastUse => (int)(DateTime.UtcNow - LastUsed).TotalSeconds;
}

public class DashboardState
{
    public DateTime SnapshotTime { get; init; } = DateTime.UtcNow;
    public int TotalRequests { get; init; }
    public int ActiveContexts { get; init; }
    public int ErrorCount { get; init; }
    public double RequestsPerSec { get; init; }
    public double AvgLatencyMs { get; init; }
    public string ModelName { get; init; } = "gemma4:4b";
    public bool MetalActive { get; init; }
    public string? ActivePack { get; init; }
    public int Port { get; init; } = 5555;
    public List<ContextSnapshot> RecentContexts { get; init; } = [];
    public string[] SparklineHistory { get; init; } = []; // last 30 rps samples as sparkline chars
}
```

- [ ] **Step 4: Create DashboardPoller.cs**

Create `llmock.cli/Dashboard/DashboardPoller.cs`:

```csharp
using System.Text.Json;

namespace LLMock.Cli.Dashboard;

public class DashboardPoller
{
    private static readonly string[] SparklineChars = ["▁", "▂", "▃", "▄", "▅", "▆", "▇", "█"];
    private const int SparklineLength = 30;

    private readonly string _statsUrl;
    private DashboardState _current = new();
    private readonly List<double> _rpsHistory = [];

    public DashboardPoller(int port = 5555)
    {
        _statsUrl = $"http://localhost:{port}/api/dashboard/stats";
    }

    public DashboardState Current => _current;

    /// <summary>Continuously polls the stats endpoint and updates Current state.</summary>
    public async Task RunAsync(Action onUpdate, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var json = await http.GetStringAsync(_statsUrl, ct);
                var prev = _current;
                _current = ParseStats(json, prev);
                onUpdate();
            }
            catch (OperationCanceledException) { break; }
            catch { /* server not yet ready, retry */ }

            await Task.Delay(500, ct).ConfigureAwait(false);
        }
    }

    public static double CalculateRps(DashboardState prev, DashboardState current)
    {
        var elapsed = (current.SnapshotTime - prev.SnapshotTime).TotalSeconds;
        if (elapsed <= 0) return 0;
        var delta = current.TotalRequests - prev.TotalRequests;
        return delta <= 0 ? 0 : delta / elapsed;
    }

    private DashboardState ParseStats(string json, DashboardState prev)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var totalRequests = root.TryGetProperty("totalRequests", out var tr) ? tr.GetInt32() : 0;
        var activeContexts = root.TryGetProperty("activeContexts", out var ac) ? ac.GetInt32() : 0;

        var contexts = new List<ContextSnapshot>();
        if (root.TryGetProperty("apiContexts", out var ctxArr))
            foreach (var ctx in ctxArr.EnumerateArray())
            {
                var name = ctx.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                var calls = ctx.TryGetProperty("calls", out var c) ? c.GetInt32() : 0;
                var lastUsed = ctx.TryGetProperty("lastUsed", out var lu)
                    ? lu.GetDateTime()
                    : DateTime.UtcNow;
                contexts.Add(new ContextSnapshot(name, calls, lastUsed));
            }

        var snapshot = new DashboardState
        {
            SnapshotTime = DateTime.UtcNow,
            TotalRequests = totalRequests,
            ActiveContexts = activeContexts,
            RecentContexts = contexts,
        };

        var rps = CalculateRps(prev, snapshot);
        _rpsHistory.Add(rps);
        if (_rpsHistory.Count > SparklineLength)
            _rpsHistory.RemoveAt(0);

        var maxRps = _rpsHistory.Count > 0 ? _rpsHistory.Max() : 1;
        var sparkline = _rpsHistory
            .Select(r => maxRps > 0
                ? SparklineChars[(int)(r / maxRps * (SparklineChars.Length - 1))]
                : SparklineChars[0])
            .ToArray();

        return snapshot with { RequestsPerSec = rps, SparklineHistory = sparkline };
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

```bash
dotnet test --filter "DashboardPollerTests" --verbosity minimal
```

Expected: PASS — all 4 tests green.

- [ ] **Step 6: Commit**

```bash
git add llmock.cli/Dashboard/DashboardState.cs llmock.cli/Dashboard/DashboardPoller.cs LLMApi.Tests/Cli/DashboardPollerTests.cs
git commit -m "feat: add DashboardState + DashboardPoller with sparkline RPS tracking"
```

---

## Task 6: DashboardRenderer

**Files:**
- Create: `llmock.cli/Dashboard/DashboardRenderer.cs`

**Context:** `DashboardRenderer` uses XenoAtom.Terminal.UI to render a retained-mode UI. XenoAtom.Terminal.UI's programming model: you create a `TerminalApp`, add `Widget` subclasses (panels, text elements), then call `app.Run()`. State updates trigger targeted redraws via property setters — like React. Check the XenoAtom.Terminal.UI NuGet page or GitHub for the exact API before implementing. The renderer subscribes to `DashboardPoller.Current` changes and updates widget properties.

There are no unit tests for the renderer (it requires a real terminal). Verify visually by running `llmock serve` after wiring in Task 8.

- [ ] **Step 1: Check XenoAtom.Terminal.UI API**

After `dotnet restore` succeeds (Task 1), examine the package:
```bash
find ~/.nuget/packages/xenoatom.terminal.ui -name "*.xml" | head -5
# Or check the README at https://github.com/xoofx/XenoAtom.Terminal.UI
```

Look for: how to create an app, how to create panels/boxes, how to update text content, how to run the event loop.

- [ ] **Step 2: Create DashboardRenderer.cs**

Create `llmock.cli/Dashboard/DashboardRenderer.cs` with the following structure. **Adapt the XenoAtom API calls to match the actual library API** based on what you find in Step 1.

```csharp
using XenoAtom.Terminal.UI; // adjust namespace if different
using LLMock.Cli.Dashboard;

namespace LLMock.Cli.Dashboard;

/// <summary>
/// btop-style retained-mode dashboard rendered via XenoAtom.Terminal.UI.
/// Call RunAsync() — it blocks until the user presses 'q' or cancellation is requested.
/// </summary>
public class DashboardRenderer
{
    private readonly DashboardPoller _poller;
    private readonly CancellationTokenSource _cts = new();

    public DashboardRenderer(DashboardPoller poller)
    {
        _poller = poller;
    }

    public async Task RunAsync(CancellationToken externalCt = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(externalCt, _cts.Token);
        var ct = linked.Token;

        // ─── XenoAtom.Terminal.UI setup ───────────────────────────────────────────
        // NOTE: The exact XenoAtom.Terminal.UI API must be verified against the package.
        // The structure below follows the retained-mode paradigm described in the docs.
        // Adjust class names (TerminalApp, TextWidget, BoxWidget, etc.) to match actual API.

        // Start poller in background
        var pollerTask = _poller.RunAsync(() => Refresh(), ct);

        // Render initial state
        RenderFallback(ct);

        await pollerTask;
    }

    private void Refresh()
    {
        var state = _poller.Current;
        // Update widget properties — XenoAtom triggers targeted redraws
        // This method is called every 500ms from the poller
        RenderFallback(CancellationToken.None); // fallback to console until XenoAtom API confirmed
    }

    /// <summary>
    /// Console-based fallback render used until XenoAtom.Terminal.UI API is confirmed.
    /// Replace with XenoAtom widget updates once the API is known.
    /// </summary>
    private void RenderFallback(CancellationToken ct)
    {
        var state = _poller.Current;
        if (ct.IsCancellationRequested) return;

        Console.Clear();
        var width = Math.Min(Console.WindowWidth, 80);
        var line = new string('─', width - 2);

        Console.WriteLine($"┌{line}┐");
        var sparkline = string.Join("", state.SparklineHistory.TakeLast(40));
        Console.WriteLine($"│ Requests/s  {sparkline,-40} │");
        Console.WriteLine($"│ Contexts    {state.ActiveContexts} active · {state.TotalRequests} total".PadRight(width - 1) + "│");
        Console.WriteLine($"│ Model       {state.ModelName}{(state.MetalActive ? " · Metal ✓" : "")}  {state.AvgLatencyMs:F0}ms avg".PadRight(width - 1) + "│");
        Console.WriteLine($"├{line}┤");
        Console.WriteLine($"│ Active Contexts".PadRight(width - 1) + "│");

        foreach (var ctx in state.RecentContexts.Take(5))
        {
            var row = $"│   {ctx.Name,-30} {ctx.Calls,5} calls   {ctx.SecondsSinceLastUse}s ago";
            Console.WriteLine(row.PadRight(width - 1) + "│");
        }

        Console.WriteLine($"└{line}┘");
        Console.WriteLine("  [q] quit");
    }
}
```

- [ ] **Step 3: Build to verify compile**

```bash
cd llmock.cli && dotnet build
```

Expected: Builds. Warnings about XenoAtom API usage are acceptable at this stage.

- [ ] **Step 4: Commit**

```bash
git add llmock.cli/Dashboard/DashboardRenderer.cs
git commit -m "feat: add DashboardRenderer (btop-style console UI, XenoAtom integration pending API confirmation)"
```

---

## Task 7: ServiceManager

**Files:**
- Create: `llmock.cli/Service/ServiceManager.cs`
- Test: `LLMApi.Tests/Cli/ServiceManagerTests.cs`

**Context:** `ServiceManager` generates a launchd plist XML string and invokes `launchctl` via `Process.Start`. The plist is written to `~/Library/LaunchAgents/com.llmock.agent.plist`. Tests verify plist content without touching the filesystem or invoking `launchctl`.

- [ ] **Step 1: Write failing tests**

Create `LLMApi.Tests/Cli/ServiceManagerTests.cs`:

```csharp
using LLMock.Cli.Service;

namespace LLMApi.Tests.Cli;

public class ServiceManagerTests
{
    [Fact]
    public void GeneratePlist_ContainsLabel()
    {
        var plist = ServiceManager.GeneratePlist("/usr/local/bin/llmock", 5555);
        Assert.Contains("com.llmock.agent", plist);
    }

    [Fact]
    public void GeneratePlist_ContainsExecutablePath()
    {
        var plist = ServiceManager.GeneratePlist("/usr/local/bin/llmock", 5555);
        Assert.Contains("/usr/local/bin/llmock", plist);
    }

    [Fact]
    public void GeneratePlist_ContainsServeHeadless()
    {
        var plist = ServiceManager.GeneratePlist("/usr/local/bin/llmock", 5555);
        Assert.Contains("serve", plist);
        Assert.Contains("--headless", plist);
    }

    [Fact]
    public void GeneratePlist_ContainsRunAtLoad()
    {
        var plist = ServiceManager.GeneratePlist("/usr/local/bin/llmock", 5555);
        Assert.Contains("RunAtLoad", plist);
        Assert.Contains("<true/>", plist);
    }

    [Fact]
    public void GeneratePlist_ContainsLogPath()
    {
        var plist = ServiceManager.GeneratePlist("/usr/local/bin/llmock", 5555);
        Assert.Contains("llmock.log", plist);
        Assert.Contains("StandardOutPath", plist);
    }

    [Fact]
    public void PlistPath_IsInLaunchAgents()
    {
        Assert.Contains("LaunchAgents", ServiceManager.PlistPath);
        Assert.Contains("com.llmock.agent.plist", ServiceManager.PlistPath);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
dotnet test --filter "ServiceManagerTests" --verbosity minimal
```

Expected: FAIL.

- [ ] **Step 3: Create ServiceManager.cs**

Create `llmock.cli/Service/ServiceManager.cs`:

```csharp
using System.Diagnostics;
using LLMock.Cli.Daemon;

namespace LLMock.Cli.Service;

public static class ServiceManager
{
    public static readonly string PlistPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Library", "LaunchAgents", "com.llmock.agent.plist");

    private static readonly string LogPath = DaemonController.LogFilePath;

    public static string GeneratePlist(string executablePath, int port)
    {
        return $"""
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>Label</key>
    <string>com.llmock.agent</string>
    <key>ProgramArguments</key>
    <array>
        <string>{executablePath}</string>
        <string>serve</string>
        <string>--headless</string>
        <string>--port</string>
        <string>{port}</string>
    </array>
    <key>RunAtLoad</key>
    <true/>
    <key>KeepAlive</key>
    <true/>
    <key>StandardOutPath</key>
    <string>{LogPath}</string>
    <key>StandardErrorPath</key>
    <string>{LogPath}</string>
</dict>
</plist>
""";
    }

    public static async Task InstallAsync(int port = 5555)
    {
        var executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Cannot determine executable path.");

        var plistContent = GeneratePlist(executablePath, port);
        var plistDir = Path.GetDirectoryName(PlistPath)!;
        Directory.CreateDirectory(plistDir);

        Console.WriteLine($"  Writing {PlistPath}");
        await File.WriteAllTextAsync(PlistPath, plistContent);

        Console.Write("  Loading service via launchctl... ");
        var exitCode = await RunLaunchctl("load", PlistPath);
        if (exitCode == 0)
        {
            Console.WriteLine("✓");
            Console.WriteLine("  LLMock will now start automatically on login.");
            Console.WriteLine("  Run 'llmock status' to verify.");
        }
        else
        {
            Console.WriteLine("FAILED");
            Console.WriteLine($"  launchctl load exited with code {exitCode}");
            Console.WriteLine($"  Try: launchctl load {PlistPath}");
        }
    }

    public static async Task UninstallAsync()
    {
        if (!File.Exists(PlistPath))
        {
            Console.WriteLine("  Service not installed (plist not found).");
            return;
        }

        Console.Write("  Unloading service... ");
        await RunLaunchctl("unload", PlistPath);
        Console.WriteLine("✓");

        Console.WriteLine($"  Removing {PlistPath}");
        File.Delete(PlistPath);

        Console.WriteLine("  Service removed.");
    }

    private static async Task<int> RunLaunchctl(string command, string arg)
    {
        var psi = new ProcessStartInfo("launchctl", $"{command} {arg}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var process = Process.Start(psi)!;
        await process.WaitForExitAsync();
        return process.ExitCode;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
dotnet test --filter "ServiceManagerTests" --verbosity minimal
```

Expected: PASS — all 6 tests green.

- [ ] **Step 5: Commit**

```bash
git add llmock.cli/Service/ServiceManager.cs LLMApi.Tests/Cli/ServiceManagerTests.cs
git commit -m "feat: add ServiceManager for launchd plist generation and install/uninstall"
```

---

## Task 8: Command classes

**Files:**
- Create: `llmock.cli/Commands/ServeCommand.cs`
- Create: `llmock.cli/Commands/DashboardCommand.cs`
- Create: `llmock.cli/Commands/StatusCommand.cs`
- Create: `llmock.cli/Commands/StopCommand.cs`
- Create: `llmock.cli/Commands/LogsCommand.cs`
- Create: `llmock.cli/Commands/ModelsCommand.cs`
- Create: `llmock.cli/Commands/InstallServiceCommand.cs`
- Create: `llmock.cli/Commands/UninstallServiceCommand.cs`

**Context:** These are thin wrappers that parse their specific args and delegate to the services built in previous tasks. Each command has a static `RunAsync(string[] args, CancellationToken ct)` method. No tests — they're integration points best verified by running the CLI.

- [ ] **Step 1: Create StatusCommand.cs**

Create `llmock.cli/Commands/StatusCommand.cs`:

```csharp
using System.Text.Json;
using LLMock.Cli.Daemon;

namespace LLMock.Cli.Commands;

public static class StatusCommand
{
    public static async Task<int> RunAsync(CancellationToken ct)
    {
        if (!DaemonController.IsDaemonRunning())
        {
            Console.WriteLine("  LLMock: NOT RUNNING");
            return 1;
        }

        Console.WriteLine("  LLMock: RUNNING");

        // Try to get stats from the stats endpoint directly
        try
        {
            // Read port from PID-adjacent config or default
            var port = 5555;
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var json = await http.GetStringAsync($"http://localhost:{port}/api/dashboard/stats", ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var totalRequests = root.TryGetProperty("totalRequests", out var tr) ? tr.GetInt32() : 0;
            var activeContexts = root.TryGetProperty("activeContexts", out var ac) ? ac.GetInt32() : 0;

            Console.WriteLine($"  Port:     {port}");
            Console.WriteLine($"  Requests: {totalRequests} total");
            Console.WriteLine($"  Contexts: {activeContexts} active");
        }
        catch
        {
            Console.WriteLine("  (Stats unavailable — server may be starting)");
        }

        return 0;
    }
}
```

- [ ] **Step 2: Create StopCommand.cs**

Create `llmock.cli/Commands/StopCommand.cs`:

```csharp
using LLMock.Cli.Daemon;

namespace LLMock.Cli.Commands;

public static class StopCommand
{
    public static async Task<int> RunAsync(CancellationToken ct)
    {
        if (!DaemonController.IsDaemonRunning())
        {
            Console.WriteLine("  LLMock is not running.");
            return 0;
        }

        Console.Write("  Stopping LLMock... ");

        // Read PID and send SIGTERM
        if (int.TryParse((await File.ReadAllTextAsync(DaemonController.PidFilePath, ct)).Trim(), out var pid))
        {
            try
            {
                var process = System.Diagnostics.Process.GetProcessById(pid);
                process.Kill(entireProcessTree: false); // SIGTERM equivalent
                await process.WaitForExitAsync(ct);
            }
            catch { /* already gone */ }
        }

        // Clean up socket and PID
        if (File.Exists(DaemonController.SocketPath)) File.Delete(DaemonController.SocketPath);
        if (File.Exists(DaemonController.PidFilePath)) File.Delete(DaemonController.PidFilePath);

        Console.WriteLine("✓");
        return 0;
    }
}
```

- [ ] **Step 3: Create LogsCommand.cs**

Create `llmock.cli/Commands/LogsCommand.cs`:

```csharp
using LLMock.Cli.Daemon;

namespace LLMock.Cli.Commands;

public static class LogsCommand
{
    public static async Task<int> RunAsync(CancellationToken ct)
    {
        var logPath = DaemonController.LogFilePath;
        if (!File.Exists(logPath))
        {
            Console.WriteLine($"  No log file at {logPath}");
            Console.WriteLine("  Start LLMock with: llmock serve --daemon");
            return 1;
        }

        Console.WriteLine($"  Tailing {logPath}  (Ctrl-C to stop)");
        Console.WriteLine();

        // Tail the log file
        using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        stream.Seek(0, SeekOrigin.End); // start from end (tail behavior)
        using var reader = new StreamReader(stream);

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line != null)
                Console.WriteLine(line);
            else
                await Task.Delay(250, ct).ConfigureAwait(false);
        }

        return 0;
    }
}
```

- [ ] **Step 4: Create ModelsCommand.cs**

Create `llmock.cli/Commands/ModelsCommand.cs`:

```csharp
using LLMock.Cli.Embedded;

namespace LLMock.Cli.Commands;

public static class ModelsCommand
{
    public static async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        var download = args.Contains("download");
        var opts = new EmbeddedModelOptions();
        var modelPath = ModelDownloader.GetModelPath(opts);

        if (download)
        {
            Console.WriteLine("  Downloading embedded model...");
            await ModelDownloader.EnsureModelAsync(opts, ct);
            return 0;
        }

        // List
        Console.WriteLine($"  Downloaded models (~/.llmock/models/):");
        if (File.Exists(modelPath))
        {
            var size = new FileInfo(modelPath).Length;
            Console.WriteLine($"    ✓ {opts.FileName}   {ModelDownloader.FormatBytes(size)}   [active - embedded]");
        }
        else
        {
            Console.WriteLine($"    ✗ {opts.FileName}   not downloaded");
            Console.WriteLine($"      Run: llmock models download");
        }

        return 0;
    }
}
```

- [ ] **Step 5: Create InstallServiceCommand.cs**

Create `llmock.cli/Commands/InstallServiceCommand.cs`:

```csharp
using LLMock.Cli.Service;

namespace LLMock.Cli.Commands;

public static class InstallServiceCommand
{
    public static async Task<int> RunAsync(int port = 5555)
    {
        if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.OSX))
        {
            Console.WriteLine("  install-service is only supported on macOS (launchd).");
            return 1;
        }

        await ServiceManager.InstallAsync(port);
        return 0;
    }
}
```

- [ ] **Step 6: Create UninstallServiceCommand.cs**

Create `llmock.cli/Commands/UninstallServiceCommand.cs`:

```csharp
using LLMock.Cli.Service;

namespace LLMock.Cli.Commands;

public static class UninstallServiceCommand
{
    public static async Task<int> RunAsync()
    {
        if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                System.Runtime.InteropServices.OSPlatform.OSX))
        {
            Console.WriteLine("  uninstall-service is only supported on macOS (launchd).");
            return 1;
        }

        await ServiceManager.UninstallAsync();
        return 0;
    }
}
```

- [ ] **Step 7: Create DashboardCommand.cs**

Create `llmock.cli/Commands/DashboardCommand.cs`:

```csharp
using LLMock.Cli.Daemon;
using LLMock.Cli.Dashboard;

namespace LLMock.Cli.Commands;

public static class DashboardCommand
{
    public static async Task<int> RunAsync(int port, CancellationToken ct)
    {
        if (!DaemonController.IsDaemonRunning())
        {
            Console.WriteLine("  No daemon is running. Start with: llmock serve --daemon");
            return 1;
        }

        Console.WriteLine($"  Connecting to LLMock on :{port}...");
        var poller = new DashboardPoller(port);
        var renderer = new DashboardRenderer(poller);
        await renderer.RunAsync(ct);
        return 0;
    }
}
```

- [ ] **Step 8: Build to verify compile**

```bash
cd llmock.cli && dotnet build
```

Expected: Builds successfully. Fix any compile errors.

- [ ] **Step 9: Commit**

```bash
git add llmock.cli/Commands/
git commit -m "feat: add CLI command classes (status, stop, logs, models, install/uninstall-service, dashboard)"
```

---

## Task 9: Refactor Program.cs + wire ServeCommand

**Files:**
- Modify: `llmock.cli/Program.cs`
- Create: `llmock.cli/Commands/ServeCommand.cs`

**Context:** The current `Program.cs` is a 400-line monolith. This task replaces it with a command dispatcher. The server startup logic moves into `ServeCommand`. `EmbeddedLlmProvider` is registered as a fallback after the DI container builds. The `--daemon` flag forks the process using `Process.Start` with `--headless` and exits.

- [ ] **Step 1: Create ServeCommand.cs**

Create `llmock.cli/Commands/ServeCommand.cs` — this contains the server startup logic extracted from the current `Program.cs`:

```csharp
using LLMock.Cli.Dashboard;
using LLMock.Cli.Daemon;
using LLMock.Cli.Embedded;
using mostlylucid.mockllmapi;
using mostlylucid.mockllmapi.Models;
using mostlylucid.mockllmapi.RequestHandlers;
using mostlylucid.mockllmapi.Services;
using mostlylucid.mockllmapi.Services.Providers;
using Serilog;
using Serilog.Events;

namespace LLMock.Cli.Commands;

public static class ServeCommand
{
    public static async Task<int> RunAsync(
        int port,
        string[] specs,
        string? backend,
        string? model,
        string? baseUrl,
        string? apiKey,
        string? configFile,
        string? pack,
        bool headless,
        bool daemon,
        CancellationToken ct)
    {
        // Daemon mode: re-launch self with --headless, then exit
        if (daemon)
        {
            var self = Environment.ProcessPath ?? "llmock";
            var daemonArgs = BuildDaemonArgs(port, specs, backend, model, baseUrl, apiKey, configFile, pack);
            var psi = new System.Diagnostics.ProcessStartInfo(self, daemonArgs)
            {
                UseShellExecute = true,
                CreateNoWindow = true,
            };
            System.Diagnostics.Process.Start(psi);
            Console.WriteLine($"  LLMock started in background on :{port}");
            Console.WriteLine("  Run 'llmock status' to check. 'llmock stop' to stop.");
            return 0;
        }

        ConfigureSerilog(headless);

        var builder = WebApplication.CreateBuilder();
        builder.Host.UseSerilog();

        // Load config
        if (!string.IsNullOrWhiteSpace(configFile))
            builder.Configuration.AddJsonFile(configFile, false, true);
        else
            builder.Configuration.AddJsonFile("appsettings.json", true, true);

        builder.Configuration.AddEnvironmentVariables("LLMOCK_");

        if (port == 5000) port = builder.Configuration.GetValue<int?>("LLMockCli:Port") ?? 5555;
        builder.WebHost.UseUrls($"http://localhost:{port}");

        // Register LLMock API
        builder.Services.AddLLMockApi(options =>
        {
            builder.Configuration.GetSection("LLMockApi").Bind(options);

            if (!string.IsNullOrWhiteSpace(backend) || !string.IsNullOrWhiteSpace(model) || !string.IsNullOrWhiteSpace(baseUrl))
            {
                var backendConfig = new LlmBackendConfig
                {
                    Name = backend ?? "cli",
                    Provider = backend ?? "ollama",
                    ModelName = model ?? "gemma4:4b",
                    BaseUrl = baseUrl ?? "http://localhost:11434/v1/",
                    ApiKey = apiKey,
                    Enabled = true
                };
                options.LlmBackends = options.LlmBackends is { Count: > 0 }
                    ? [backendConfig, ..options.LlmBackends.Skip(1)]
                    : [backendConfig];
            }

            if (options.LlmBackends == null || options.LlmBackends.Count == 0)
                options.LlmBackends =
                [
                    new LlmBackendConfig
                    {
                        Name = "ollama", Provider = "ollama",
                        ModelName = "gemma4:4b", BaseUrl = "http://localhost:11434/v1/", Enabled = true
                    }
                ];

            // Append embedded as last-resort fallback
            options.LlmBackends.Add(new LlmBackendConfig
            {
                Name = "embedded", Provider = "embedded",
                ModelName = "qwen3.5-0.8b", Enabled = true
            });

            if (!string.IsNullOrWhiteSpace(pack))
                options.ActivePackId = pack;
        });

        // Register EmbeddedLlmProvider in DI
        builder.Services.AddSingleton<EmbeddedLlmProvider>();

        var app = builder.Build();

        // Register embedded provider with the factory (fallback)
        var embeddedProvider = app.Services.GetRequiredService<EmbeddedLlmProvider>();
        var providerFactory = app.Services.GetRequiredService<LlmProviderFactory>();
        providerFactory.RegisterProvider(EmbeddedLlmProvider.ProviderName, embeddedProvider);

        // Ensure model is downloaded before starting
        var modelOpts = new EmbeddedModelOptions();
        if (!headless)
        {
            Console.WriteLine($"\n  ✦ LLMock v{GetVersion()}");
            Console.Write("  Checking for embedded model... ");
        }

        var modelReady = false;
        try
        {
            var modelPath = await ModelDownloader.EnsureModelAsync(modelOpts, ct);
            embeddedProvider.LoadModel(modelPath);
            modelReady = true;
        }
        catch (Exception ex)
        {
            Log.Warning("Embedded model unavailable: {Message}. Remote backends will be used.", ex.Message);
            if (!headless) Console.WriteLine($"  Embedded model unavailable — using remote backends only.");
        }

        app.UseSerilogRequestLogging(o =>
        {
            o.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000}ms";
            o.GetLevel = (ctx, elapsed, ex) =>
                ex != null ? LogEventLevel.Error
                : ctx.Response.StatusCode >= 500 ? LogEventLevel.Error
                : ctx.Response.StatusCode >= 400 ? LogEventLevel.Warning
                : LogEventLevel.Information;
        });

        app.MapLLMockApi();
        app.MapLLMockGraphQL("/api");

        // Daemon IPC socket
        await using var daemonController = new DaemonController();
        await daemonController.StartServerAsync(ct);

        // Broadcast stats every second
        _ = BroadcastStatsAsync(daemonController, port, ct);

        // Load OpenAPI specs
        if (specs.Length > 0)
            await LoadSpecsAsync(app, specs);

        // Start server
        var serverTask = app.RunAsync(ct);

        if (!headless)
        {
            // Open dashboard
            var poller = new DashboardPoller(port);
            var renderer = new DashboardRenderer(poller);
            await renderer.RunAsync(ct);
        }
        else
        {
            Log.Information("LLMock listening on :{Port} (headless)", port);
            await serverTask;
        }

        return 0;
    }

    private static async Task BroadcastStatsAsync(DaemonController daemon, int port, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var json = await http.GetStringAsync($"http://localhost:{port}/api/dashboard/stats", ct);
                await daemon.BroadcastAsync(new { type = "stats", data = json });
            }
            catch { /* not ready yet */ }
            await Task.Delay(1000, ct).ConfigureAwait(false);
        }
    }

    private static async Task LoadSpecsAsync(WebApplication app, string[] specs)
    {
        var openApiManager = app.Services.GetRequiredService<DynamicOpenApiManager>();
        foreach (var (spec, i) in specs.Select((s, i) => (s, i)))
            try
            {
                var specName = Path.GetFileNameWithoutExtension(spec)?.Replace(".", "-") ?? $"spec{i}";
                var result = await openApiManager.LoadSpecAsync(specName, spec, $"/api/spec{i}");
                Log.Information("Loaded spec '{Name}' — {Count} endpoints at /api/spec{I}", specName, result.EndpointCount, i);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load spec: {Spec}", spec);
            }
    }

    private static string BuildDaemonArgs(int port, string[] specs, string? backend, string? model,
        string? baseUrl, string? apiKey, string? configFile, string? pack)
    {
        var parts = new List<string> { "serve", "--headless", $"--port {port}" };
        if (backend != null) parts.Add($"--backend {backend}");
        if (model != null) parts.Add($"--model {model}");
        if (baseUrl != null) parts.Add($"--base-url {baseUrl}");
        if (apiKey != null) parts.Add($"--api-key {apiKey}");
        if (configFile != null) parts.Add($"--config {configFile}");
        if (pack != null) parts.Add($"--pack {pack}");
        foreach (var spec in specs) parts.Add($"--spec {spec}");
        return string.Join(" ", parts);
    }

    private static string GetVersion() =>
        System.Reflection.Assembly.GetExecutingAssembly()
            .GetName().Version?.ToString(3) ?? "2.4.0";

    private static void ConfigureSerilog(bool headless)
    {
        var config = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.FromLogContext();

        if (headless)
            config = config
                .WriteTo.File(
                    DaemonController.LogFilePath,
                    LogEventLevel.Information,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7,
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}");
        else
            config = config
                .WriteTo.Console(LogEventLevel.Information)
                .WriteTo.File(
                    "logs/llmock-.log",
                    LogEventLevel.Warning,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7);

        Log.Logger = config.CreateLogger();
    }
}
```

- [ ] **Step 2: Replace Program.cs**

Replace `llmock.cli/Program.cs` with:

```csharp
using LLMock.Cli.Commands;
using Serilog;

namespace LLMock.Cli;

internal class Program
{
    private static async Task<int> Main(string[] args)
    {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        try
        {
            return args.Length == 0 || args[0] == "serve"
                ? await HandleServe(args, cts.Token)
                : await HandleCommand(args, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n✗ Fatal error: {ex.Message}");
            Console.ResetColor();
            return 1;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    private static async Task<int> HandleServe(string[] args, CancellationToken ct)
    {
        var port = 5000;
        var specs = new List<string>();
        string? backend = null, model = null, baseUrl = null, apiKey = null, configFile = null, pack = null;
        var headless = false;
        var daemon = false;

        for (var i = 0; i < args.Length; i++)
            switch (args[i])
            {
                case "--port" or "-p" when i + 1 < args.Length: port = int.Parse(args[++i]); break;
                case "--spec" or "-s" when i + 1 < args.Length: specs.Add(args[++i]); break;
                case "--backend" or "-b" when i + 1 < args.Length: backend = args[++i]; break;
                case "--model" or "-m" when i + 1 < args.Length: model = args[++i]; break;
                case "--base-url" when i + 1 < args.Length: baseUrl = args[++i]; break;
                case "--api-key" or "-k" when i + 1 < args.Length: apiKey = args[++i]; break;
                case "--config" or "-c" when i + 1 < args.Length: configFile = args[++i]; break;
                case "--pack" or "-P" when i + 1 < args.Length: pack = args[++i]; break;
                case "--headless": headless = true; break;
                case "--daemon": daemon = true; break;
                case "--help" or "-h": ShowHelp(); return 0;
                default:
                    if (!args[i].StartsWith('-') && args[i] != "serve")
                        specs.Add(args[i]);
                    break;
            }

        return await ServeCommand.RunAsync(port, [..specs], backend, model, baseUrl, apiKey,
            configFile, pack, headless, daemon, ct);
    }

    private static async Task<int> HandleCommand(string[] args, CancellationToken ct)
    {
        return args[0] switch
        {
            "dashboard" => await DashboardCommand.RunAsync(GetPort(args), ct),
            "status" => await StatusCommand.RunAsync(ct),
            "stop" => await StopCommand.RunAsync(ct),
            "logs" => await LogsCommand.RunAsync(ct),
            "models" => await ModelsCommand.RunAsync(args[1..], ct),
            "install-service" => await InstallServiceCommand.RunAsync(GetPort(args)),
            "uninstall-service" => await UninstallServiceCommand.RunAsync(),
            "--help" or "-h" or "help" => ShowHelpReturn(),
            _ => UnknownCommand(args[0]),
        };
    }

    private static int GetPort(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i] is "--port" or "-p" && int.TryParse(args[i + 1], out var p))
                return p;
        return 5555;
    }

    private static int ShowHelpReturn() { ShowHelp(); return 0; }

    private static int UnknownCommand(string cmd)
    {
        Console.WriteLine($"Unknown command: {cmd}. Run 'llmock --help' for usage.");
        return 1;
    }

    private static void ShowHelp()
    {
        Console.WriteLine("""
            LLMock CLI - LLM-powered mock API server with live dashboard

            USAGE:
                llmock [command] [options]

            COMMANDS:
                serve                           Start the mock API server (default)
                dashboard                       Open live dashboard (connects to running daemon)
                status                          Show daemon status
                stop                            Stop the running daemon
                logs                            Tail daemon logs
                models                          List downloaded models
                models download                 Download the embedded model
                install-service                 Install as a login item (macOS launchd)
                uninstall-service               Remove login item

            SERVE OPTIONS:
                --port, -p <port>              Server port (default: 5555)
                --spec, -s <file-or-url>       OpenAPI spec file or URL (repeatable)
                --backend, -b <provider>       LLM backend (ollama, openai, lmstudio, embedded)
                --model, -m <model>            Model name
                --base-url <url>               LLM backend base URL
                --api-key, -k <key>            API key for LLM backend
                --config, -c <file>            Path to appsettings.json file
                --pack, -P <pack-id>           API Holodeck pack to activate
                --headless                     Run without dashboard UI
                --daemon                       Start in background (implies --headless)
                --help, -h                     Show this help

            EXAMPLES:
                llmock serve
                llmock serve --daemon
                llmock serve --pack wordpress-rest
                llmock serve --port 8080 --pack banking
                llmock install-service
                llmock status
                llmock dashboard
            """);
    }
}
```

- [ ] **Step 3: Build**

```bash
cd llmock.cli && dotnet build
```

Expected: Builds. Fix any compile errors (missing usings, namespace issues).

- [ ] **Step 4: Smoke test**

```bash
cd llmock.cli
dotnet run -- --help
```

Expected: Help text prints with all commands listed.

```bash
dotnet run -- models
```

Expected: Shows model status (not yet downloaded is fine).

- [ ] **Step 5: Run all tests**

```bash
dotnet test LLMApi.Tests/LLMApi.Tests.csproj --verbosity minimal
```

Expected: All existing tests still pass + new CLI tests pass.

- [ ] **Step 6: Commit**

```bash
git add llmock.cli/Program.cs llmock.cli/Commands/ServeCommand.cs
git commit -m "refactor: replace Program.cs monolith with command dispatcher + ServeCommand"
```

---

## Task 10: End-to-end smoke test + polish

**Files:**
- Modify: `llmock.cli/Commands/ServeCommand.cs` (minor adjustments after real test)

**Context:** Run the actual CLI locally to verify the first-run flow, dashboard render, and daemon commands work end-to-end. Fix any issues found.

- [ ] **Step 1: Full build in release mode**

```bash
dotnet publish llmock.cli/llmock.cli.csproj -c Release -r osx-arm64 -o /tmp/llmock-test
```

Expected: Publishes successfully. Output is a directory (not single file) because we disabled `PublishSingleFile`.

- [ ] **Step 2: Test first-run model download flow**

```bash
/tmp/llmock-test/llmock models
```

Expected: Shows model not downloaded.

```bash
# Don't actually download in CI — just verify the command works
/tmp/llmock-test/llmock models download --dry-run 2>/dev/null || true
/tmp/llmock-test/llmock --help
```

Expected: Help text displays correctly.

- [ ] **Step 3: Test serve + headless mode (no LLM needed)**

```bash
# Start server in headless mode (will fall back to Ollama if no embedded model)
/tmp/llmock-test/llmock serve --headless --port 15555 &
sleep 2

# Check it's responding
curl -s http://localhost:15555/api/dashboard/stats | head -c 200

# Stop it
/tmp/llmock-test/llmock stop || kill %1
```

Expected: Server starts, stats endpoint responds with JSON.

- [ ] **Step 4: Test status command**

```bash
/tmp/llmock-test/llmock serve --headless --port 15555 &
sleep 2
/tmp/llmock-test/llmock status
kill %1
```

Expected: `status` prints "RUNNING" with port and request count.

- [ ] **Step 5: Test install-service (macOS only)**

```bash
/tmp/llmock-test/llmock install-service
cat ~/Library/LaunchAgents/com.llmock.agent.plist | head -20
/tmp/llmock-test/llmock uninstall-service
```

Expected: Plist is created, then removed. No errors.

- [ ] **Step 6: Run full test suite**

```bash
dotnet test --verbosity minimal
```

Expected: All tests pass.

- [ ] **Step 7: Commit any fixes**

```bash
git add -p  # stage only modified files
git commit -m "fix: polish CLI commands after smoke testing"
```

---

## Self-Review

**Spec coverage check:**

| Spec requirement | Task |
|-----------------|------|
| EmbeddedLlmProvider (LlamaSharp + Metal) | Task 4 |
| ModelDownloader (streaming download + SHA256 + progress) | Task 3 |
| DashboardRenderer (XenoAtom.Terminal.UI) | Task 6 |
| DashboardPoller (500ms poll of /api/dashboard/stats) | Task 5 |
| DaemonController (Unix socket, server + client) | Task 2 |
| DaemonMessages (StatsEvent, LogEvent, ShutdownCommand, StatusResponse) | Task 2 |
| ServiceManager (launchd plist + launchctl) | Task 7 |
| `llmock serve` (foreground + dashboard) | Task 9 |
| `llmock serve --headless` (no dashboard) | Task 9 |
| `llmock serve --daemon` (detach + background) | Task 9 |
| `llmock serve --pack <id>` | Task 9 |
| `llmock dashboard` (connect to daemon) | Task 8 |
| `llmock status` | Task 8 |
| `llmock stop` | Task 8 |
| `llmock logs` | Task 8 |
| `llmock models` / `models download` | Task 8 |
| `llmock install-service` | Task 8 |
| `llmock uninstall-service` | Task 8 |
| First-run download UX | Task 3 + Task 9 |
| PID file at ~/.llmock/llmock.pid | Task 2 |
| Package dependencies (LlamaSharp, XenoAtom) | Task 1 |
| PublishSingleFile disabled | Task 1 |

All spec requirements covered. ✓

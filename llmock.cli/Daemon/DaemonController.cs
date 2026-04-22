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
        var buf = new byte[4096];
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

        var buf = new byte[4096];
        var pending = new StringBuilder();

        while (true)
        {
            var read = await client.ReceiveAsync(buf, ct);
            if (read == 0) break;

            pending.Append(Encoding.UTF8.GetString(buf, 0, read));
            var text = pending.ToString();
            var newlineIdx = text.IndexOf('\n');
            if (newlineIdx >= 0)
                return text[..newlineIdx].Trim();
        }
        return null;
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

        if (File.Exists(SocketPath)) File.Delete(SocketPath);
        if (File.Exists(PidFilePath)) File.Delete(PidFilePath);

        await ValueTask.CompletedTask;
    }
}

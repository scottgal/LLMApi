using Microsoft.Extensions.Hosting;
using Spectre.Console;
using System.Text;

namespace LLMApi.Services;

/// <summary>
/// Hosted service that runs the Spectre.Console live dashboard
/// </summary>
public class DashboardService : BackgroundService
{
    private readonly DashboardMetrics _metrics;
    private readonly IHostApplicationLifetime _lifetime;

    public DashboardService(DashboardMetrics metrics, IHostApplicationLifetime lifetime)
    {
        _metrics = metrics;
        _lifetime = lifetime;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait a moment for the app to start
        await Task.Delay(500, stoppingToken);

        await AnsiConsole.Live(CreateLayout())
            .AutoClear(false)
            .StartAsync(async ctx =>
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    ctx.UpdateTarget(CreateLayout());
                    await Task.Delay(1000, stoppingToken);
                }
            });
    }

    private Layout CreateLayout()
    {
        // btop-style 2x2 grid layout - each panel gets exactly a quarter of the console window
        var layout = new Layout("Root")
            .SplitRows(
                new Layout("TopRow").Size(50),
                new Layout("BottomRow").Size(50));

        layout["TopRow"].SplitColumns(
            new Layout("TopLeft").Size(50),
            new Layout("TopRight").Size(50));

        layout["BottomRow"].SplitColumns(
            new Layout("BottomLeft").Size(50),
            new Layout("BottomRight").Size(50));

        layout["TopRow"]["TopLeft"].Update(CreateLogsPanel());
        layout["TopRow"]["TopRight"].Update(CreateApiConnectionsPanel());
        layout["BottomRow"]["BottomLeft"].Update(CreateSignalRContextsPanel());
        layout["BottomRow"]["BottomRight"].Update(CreateLlmRequestsPanel());

        return layout;
    }

    private Panel CreateLogsPanel()
    {
        var logs = _metrics.GetLogs()
            .Take(8) // Limit to 8 most recent logs to prevent layout overflow
            .ToList();

        var table = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn(new TableColumn("Time").Width(8))
            .AddColumn(new TableColumn("Level").Width(3))
            .AddColumn(new TableColumn("Message").NoWrap());

        foreach (var log in logs)
        {
            var levelColor = log.Level switch
            {
                "Error" => "red",
                "Warning" => "yellow",
                "Information" => "blue",
                _ => "grey"
            };

            // Truncate message to 35 chars max to prevent wrapping
            var message = log.Message.Length > 35
                ? log.Message.Substring(0, 32) + "..."
                : log.Message;

            table.AddRow(
                $"[grey]{log.Timestamp:HH:mm:ss}[/]",
                $"[{levelColor}]{log.Level[..Math.Min(3, log.Level.Length)]}[/]",
                Markup.Escape(message)
            );
        }

        if (logs.Count == 0)
        {
            table.AddRow("[grey]--:--:--[/]", "[grey]---[/]", "[grey]No logs yet[/]");
        }

        return new Panel(table)
            .Header("[yellow]ASP.NET Logs[/]")
            .BorderColor(Color.Yellow);
    }

    private Panel CreateApiConnectionsPanel()
    {
        var connections = _metrics.GetApiConnections().ToList();

        var table = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn(new TableColumn("Method").Width(6))
            .AddColumn(new TableColumn("Path").NoWrap())
            .AddColumn(new TableColumn("Duration").Width(6));

        foreach (var conn in connections.Take(7))
        {
            var duration = (DateTime.UtcNow - conn.StartTime).TotalSeconds;
            var path = conn.Path.Length > 20 ? "..." + conn.Path.Substring(conn.Path.Length - 17) : conn.Path;

            table.AddRow(
                $"[green]{conn.Method}[/]",
                Markup.Escape(path),
                $"[grey]{duration:F1}s[/]"
            );
        }

        if (!connections.Any())
        {
            table.AddRow("[grey]None[/]", "", "");
        }

        return new Panel(table)
            .Header($"[green]API Connections ({connections.Count})[/]")
            .BorderColor(Color.Green);
    }

    private Panel CreateSignalRContextsPanel()
    {
        var contexts = _metrics.GetSignalRContexts().ToList();

        var table = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn(new TableColumn("Context").Width(12).NoWrap())
            .AddColumn(new TableColumn("Conn").Width(4))
            .AddColumn(new TableColumn("Status").Width(7));

        foreach (var ctx in contexts.Take(7))
        {
            var statusColor = ctx.IsActive ? "green" : "grey";
            var statusText = ctx.IsActive ? "Active" : "Stop";
            var name = ctx.Name.Length > 12 ? ctx.Name.Substring(0, 9) + "..." : ctx.Name;

            table.AddRow(
                Markup.Escape(name),
                $"[blue]{ctx.Connections}[/]",
                $"[{statusColor}]{statusText}[/]"
            );
        }

        if (!contexts.Any())
        {
            table.AddRow("[grey]None[/]", "", "");
        }

        return new Panel(table)
            .Header($"[aqua]SignalR Contexts ({contexts.Count})[/]")
            .BorderColor(Color.Aqua);
    }

    private Panel CreateLlmRequestsPanel()
    {
        var requests = _metrics.GetLlmRequests().ToList();

        var table = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn(new TableColumn("Time").Width(8))
            .AddColumn(new TableColumn("Status").Width(3))
            .AddColumn(new TableColumn("Prompt").NoWrap());

        foreach (var req in requests.Take(8))
        {
            var color = req.IsError ? "red" : "green";
            var prefix = req.IsError ? "ERR" : req.Response != null ? "OK" : "...";
            var prompt = req.Prompt.Length > 25 ? req.Prompt.Substring(0, 22) + "..." : req.Prompt;

            table.AddRow(
                $"[grey]{req.Timestamp:HH:mm:ss}[/]",
                $"[{color}]{prefix}[/]",
                Markup.Escape(prompt)
            );
        }

        if (!requests.Any())
        {
            table.AddRow("[grey]--:--:--[/]", "[grey]---[/]", "[grey]No requests yet[/]");
        }

        return new Panel(table)
            .Header($"[purple]LLM Requests ({requests.Count})[/]")
            .BorderColor(Color.Purple);
    }
}

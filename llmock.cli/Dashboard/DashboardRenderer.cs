using System.Text;

namespace LLMock.Cli.Dashboard;

/// <summary>
/// Renders a btop-style live dashboard to the console using box-drawing characters.
/// Uses a clear-and-rewrite approach (no flicker if done correctly).
/// </summary>
public class DashboardRenderer
{
    private readonly DashboardPoller _poller;

    // Box-drawing constants
    private const string TL = "┌";
    private const string TR = "┐";
    private const string BL = "└";
    private const string BR = "┘";
    private const string H  = "─";
    private const string V  = "│";
    private const string ML = "├";
    private const string MR = "┤";

    public DashboardRenderer(DashboardPoller poller)
    {
        _poller = poller;
    }

    /// <summary>
    /// Runs the live dashboard until cancellation or the user presses 'q'.
    /// </summary>
    public async Task RunAsync(CancellationToken ct)
    {
        Console.CursorVisible = false;
        Console.Clear();

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

            var pollerTask = _poller.RunAsync(Render, cts.Token);

            // Handle keyboard input on main loop
            while (!cts.Token.IsCancellationRequested)
            {
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(intercept: true);
                    if (key.KeyChar == 'q' || key.Key == ConsoleKey.Q ||
                        key.Key == ConsoleKey.Escape)
                    {
                        await cts.CancelAsync();
                        break;
                    }
                }

                try { await Task.Delay(50, cts.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }

            try { await pollerTask; } catch (OperationCanceledException) { /* expected */ }
        }
        finally
        {
            Console.CursorVisible = true;
            Console.SetCursorPosition(0, Console.WindowHeight - 1);
            Console.ResetColor();
            Console.WriteLine();
        }
    }

    private void Render()
    {
        var state = _poller.Current;
        var width = Math.Max(70, Console.WindowWidth - 1);

        Console.SetCursorPosition(0, 0);

        var lines = BuildLines(state, width);
        var sb = new StringBuilder();
        foreach (var line in lines)
            sb.AppendLine(line);

        // Write everything in one call to reduce flicker
        Console.Write(sb.ToString());
    }

    private List<string> BuildLines(DashboardState state, int width)
    {
        var lines = new List<string>();

        // ── Top border ─────────────────────────────────────────────────────────
        // Title bar: ┌─ LLMock ──────────────── :5555 ──────────────────────────┐
        var portLabel = $":{state.Port}";
        var title = " LLMock ";
        var innerWidth = width - 2; // minus the two side chars ┌ and ┐

        // Fill: TL + title segment + fill + portLabel + fill + TR
        var leftLen = title.Length + 2; // " LLMock " + 2 dashes
        var rightLen = portLabel.Length + 2;
        var fillLen = innerWidth - leftLen - rightLen;
        if (fillLen < 0) fillLen = 0;

        lines.Add(TL + H + title + Repeat(H, fillLen / 2) + portLabel + Repeat(H, fillLen - fillLen / 2) + TR);

        // ── Requests/s row ─────────────────────────────────────────────────────
        var sparkline = string.Join("", state.SparklineHistory.TakeLast(40));
        var rpsLabel = $" Requests/s  {sparkline}";
        var packLabel = state.ActivePack != null ? $"Pack: {state.ActivePack}" : "";
        lines.Add(PaddedRow(V, rpsLabel, packLabel, width));

        // ── Contexts summary row ────────────────────────────────────────────────
        var ctxSummary = $" Contexts    {state.ActiveContexts} active · {state.TotalRequests} total · {state.ErrorCount} errors";
        lines.Add(PaddedRow(V, ctxSummary, "", width));

        // ── Model row ──────────────────────────────────────────────────────────
        var latencyStr = state.AvgLatencyMs > 0 ? $" · {state.AvgLatencyMs:0}ms avg" : "";
        var modelRow = $" Model       {state.ModelName}{latencyStr}";
        lines.Add(PaddedRow(V, modelRow, "", width));

        // ── Section divider ────────────────────────────────────────────────────
        var sectionTitle = " Active Contexts ";
        var dividerFill = innerWidth - sectionTitle.Length;
        if (dividerFill < 0) dividerFill = 0;
        lines.Add(ML + sectionTitle + Repeat(H, dividerFill) + MR);

        // ── Context rows ───────────────────────────────────────────────────────
        const int maxContextRows = 8;
        const int nameColWidth = 30;

        if (state.RecentContexts.Count == 0)
        {
            lines.Add(PaddedRow(V, "  (no active contexts)", "", width));
        }
        else
        {
            foreach (var ctx in state.RecentContexts.Take(maxContextRows))
            {
                var name = Truncate(ctx.Name, nameColWidth).PadRight(nameColWidth);
                var calls = $"{ctx.Calls,6} calls";
                var ago = $"{ctx.SecondsSinceLastUse}s ago";
                var ctxLine = $"  {name}  {calls}   {ago}";
                lines.Add(PaddedRow(V, ctxLine, "", width));
            }
        }

        // ── Bottom border ─────────────────────────────────────────────────────
        lines.Add(BL + Repeat(H, innerWidth) + BR);

        // ── Footer ───────────────────────────────────────────────────────────
        lines.Add("  [q] quit");

        // Pad all lines to width to overwrite any leftover chars from a wider previous render
        return lines.Select(l => PadOrTruncate(l, width)).ToList();
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a row: V + leftContent (padded) + rightContent + V
    /// Right content is right-aligned within the inner width.
    /// </summary>
    private static string PaddedRow(string border, string left, string right, int width)
    {
        var innerWidth = width - 2;
        if (right.Length > 0)
        {
            // right-align the right label
            var gap = innerWidth - left.Length - right.Length - 1;
            if (gap < 1) gap = 1;
            var content = left + new string(' ', gap) + right;
            return border + PadOrTruncateInner(content, innerWidth) + border;
        }
        else
        {
            return border + PadOrTruncateInner(left, innerWidth) + border;
        }
    }

    private static string PadOrTruncateInner(string s, int width)
    {
        if (s.Length >= width) return s[..width];
        return s.PadRight(width);
    }

    private static string PadOrTruncate(string s, int width)
    {
        if (s.Length >= width) return s[..width];
        return s.PadRight(width);
    }

    private static string Truncate(string s, int maxLen)
        => s.Length <= maxLen ? s : s[..(maxLen - 1)] + "…";

    private static string Repeat(string ch, int count)
        => count <= 0 ? "" : string.Concat(Enumerable.Repeat(ch, count));
}

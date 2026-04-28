using Microsoft.Extensions.Hosting;
using Symphony.Service.Hosting;

namespace Symphony.Service.Observability;

public sealed class ConsoleDashboard : BackgroundService
{
    private readonly RuntimeStateStore _state;

    public ConsoleDashboard(RuntimeStateStore state)
    {
        _state = state;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            Render(_state.Snapshot());
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }

    private static void Render(RuntimeSnapshot snapshot)
    {
        if (Console.IsOutputRedirected)
        {
            return;
        }

        Console.Clear();
        Console.WriteLine("SYMPHONY STATUS");
        Console.WriteLine($"generated_at={snapshot.GeneratedAt:O} polling={(snapshot.Polling.InProgress ? "in_progress" : "idle")} next_poll={snapshot.Polling.NextPollAt:O}");
        Console.WriteLine($"running={snapshot.Running.Count} retrying={snapshot.Retrying.Count} tokens={snapshot.CodexTotals.TotalTokens} runtime_seconds={snapshot.CodexTotals.SecondsRunning:N1}");
        Console.WriteLine();

        Console.WriteLine("RUNNING");
        foreach (var running in snapshot.Running)
        {
            Console.WriteLine($"{running.IssueIdentifier,-12} state={running.State,-14} session={running.SessionId ?? "-",-20} event={running.LastEvent ?? "-",-20} worker={running.WorkerHost ?? "local"}");
            Console.WriteLine($"  workspace={running.WorkspacePath ?? "-"}");
            Console.WriteLine($"  message={Trim(running.LastMessage)}");
        }

        Console.WriteLine();
        Console.WriteLine("RETRYING");
        foreach (var retry in snapshot.Retrying)
        {
            Console.WriteLine($"{retry.IssueIdentifier,-12} attempt={retry.Attempt} due_at={retry.DueAt:O} error={Trim(retry.Error)}");
        }
    }

    private static string Trim(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "-";
        }

        return value.Length <= 100 ? value : value[..100];
    }
}


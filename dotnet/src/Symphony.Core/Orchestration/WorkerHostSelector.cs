using Symphony.Core.Configuration;

namespace Symphony.Core.Orchestration;

public sealed class WorkerHostSelector
{
    public string? Select(OrchestratorRuntimeState state, SymphonyConfig config, string? preferredWorkerHost = null)
    {
        var configuredHosts = config.Worker.SshHosts
            .Select(host => host.Trim())
            .Where(host => host.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (!string.IsNullOrWhiteSpace(preferredWorkerHost))
        {
            return HasCapacity(state, config, preferredWorkerHost) ? preferredWorkerHost : OrchestratorStateMachine.NoWorkerCapacity;
        }

        if (configuredHosts.Length == 0)
        {
            return null;
        }

        return configuredHosts.FirstOrDefault(host => HasCapacity(state, config, host))
            ?? OrchestratorStateMachine.NoWorkerCapacity;
    }

    public bool HasAnyCapacity(OrchestratorRuntimeState state, SymphonyConfig config)
    {
        return Select(state, config) != OrchestratorStateMachine.NoWorkerCapacity;
    }

    private static bool HasCapacity(OrchestratorRuntimeState state, SymphonyConfig config, string host)
    {
        if (config.Worker.MaxConcurrentAgentsPerHost is not { } limit)
        {
            return true;
        }

        var used = state.Running.Values.Count(running =>
            string.Equals(running.WorkerHost, host, StringComparison.OrdinalIgnoreCase));

        return used < limit;
    }
}

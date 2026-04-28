using Symphony.Core.Configuration;

namespace Symphony.Core.Orchestration;

public sealed class RetryScheduler
{
    public RetryEntry ScheduleFailure(
        OrchestratorRuntimeState state,
        SymphonyConfig config,
        string issueId,
        string identifier,
        int attempt,
        DateTimeOffset now,
        string? error = null,
        string? workerHost = null,
        string? workspacePath = null)
    {
        return new OrchestratorStateMachine().ScheduleRetry(
            state,
            config,
            issueId,
            identifier,
            attempt,
            now,
            RetryDelayType.Failure,
            error,
            workerHost,
            workspacePath);
    }

    public RetryEntry ScheduleContinuation(
        OrchestratorRuntimeState state,
        SymphonyConfig config,
        string issueId,
        string identifier,
        DateTimeOffset now,
        string? workerHost = null,
        string? workspacePath = null)
    {
        return new OrchestratorStateMachine().ScheduleRetry(
            state,
            config,
            issueId,
            identifier,
            1,
            now,
            RetryDelayType.Continuation,
            null,
            workerHost,
            workspacePath);
    }
}

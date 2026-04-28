using Symphony.Core.Configuration;
using Symphony.Core.Workflow;
using Symphony.Linear;
using Symphony.Workspaces;

namespace Symphony.Service.Hosting;

public sealed class ConfigBackedOptions(WorkflowStore workflowStore, ConfigResolver configResolver)
    : ILinearOptionsProvider, IWorkspaceOptionsProvider
{
    public SymphonyConfig CurrentConfig() => configResolver.Resolve(workflowStore.ReloadIfChanged());

    public LinearOptions GetLinearOptions()
    {
        var config = CurrentConfig();
        return new LinearOptions
        {
            Endpoint = config.Tracker.Endpoint,
            ApiKey = config.Tracker.ApiKey,
            ProjectSlug = config.Tracker.ProjectSlug,
            ActiveStates = config.Tracker.ActiveStates,
            DispatchStates = config.Tracker.DispatchStates,
            TerminalStates = config.Tracker.TerminalStates,
            Assignee = config.Tracker.Assignee
        };
    }

    public WorkspaceOptions GetWorkspaceOptions()
    {
        var config = CurrentConfig();
        return new WorkspaceOptions
        {
            Root = config.Workspace.Root,
            Hooks = new WorkspaceHooks
            {
                AfterCreate = config.Hooks.AfterCreate,
                BeforeRun = config.Hooks.BeforeRun,
                AfterRun = config.Hooks.AfterRun,
                BeforeRemove = config.Hooks.BeforeRemove,
                TimeoutMs = config.Hooks.TimeoutMs
            }
        };
    }
}

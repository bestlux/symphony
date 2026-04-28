namespace Symphony.Service.Hosting;

internal static class WorkflowStatePreflight
{
    public static readonly string[] RequiredStates =
    [
        "Backlog",
        "Todo",
        "In Progress",
        "Human Review",
        "Merging",
        "Rework",
        "Done",
        "Duplicate"
    ];

    public static readonly IReadOnlyList<IReadOnlyList<string>> RequiredTerminalAlternatives =
    [
        ["Canceled", "Cancelled"]
    ];
}

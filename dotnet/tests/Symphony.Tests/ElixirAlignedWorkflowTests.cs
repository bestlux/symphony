using System.Diagnostics;
using System.Net;
using System.Text;
using Symphony.Abstractions.Issues;
using Symphony.Core.Configuration;
using Symphony.Core.Orchestration;
using Symphony.Core.Prompts;
using Symphony.Core.Workflow;
using Symphony.Linear;
using Symphony.Service.Hosting;
using Symphony.Service.Observability;
using Symphony.Workspaces;
using Xunit;

namespace Symphony.Tests;

public sealed class ElixirAlignedWorkflowTests
{
    [Fact]
    public void TodoClaimMovesRunKindToInProgress()
    {
        var runKind = RunKind.FromIssueState("Todo");

        Assert.Equal("In Progress", runKind.StartState);
        Assert.Equal("In Progress", runKind.ContinueWhileState);
    }

    [Theory]
    [InlineData("In Progress")]
    [InlineData("Rework")]
    [InlineData("Merging")]
    public void ContinuingStatesDoNotMoveOnDispatch(string state)
    {
        var runKind = RunKind.FromIssueState(state);

        Assert.Null(runKind.StartState);
        Assert.Equal(state, runKind.ContinueWhileState);
    }

    [Fact]
    public void HumanReviewIsNotDispatched()
    {
        var config = TestConfig();
        var orchestrator = new SymphonyOrchestrator(config, DateTimeOffset.UnixEpoch);

        var decisions = orchestrator.ChooseDispatches(
            config,
            [Issue("1", "IOM-1", "Human Review")],
            DateTimeOffset.UnixEpoch);

        Assert.Empty(decisions);
    }

    [Fact]
    public void HumanReviewIsNotDispatchedEvenWhenWorkflowConfigIncludesIt()
    {
        var config = TestConfig() with
        {
            Tracker = TestConfig().Tracker with
            {
                ActiveStates = ["Todo", "In Progress", "Human Review", "Merging", "Rework"],
                DispatchStates = ["Todo", "In Progress", "Human Review", "Merging", "Rework"]
            }
        };
        var orchestrator = new SymphonyOrchestrator(config, DateTimeOffset.UnixEpoch);

        var decisions = orchestrator.ChooseDispatches(
            config,
            [Issue("1", "IOM-1", "Human Review")],
            DateTimeOffset.UnixEpoch);

        Assert.Empty(decisions);
    }

    [Fact]
    public void TodoMergingAndReworkAreDispatched()
    {
        var config = TestConfig();
        var orchestrator = new SymphonyOrchestrator(config, DateTimeOffset.UnixEpoch);

        var decisions = orchestrator.ChooseDispatches(
            config,
            [
                Issue("1", "IOM-1", "Todo"),
                Issue("2", "IOM-2", "Merging"),
                Issue("3", "IOM-3", "Rework")
            ],
            DateTimeOffset.UnixEpoch);

        Assert.Equal(new[] { "IOM-1", "IOM-2", "IOM-3" }, decisions.Select(decision => decision.Issue.Identifier).ToArray());
    }

    [Fact]
    public void ConfigDefaultsUseElixirDispatchStates()
    {
        var workflow = new WorkflowDefinition(
            new Dictionary<string, object?>
            {
                ["tracker"] = new Dictionary<string, object?>
                {
                    ["kind"] = "linear",
                    ["api_key"] = "token",
                    ["project_slug"] = "symphony"
                }
            },
            "Prompt",
            Path.Combine(Environment.CurrentDirectory, "WORKFLOW.md"),
            DateTimeOffset.UnixEpoch);

        var config = new ConfigResolver().Resolve(workflow);

        Assert.Equal(new[] { "Todo", "In Progress", "Merging", "Rework" }, config.Tracker.ActiveStates);
        Assert.Equal(new[] { "Todo", "In Progress", "Merging", "Rework" }, config.Tracker.DispatchStates);
    }

    [Fact]
    public void ReviewPacketUsesWorkpadAndPrAttachmentEvidence()
    {
        var issue = new Issue(
            "1",
            "IOM-1",
            "Test issue",
            "Test body",
            1,
            "Human Review",
            "codex/iom-1",
            "https://linear.app/iomancer/issue/IOM-1",
            [],
            [],
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            null,
            true,
            [
                new IssueComment(
                    "comment-1",
                    """
                    ## Codex Workpad

                    ### Plan
                    - [x] Implement packet API

                    ### Acceptance Criteria
                    - [x] Operator shows packet fields

                    ### Validation
                    - [x] targeted tests: `.\scripts\validate-symphony.ps1` passed

                    ### Notes

                    ### Summary
                    - Adds a structured review packet API.

                    ### Changed Files
                    - dotnet/src/Symphony.Service/Observability/HttpApi.cs

                    ### Risks
                    - None known.

                    ### Follow-up Issues
                    - None.
                    """,
                    DateTimeOffset.UnixEpoch,
                    DateTimeOffset.UnixEpoch)
            ],
            [new IssueLink("attachment-1", "PR #123", "https://github.com/bestlux/symphony/pull/123", "github")]);

        var packet = ReviewPacketBuilder.Build(issue, running: null, completed: null);

        Assert.True(packet.ReadyForHumanReview);
        Assert.Equal("complete", packet.WorkpadStatus);
        Assert.Equal("https://github.com/bestlux/symphony/pull/123", packet.PrUrl);
        Assert.Contains("Adds a structured review packet API.", packet.Summary);
        Assert.Contains("dotnet/src/Symphony.Service/Observability/HttpApi.cs", packet.Files);
        Assert.Contains(packet.Validation, item => item.Contains(".\\scripts\\validate-symphony.ps1", StringComparison.Ordinal));
        Assert.Empty(packet.Missing);
    }

    [Fact]
    public void ReviewPacketReportsMissingHumanReviewEvidence()
    {
        var issue = Issue(
            "1",
            "IOM-1",
            "Human Review",
            branchName: "codex/iom-1") with
        {
            Comments =
            [
                new IssueComment(
                    "comment-1",
                    """
                    ## Codex Workpad

                    ### Plan
                    - [ ] Finish validation
                    """,
                    DateTimeOffset.UnixEpoch,
                    DateTimeOffset.UnixEpoch)
            ]
        };

        var packet = ReviewPacketBuilder.Build(issue, running: null, completed: null);

        Assert.False(packet.ReadyForHumanReview);
        Assert.Equal("incomplete (1 unchecked)", packet.WorkpadStatus);
        Assert.Contains("summary", packet.Missing);
        Assert.Contains("changed files", packet.Missing);
        Assert.Contains("validation evidence", packet.Missing);
        Assert.Contains("PR URL", packet.Missing);
        Assert.Contains("completed workpad", packet.Missing);
    }

    [Theory]
    [InlineData("Todo")]
    [InlineData("In Progress")]
    [InlineData("Merging")]
    [InlineData("Rework")]
    public void WorkspaceCleanupPolicyNeverCleansActiveStates(string state)
    {
        var decision = WorkspaceCleanupPolicy.Evaluate(
            state,
            pathExists: true,
            retained: false,
            hasDurableArtifacts: true);

        Assert.False(decision.CanCleanup);
        Assert.Equal("blocked", decision.Outcome);
        Assert.Contains("active", decision.BlockedReason);
    }

    [Fact]
    public void WorkspaceCleanupPolicyRetainsHumanReview()
    {
        var decision = WorkspaceCleanupPolicy.Evaluate(
            "Human Review",
            pathExists: true,
            retained: false,
            hasDurableArtifacts: true);

        Assert.False(decision.CanCleanup);
        Assert.Equal("retained", decision.Outcome);
        Assert.Contains("Human Review", decision.BlockedReason);
    }

    [Theory]
    [InlineData("Done")]
    [InlineData("Duplicate")]
    [InlineData("Canceled")]
    [InlineData("Cancelled")]
    public void WorkspaceCleanupPolicyRequiresArtifactsForTerminalStates(string state)
    {
        var missingArtifacts = WorkspaceCleanupPolicy.Evaluate(
            state,
            pathExists: true,
            retained: false,
            hasDurableArtifacts: false);
        var withArtifacts = WorkspaceCleanupPolicy.Evaluate(
            state,
            pathExists: true,
            retained: false,
            hasDurableArtifacts: true);

        Assert.False(missingArtifacts.CanCleanup);
        Assert.Contains("artifacts", missingArtifacts.BlockedReason);
        Assert.True(withArtifacts.CanCleanup);
        Assert.Equal("eligible", withArtifacts.Outcome);
    }

    [Fact]
    public void CompletedRunSchedulesContinuationWithoutTerminalTransition()
    {
        var config = TestConfig();
        var now = DateTimeOffset.UnixEpoch;
        var orchestrator = new SymphonyOrchestrator(config, now);
        var issue = Issue("1", "IOM-1", "In Progress");

        orchestrator.ChooseDispatches(config, [issue], now);
        orchestrator.MarkCompleted(issue.Id, scheduleContinuationCheck: true, config, now);

        var snapshot = orchestrator.Snapshot();
        Assert.Empty(snapshot.Running);
        Assert.Contains(issue.Id, snapshot.CompletedIssueIds);
        var retry = Assert.Single(snapshot.RetryAttempts);
        Assert.Equal(issue.Id, retry.IssueId);
        Assert.Equal(1, retry.Attempt);
    }

    [Fact]
    public async Task TodoRequiresCleanWorkspace()
    {
        using var temp = new TempDirectory();
        var manager = WorkspaceManagerFor(temp.Path);
        var issue = Issue("todo", "IOM-TODO", "Todo");
        CreateDirtyGitWorkspace(Path.Combine(temp.Path, "IOM-TODO"));

        var ex = await Assert.ThrowsAsync<WorkspaceException>(() =>
            manager.CreateForIssueAsync(issue, TestConfig(temp.Path), workerHost: null, CancellationToken.None));

        Assert.Contains("dirty before dispatch", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("In Progress")]
    [InlineData("Rework")]
    [InlineData("Merging")]
    public async Task ExistingActiveWorkspacesMayBeDirty(string state)
    {
        using var temp = new TempDirectory();
        var manager = WorkspaceManagerFor(temp.Path);
        var identifier = "IOM-" + state.Replace(" ", "-", StringComparison.OrdinalIgnoreCase).ToUpperInvariant();
        var issue = Issue(state, identifier, state);
        CreateDirtyGitWorkspace(Path.Combine(temp.Path, identifier));

        var workspace = await manager.CreateForIssueAsync(issue, TestConfig(temp.Path), workerHost: null, CancellationToken.None);

        Assert.False(workspace.IsClean);
        Assert.Contains("dirty.txt", workspace.Status);
    }

    [Fact]
    public async Task MissingLinearWorkflowStatesProduceClearError()
    {
        const string response = """
            {
              "data": {
                "projects": {
                  "nodes": [
                    {
                      "teams": {
                        "nodes": [
                          {
                            "states": {
                              "nodes": [
                                { "name": "Backlog" },
                                { "name": "Todo" },
                                { "name": "In Progress" },
                                { "name": "Human Review" },
                                { "name": "Merging" },
                                { "name": "Rework" },
                                { "name": "Done" },
                                { "name": "Canceled" }
                              ]
                            }
                          }
                        ]
                      }
                    }
                  ]
                }
              }
            }
            """;
        var tracker = LinearTracker(response);

        var ex = await Assert.ThrowsAsync<LinearException>(() =>
            tracker.ValidateWorkflowStatesAsync(
                WorkflowStatePreflight.RequiredStates,
                WorkflowStatePreflight.RequiredTerminalAlternatives,
                CancellationToken.None));

        Assert.Contains("Duplicate", ex.Message);
        Assert.Contains("symphony", ex.Message);
    }

    [Fact]
    public async Task RequiredLinearWorkflowStatesPassPreflight()
    {
        const string response = """
            {
              "data": {
                "projects": {
                  "nodes": [
                    {
                      "teams": {
                        "nodes": [
                          {
                            "states": {
                              "nodes": [
                                { "name": "Backlog" },
                                { "name": "Todo" },
                                { "name": "In Progress" },
                                { "name": "Human Review" },
                                { "name": "Merging" },
                                { "name": "Rework" },
                                { "name": "Done" },
                                { "name": "Duplicate" },
                                { "name": "Cancelled" }
                              ]
                            }
                          }
                        ]
                      }
                    }
                  ]
                }
              }
            }
            """;
        var tracker = LinearTracker(response);

        await tracker.ValidateWorkflowStatesAsync(
            WorkflowStatePreflight.RequiredStates,
            WorkflowStatePreflight.RequiredTerminalAlternatives,
            CancellationToken.None);
    }

    [Fact]
    public void PromptRendererExposesLinearBranchName()
    {
        var issue = Issue("1", "IOM-1", "Todo", branchName: "codex/iom-1-test");
        var rendered = new PromptRenderer().Render("Branch={{ issue.branch_name }}", issue);

        Assert.Equal("Branch=codex/iom-1-test", rendered);
    }

    private static SymphonyConfig TestConfig(string? workspaceRoot = null)
    {
        return new SymphonyConfig(
            new TrackerConfig(
                "linear",
                "https://api.linear.app/graphql",
                "token",
                "symphony",
                null,
                ["Todo", "In Progress", "Merging", "Rework"],
                ["Todo", "In Progress", "Merging", "Rework"],
                ["Closed", "Cancelled", "Canceled", "Duplicate", "Done"]),
            new PollingConfig(1_000),
            new WorkspaceConfig(workspaceRoot ?? Path.Combine(Path.GetTempPath(), "symphony-tests")),
            new WorkerConfig([], null),
            new AgentConfig(10, 20, 300_000, new Dictionary<string, int>()),
            new CodexConfig("codex app-server", new Dictionary<string, object?>(), "workspace-write", null, 3_600_000, 5_000, 300_000),
            new HooksConfig(null, null, null, null, 60_000),
            new ObservabilityConfig(true, 1_000, 16),
            new ServerConfig(null, "127.0.0.1"));
    }

    private static Issue Issue(string id, string identifier, string state, string? branchName = null)
    {
        return new Issue(
            id,
            identifier,
            "Test issue",
            "Test body",
            1,
            state,
            branchName,
            "https://linear.app/iomancer/issue/" + identifier,
            [],
            [],
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            null,
            true);
    }

    private static WorkspaceManager WorkspaceManagerFor(string root)
    {
        return new WorkspaceManager(new WorkspaceOptions
        {
            Root = root,
            Hooks = new WorkspaceHooks { TimeoutMs = 60_000 }
        });
    }

    private static LinearTrackerClient LinearTracker(string response)
    {
        var httpClient = new HttpClient(new StubHandler(response));
        var options = new LinearOptions
        {
            Endpoint = "https://linear.test/graphql",
            ApiKey = "token",
            ProjectSlug = "symphony"
        };
        return new LinearTrackerClient(new LinearClient(httpClient, options), options);
    }

    private static void CreateDirtyGitWorkspace(string path)
    {
        Directory.CreateDirectory(path);
        Run("git", "init", path);
        Run("git", "config user.email test@example.com", path);
        Run("git", "config user.name Tests", path);
        File.WriteAllText(Path.Combine(path, "README.md"), "seed");
        Run("git", "add README.md", path);
        Run("git", "commit -m seed", path);
        File.WriteAllText(Path.Combine(path, "dirty.txt"), "dirty");
    }

    private static void Run(string fileName, string arguments, string workingDirectory)
    {
        using var process = Process.Start(new ProcessStartInfo(fileName, arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException($"Could not start {fileName} {arguments}");

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"{fileName} {arguments} failed with {process.ExitCode}. Output={output} Error={error}");
        }
    }

    private sealed class StubHandler(string response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var message = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(message);
        }
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "symphony-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(Path, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(entry, FileAttributes.Normal);
                }

                Directory.Delete(Path, recursive: true);
            }
        }
    }
}

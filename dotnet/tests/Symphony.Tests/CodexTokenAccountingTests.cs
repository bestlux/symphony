using System.Text.Json.Nodes;
using Symphony.Abstractions.Issues;
using Symphony.Codex;
using Symphony.Core.Configuration;
using Symphony.Core.Orchestration;
using Symphony.Service.Hosting;
using Xunit;
using AccountingUpdate = Symphony.Abstractions.Runtime.CodexRuntimeUpdate;
using AccountingUsage = Symphony.Abstractions.Runtime.CodexTokenUsage;

namespace Symphony.Tests;

public sealed class CodexTokenAccountingTests
{
    [Fact]
    public void ThreadTokenUsageUpdatedMapsAbsoluteTokenUsageTotal()
    {
        var payload = JsonNode.Parse(
            """
            {
              "method": "thread/tokenUsage/updated",
              "params": {
                "tokenUsage": {
                  "total": {
                    "input_tokens": 100,
                    "output_tokens": 25,
                    "total_tokens": 125
                  },
                  "last": {
                    "input_tokens": 5,
                    "output_tokens": 2,
                    "total_tokens": 7
                  }
                }
              }
            }
            """)!.AsObject();

        var update = CodexEventMapper.Map(
            "notification",
            payload,
            raw: null,
            processId: 1234,
            workerHost: "worker-a",
            sessionId: "session-1",
            threadId: "thread-1",
            turnId: "turn-1");

        Assert.Equal("thread/tokenUsage/updated", update.Event);
        Assert.Equal(100, update.InputTokens);
        Assert.Equal(25, update.OutputTokens);
        Assert.Equal(125, update.TotalTokens);
    }

    [Fact]
    public void TurnCompletedGenericUsageDoesNotProduceTokenAccountingUpdate()
    {
        var payload = JsonNode.Parse(
            """
            {
              "method": "turn/completed",
              "params": {
                "usage": {
                  "input_tokens": 100,
                  "output_tokens": 25,
                  "total_tokens": 125
                }
              }
            }
            """)!.AsObject();

        var update = CodexEventMapper.Map(
            "turn_completed",
            payload,
            raw: null,
            processId: 1234,
            workerHost: "worker-a",
            sessionId: "session-1",
            threadId: "thread-1",
            turnId: "turn-1");

        Assert.Equal(0, update.InputTokens);
        Assert.Equal(0, update.OutputTokens);
        Assert.Equal(0, update.TotalTokens);
    }

    [Fact]
    public void TokenCountEventMapsNestedAbsoluteTotalTokenUsage()
    {
        var payload = JsonNode.Parse(
            """
            {
              "method": "codex/event/token_count",
              "params": {
                "msg": {
                  "payload": {
                    "info": {
                      "total_token_usage": {
                        "input_tokens": 200,
                        "output_tokens": 45,
                        "total_tokens": 245
                      },
                      "last_token_usage": {
                        "input_tokens": 10,
                        "output_tokens": 5,
                        "total_tokens": 15
                      }
                    }
                  }
                }
              }
            }
            """)!.AsObject();

        var update = CodexEventMapper.Map(
            "notification",
            payload,
            raw: null,
            processId: 1234,
            workerHost: "worker-a",
            sessionId: "session-1",
            threadId: "thread-1",
            turnId: "turn-1");

        Assert.Equal("codex/event/token_count", update.Event);
        Assert.Equal(200, update.InputTokens);
        Assert.Equal(45, update.OutputTokens);
        Assert.Equal(245, update.TotalTokens);
    }

    [Fact]
    public void TokenUsageTotalsUsePerThreadHighWaterMarks()
    {
        var config = TestConfig();
        var now = DateTimeOffset.UnixEpoch;
        var orchestrator = new SymphonyOrchestrator(config, now);
        var issue = Issue("issue-1", "IOM-1", "In Progress");

        orchestrator.ChooseDispatches(config, [issue], now);
        orchestrator.IntegrateCodexUpdate(issue.Id, TokenUpdate("thread-1", 100, 25, 125));
        orchestrator.IntegrateCodexUpdate(issue.Id, TokenUpdate("thread-1", 100, 25, 125));
        orchestrator.IntegrateCodexUpdate(issue.Id, TokenUpdate("thread-1", 90, 20, 110));
        orchestrator.IntegrateCodexUpdate(issue.Id, TokenUpdate("thread-2", 10, 5, 15));
        orchestrator.IntegrateCodexUpdate(issue.Id, TokenUpdate("thread-1", 110, 30, 140));

        var snapshot = orchestrator.Snapshot();
        var running = Assert.Single(snapshot.Running);
        Assert.Equal(120, running.CodexInputTokens);
        Assert.Equal(35, running.CodexOutputTokens);
        Assert.Equal(155, running.CodexTotalTokens);
        Assert.Equal(120, snapshot.CodexTotals.InputTokens);
        Assert.Equal(35, snapshot.CodexTotals.OutputTokens);
        Assert.Equal(155, snapshot.CodexTotals.TotalTokens);
    }

    [Fact]
    public void RuntimeStateStoreExposesNonzeroTokenTotalsFromCoreSnapshot()
    {
        var config = TestConfig();
        var now = DateTimeOffset.UnixEpoch;
        var orchestrator = new SymphonyOrchestrator(config, now);
        var issue = Issue("issue-1", "IOM-1", "In Progress");

        orchestrator.ChooseDispatches(config, [issue], now);
        orchestrator.IntegrateCodexUpdate(issue.Id, TokenUpdate("thread-1", 100, 25, 125));

        var store = new RuntimeStateStore();
        store.SetFromCore(orchestrator.Snapshot());

        var state = store.Snapshot();
        var running = Assert.Single(state.Running);
        Assert.Equal(125, state.CodexTotals.TotalTokens);
        Assert.Equal(125, running.TotalTokens);
    }

    private static AccountingUpdate TokenUpdate(
        string threadId,
        long inputTokens,
        long outputTokens,
        long totalTokens)
    {
        return new AccountingUpdate(
            "thread/tokenUsage/updated",
            DateTimeOffset.UnixEpoch,
            ThreadId: threadId,
            TokenUsage: new AccountingUsage(inputTokens, outputTokens, totalTokens));
    }

    private static SymphonyConfig TestConfig()
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
            new WorkspaceConfig(Path.Combine(Path.GetTempPath(), "symphony-tests")),
            new WorkerConfig([], null),
            new AgentConfig(10, 20, 300_000, new Dictionary<string, int>()),
            new CodexConfig("codex app-server", new Dictionary<string, object?>(), "workspace-write", null, 3_600_000, 5_000, 300_000),
            new HooksConfig(null, null, null, null, 60_000),
            new ObservabilityConfig(true, 1_000, 16),
            new ServerConfig(null, "127.0.0.1"));
    }

    private static Issue Issue(string id, string identifier, string state)
    {
        return new Issue(
            id,
            identifier,
            "Test issue",
            "Test body",
            1,
            state,
            null,
            "https://linear.app/iomancer/issue/" + identifier,
            [],
            [],
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            null,
            true);
    }
}

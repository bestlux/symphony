import React, { useEffect, useMemo, useState } from "react";
import { createRoot } from "react-dom/client";
import {
  Activity,
  AlertTriangle,
  CheckCircle2,
  CircleDot,
  ClipboardCheck,
  Clock,
  ExternalLink,
  FileText,
  FolderOpen,
  GitBranch,
  HardDrive,
  Link as LinkIcon,
  ListChecks,
  MessageSquare,
  PlayCircle,
  Power,
  RefreshCcw,
  RotateCcw,
  ShieldCheck,
  Square,
  Timer,
  Trash2,
  Workflow,
  XCircle
} from "lucide-react";
import "./styles.css";

type Tokens = {
  input_tokens: number;
  output_tokens: number;
  total_tokens: number;
};

type RunningItem = {
  issue_id: string;
  issue_identifier: string;
  state: string;
  worker_host?: string;
  workspace_path?: string;
  workspace_base_commit?: string;
  workspace_base_branch?: string;
  workspace_clean?: boolean;
  workspace_status?: string;
  session_id?: string;
  thread_id?: string;
  turn_id?: string;
  codex_app_server_pid?: string;
  turn_count: number;
  retry_attempt?: number;
  last_event?: string;
  last_message?: string;
  last_meaningful_event_category?: string;
  started_at: string;
  last_event_at?: string;
  heartbeat_at?: string;
  heartbeat_age_ms?: number;
  heartbeat_status?: "Active" | "Quiet" | "Stale";
  quiet_threshold_ms?: number;
  stale_threshold_ms?: number;
  stale?: boolean;
  tokens: Tokens;
};

type RetryItem = {
  issue_id: string;
  issue_identifier: string;
  attempt: number;
  due_at: string;
  error?: string;
  worker_host?: string;
  workspace_path?: string;
  workspace_base_commit?: string;
  workspace_base_branch?: string;
  workspace_clean?: boolean;
  workspace_status?: string;
};

type CompletedItem = {
  issue_id: string;
  issue_identifier: string;
  state: string;
  status: string;
  worker_host?: string;
  workspace_path?: string;
  workspace_base_commit?: string;
  workspace_base_branch?: string;
  workspace_clean?: boolean;
  workspace_status?: string;
  session_id?: string;
  thread_id?: string;
  turn_id?: string;
  turn_count: number;
  last_event?: string;
  last_message?: string;
  error?: string;
  started_at: string;
  completed_at: string;
  cleanup_outcome: string;
  tokens: Tokens;
};

type SymphonyState = {
  generated_at: string;
  counts: { running: number; retrying: number; completed: number };
  running: RunningItem[];
  retrying: RetryItem[];
  completed: CompletedItem[];
  polling?: {
    in_progress: boolean;
    last_poll_at?: string;
    next_poll_at?: string;
  };
  heartbeat?: {
    quiet_threshold_ms: number;
    stale_threshold_ms: number;
  };
  codex_totals: {
    input_tokens: number;
    output_tokens: number;
    total_tokens: number;
    seconds_running: number;
  };
  rate_limits?: unknown;
};

type Health = {
  status: string;
  generated_at: string;
  running: number;
  retrying: number;
  completed: number;
  operator_actions_available: boolean;
};

type BoardIssue = {
  issue_id: string;
  issue_identifier: string;
  title: string;
  description?: string;
  state: string;
  priority?: number;
  branch_name?: string;
  url?: string;
  labels: string[];
  updated_at?: string;
  created_at?: string;
  review_packet?: ReviewPacket;
  blocked_by?: Array<{ id?: string; identifier?: string; state?: string }>;
};

type BoardLanePayload = {
  state: string;
  issues: BoardIssue[];
};

type BoardPayload = {
  generated_at: string;
  lanes: BoardLanePayload[];
  runtime: SymphonyState;
  dispatch_states: string[];
  active_states: string[];
};

type WorkspaceInventoryItem = {
  issue_id?: string;
  issue_identifier: string;
  title: string;
  state: string;
  workspace_path?: string;
  worker_host?: string;
  branch?: string;
  pr_url?: string;
  workpad_status: string;
  git_clean?: boolean;
  git_status?: string;
  disk_bytes?: number;
  last_activity?: string;
  source: string;
  path_exists: boolean;
  retained: boolean;
  retained_reason?: string;
  retained_at?: string;
  has_run_artifact: boolean;
  has_pr_artifact: boolean;
  has_workpad_artifact: boolean;
  has_durable_artifacts: boolean;
  can_cleanup: boolean;
  cleanup_outcome: string;
  cleanup_blocked_reason: string;
  issue_url?: string;
};

type WorkspaceInventoryPayload = {
  generated_at: string;
  workspace_root: string;
  items: WorkspaceInventoryItem[];
};

type CardKind = "todo" | "in-progress" | "human-review" | "merging" | "rework" | "done";

type WorkCard = {
  key: string;
  kind: CardKind;
  issueId: string;
  identifier: string;
  title: string;
  subtitle: string;
  worker: string;
  workspace: string;
  primaryTime: string;
  tokens: Tokens;
  message: string;
  state: string;
  status: string;
  details: Array<[string, string]>;
};

type Lane = {
  key: CardKind;
  name: string;
  caption: string;
  tone: string;
  icon: React.ReactNode;
  cards: WorkCard[];
};

type ReviewPacket = {
  summary: string[];
  files: string[];
  validation: string[];
  links: string[];
  risks: string[];
  followUps: string[];
  artifact: string[];
  pr_url?: string | null;
  workpad_status: string;
  ready_for_human_review: boolean;
  missing: string[];
  raw: string;
};

type PacketListKey = "summary" | "files" | "validation" | "links" | "risks" | "followUps" | "artifact";

type ReviewItem = {
  key: string;
  issueId: string;
  identifier: string;
  title: string;
  state: string;
  url?: string;
  branch?: string;
  workspace: string;
  reviewerStatus: string;
  lastActivity: string;
  packet: ReviewPacket;
  issue: BoardIssue;
  running?: RunningItem;
  completed?: CompletedItem;
};

type RunState = "active" | "quiet" | "retrying" | "succeeded" | "failed" | "canceled" | "stale";

type RunItem = {
  key: string;
  issueId: string;
  identifier: string;
  lifecycle: RunState;
  status: string;
  state: string;
  worker: string;
  workspace: string;
  sessionId: string;
  threadId: string;
  turnId: string;
  turnCount: number;
  retryAttempt: string;
  lastEvent: string;
  lastMessage: string;
  heartbeatAt?: string;
  heartbeatAgeMs?: number;
  heartbeatLabel: string;
  startedAt?: string;
  completedAt?: string;
  tokens: Tokens;
  rateLimitStatus: string;
  error: string;
  workspaceStatus: string;
  raw: unknown;
  canStop: boolean;
  canRetry: boolean;
  stale: boolean;
  staleReason: string;
};

const laneMeta: Array<Omit<Lane, "cards">> = [
  { key: "todo", name: "Todo", caption: "Queued", tone: "blue", icon: <CircleDot size={16} /> },
  { key: "in-progress", name: "In Progress", caption: "Agent implementation", tone: "green", icon: <PlayCircle size={16} /> },
  { key: "human-review", name: "Human Review", caption: "PR ready for approval", tone: "amber", icon: <ShieldCheck size={16} /> },
  { key: "merging", name: "Merging", caption: "Landing approved PR", tone: "violet", icon: <GitBranch size={16} /> },
  { key: "rework", name: "Rework", caption: "Changes requested", tone: "red", icon: <AlertTriangle size={16} /> },
  { key: "done", name: "Done", caption: "Merged or terminal", tone: "slate", icon: <CheckCircle2 size={16} /> }
];

function App() {
  const [state, setState] = useState<SymphonyState | null>(null);
  const [board, setBoard] = useState<BoardPayload | null>(null);
  const [health, setHealth] = useState<Health | null>(null);
  const [logs, setLogs] = useState<string[]>([]);
  const [workspaces, setWorkspaces] = useState<WorkspaceInventoryPayload | null>(null);
  const [activeTab, setActiveTab] = useState<"board" | "review" | "workspaces" | "runs">("board");
  const [selectedKey, setSelectedKey] = useState<string>("");
  const [selectedReviewKey, setSelectedReviewKey] = useState<string>("");
  const [busyAction, setBusyAction] = useState<string>("");
  const [error, setError] = useState<string>("");
  const [restarting, setRestarting] = useState(false);

  async function load() {
    try {
      setError("");
      const [nextBoard, nextHealth, nextLogs] = await Promise.all([
        fetchJson<BoardPayload>("/api/v1/board"),
        fetchJson<Health>("/api/v1/health"),
        fetchJson<{ lines: string[] }>("/api/v1/logs/recent?count=120")
      ]);
      const nextWorkspaces = await fetchJson<WorkspaceInventoryPayload>("/api/v1/workspaces");
      setBoard(nextBoard);
      setState(nextBoard.runtime);
      setHealth(nextHealth);
      setLogs(nextLogs.lines ?? []);
      setWorkspaces(nextWorkspaces);
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    }
  }

  useEffect(() => {
    void load();
    const timer = window.setInterval(() => void load(), 2500);
    return () => window.clearInterval(timer);
  }, []);

  const lanes = useMemo(() => buildLanes(state, board), [state, board]);
  const cards = lanes.flatMap((lane) => lane.cards);
  const selected = cards.find((card) => card.key === selectedKey) ?? cards[0];
  const reviewItems = useMemo(() => buildReviewItems(board, state), [board, state]);
  const selectedReview = reviewItems.find((item) => item.key === selectedReviewKey) ?? reviewItems[0];

  useEffect(() => {
    if (!selectedKey && cards.length > 0) {
      setSelectedKey(cards[0].key);
    }
  }, [cards, selectedKey]);

  useEffect(() => {
    if ((!selectedReviewKey || !reviewItems.some((item) => item.key === selectedReviewKey)) && reviewItems.length > 0) {
      setSelectedReviewKey(reviewItems[0].key);
    }
  }, [reviewItems, selectedReviewKey]);

  async function runAction(name: string, action: () => Promise<void>) {
    try {
      setBusyAction(name);
      setError("");
      await action();
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    } finally {
      setBusyAction("");
    }
  }

  async function restartService() {
    const running = state?.counts.running ?? 0;
    const warning = running > 0
      ? `Restart Symphony now? ${running} active run${running === 1 ? "" : "s"} will be interrupted.`
      : "Restart Symphony now? The Operator will reconnect when the new service is online.";
    if (!window.confirm(warning)) {
      return;
    }

    try {
      setBusyAction("restart-service");
      setRestarting(true);
      setError("");
      await post("/api/v1/service/restart");
      await waitForRestart();
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    } finally {
      setRestarting(false);
      setBusyAction("");
    }
  }

  return (
    <main className="app-shell">
      <aside className="sidebar">
        <div className="brand-mark">S</div>
        <nav>
          <button className={activeTab === "board" ? "active" : ""} onClick={() => setActiveTab("board")}><Workflow size={18} />Board</button>
          <button className={activeTab === "review" ? "active" : ""} onClick={() => setActiveTab("review")}><ShieldCheck size={18} />Review</button>
          <button className={activeTab === "workspaces" ? "active" : ""} onClick={() => setActiveTab("workspaces")}><HardDrive size={18} />Workspaces</button>
          <button className={activeTab === "runs" ? "active" : ""} onClick={() => setActiveTab("runs")}><Timer size={18} />Runs</button>
        </nav>
      </aside>

      <section className="workspace">
        <header className="topbar">
          <div>
            <p className="eyebrow">local orchestration cockpit</p>
            <h1>Symphony Operator</h1>
          </div>
          <div className="topbar-status">
            <StatusPill tone={health?.operator_actions_available ? "green" : "red"} label={health?.operator_actions_available ? "Actions ready" : "Disconnected"} />
            <StatusPill tone="blue" label="PR-first flow" />
            <StatusPill tone="slate" label={formatCompactTokens(state?.codex_totals.total_tokens ?? 0, "tokens")} />
            <button onClick={() => void restartService()} disabled={busyAction !== "" || restarting}>
              <Power size={16} /> {restarting ? "Restarting" : "Restart"}
            </button>
            <button onClick={() => runAction("refresh", () => post("/api/v1/refresh"))} disabled={busyAction !== ""}>
              <RefreshCcw size={16} /> Refresh
            </button>
          </div>
        </header>

        <section className="hero-strip">
          <div>
            <span>Flow</span>
            <strong>{"Todo -> In Progress -> Human Review -> Merging -> Done"}</strong>
          </div>
          <div>
            <span>Rule</span>
            <strong>Agents open PRs. Humans approve landing.</strong>
          </div>
          <div>
            <span>Generated</span>
            <strong>{state ? formatTime(state.generated_at) : "-"}</strong>
          </div>
        </section>

        {activeTab === "board" ? <div className="content-grid">
          <section className="board-panel">
            <div className="section-heading">
              <div>
                <h2>Workflow Board</h2>
                <p>Linear-backed queue with current runtime overlays.</p>
              </div>
              <div className="metric-row">
                <Metric label="running" value={state?.counts.running ?? 0} />
                <Metric label="retrying" value={state?.counts.retrying ?? 0} />
                <Metric label="run history" value={state?.counts.completed ?? 0} />
              </div>
            </div>

            <div className="kanban">
              {lanes.map((lane) => (
                <section key={lane.key} className={`lane tone-${lane.tone}`}>
                  <header>
                    <div className="lane-title">
                      {lane.icon}
                      <span>{lane.name}</span>
                    </div>
                    <b>{lane.cards.length}</b>
                  </header>
                  <p>{lane.caption}</p>
                  <div className="lane-cards">
                    {lane.cards.length === 0 ? (
                      <div className="empty-card">No work here</div>
                    ) : (
                      lane.cards.map((card) => (
                        <button
                          key={card.key}
                          className={`work-card ${selected?.key === card.key ? "selected" : ""}`}
                          onClick={() => setSelectedKey(card.key)}
                        >
                          <div className="card-topline">
                            <strong>{card.identifier}</strong>
                            <span>{card.primaryTime}</span>
                          </div>
                          <h3>{card.title}</h3>
                          <p>{card.subtitle}</p>
                          <div className="card-footer">
                            <span>{card.worker}</span>
                            <span className="token-count" title={formatTokenTitle(card.tokens)}>{formatCompactTokens(card.tokens.total_tokens)}</span>
                          </div>
                        </button>
                      ))
                    )}
                  </div>
                </section>
              ))}
            </div>
          </section>

          <aside className="inspector">
            {selected ? (
              <>
                <div className="inspector-head">
                  <p className="eyebrow">selection</p>
                  <h2>{selected.identifier}</h2>
                  <span>{selected.issueId}</span>
                </div>

                <div className="action-grid">
                  <button onClick={() => openLinear(selected.identifier)}><ExternalLink size={15} />Open Linear</button>
                  <button onClick={() => runAction("open-workspace", () => openWorkspace(selected.workspace))} disabled={!selected.workspace || busyAction !== ""}><FolderOpen size={15} />Workspace</button>
                  <button
                    onClick={() => runAction("stop", () => post(`/api/v1/runs/${encodeURIComponent(selected.issueId)}/stop`, { cleanup_workspace: false }))}
                    disabled={!selected.key.startsWith("running:") || busyAction !== ""}
                  >
                    <Square size={15} />Stop
                  </button>
                  <button
                    onClick={() => runAction("retry", () => post(`/api/v1/runs/${encodeURIComponent(selected.issueId)}/retry`))}
                    disabled={(!selected.key.startsWith("running:") && !selected.key.startsWith("retry:")) || busyAction !== ""}
                  >
                    <RotateCcw size={15} />Retry
                  </button>
                </div>

                <div className="review-actions">
                  <button
                    onClick={() => runAction("approve-merge", () => moveIssue(selected.issueId, "Merging"))}
                    disabled={selected.kind !== "human-review" || busyAction !== ""}
                  >
                    <ShieldCheck size={15} />Approve for Merge
                  </button>
                  <button
                    onClick={() => runAction("request-rework", () => moveIssue(selected.issueId, "Rework"))}
                    disabled={selected.kind !== "human-review" || busyAction !== ""}
                  >
                    <MessageSquare size={15} />Request Rework
                  </button>
                  <button
                    onClick={() => runAction("cancel", () => moveIssue(selected.issueId, "Canceled"))}
                    disabled={busyAction !== ""}
                  >
                    <XCircle size={15} />Cancel
                  </button>
                  <button
                    onClick={() => runAction("send-human-review", () => moveIssue(selected.issueId, "Human Review"))}
                    disabled={selected.kind !== "in-progress" || busyAction !== ""}
                  >
                    <CheckCircle2 size={15} />Mark Review
                  </button>
                  <small>Human Review waits for approval. Merging dispatches the landing agent through PR and the land skill.</small>
                </div>

                <dl className="details">
                  {selected.details.map(([label, value]) => (
                    <React.Fragment key={label}>
                      <dt>{label}</dt>
                      <dd>{value}</dd>
                    </React.Fragment>
                  ))}
                </dl>

                <div className="message-box">
                  <h3>Latest Message</h3>
                  <pre>{selected.message || "No message captured yet."}</pre>
                </div>
              </>
            ) : (
              <div className="empty-inspector">No Symphony work selected.</div>
            )}
          </aside>
        </div> : activeTab === "review" ? (
          <ReviewWorkspace
            items={reviewItems}
            selected={selectedReview}
            selectedKey={selectedReviewKey}
            busyAction={busyAction}
            onSelect={setSelectedReviewKey}
            onAction={runAction}
          />
        ) : activeTab === "workspaces" ? (
          <WorkspacesWorkspace
            inventory={workspaces}
            busyAction={busyAction}
            onAction={runAction}
          />
        ) : (
          <RunsWorkspace
            state={state}
            health={health}
            busyAction={busyAction}
            onAction={runAction}
          />
        )}

        <section className="bottom-grid">
          <div className="timeline panel">
            <div className="section-heading compact">
              <h2>Activity Timeline</h2>
              <p>Human-readable service and selected-task events.</p>
            </div>
            {buildTimeline(selected, state).map((entry) => (
              <div key={`${entry.time}-${entry.title}`} className="timeline-row">
                <span>{entry.time}</span>
                <i />
                <div>
                  <strong>{entry.title}</strong>
                  <p>{entry.detail}</p>
                </div>
              </div>
            ))}
          </div>

          <div className="logs panel">
            <div className="section-heading compact">
              <h2>Recent Logs</h2>
              <p>Noise-reduced stream from the Service.</p>
            </div>
            <div className="log-list">
              {logs.length === 0 ? <div className="empty-card">No recent log lines</div> : logs.slice(-12).map((line) => <LogLine key={line} line={line} />)}
            </div>
          </div>
        </section>

        {error && <div className="error-toast">{error}</div>}
      </section>
    </main>
  );
}

function ReviewWorkspace({
  items,
  selected,
  selectedKey,
  busyAction,
  onSelect,
  onAction
}: {
  items: ReviewItem[];
  selected: ReviewItem | undefined;
  selectedKey: string;
  busyAction: string;
  onSelect: (key: string) => void;
  onAction: (name: string, action: () => Promise<void>) => void;
}) {
  const review = items.filter((item) => stateToKind(item.state) === "human-review").length;
  const merging = items.filter((item) => stateToKind(item.state) === "merging").length;

  return (
    <section className="review-workspace">
      <div className="review-list panel">
        <div className="section-heading compact">
          <div>
            <h2>Human Review</h2>
            <p>Approval and landing work, sorted by most recently touched.</p>
          </div>
          <div className="metric-row">
            <Metric label="review" value={review} />
            <Metric label="merging" value={merging} />
          </div>
        </div>

        <div className="review-cards">
          {items.length === 0 ? (
            <div className="empty-card">No work is waiting for review.</div>
          ) : (
            items.map((item) => (
              <button
                key={item.key}
                className={`review-card ${selectedKey === item.key ? "selected" : ""}`}
                onClick={() => onSelect(item.key)}
              >
                <div className="card-topline">
                  <strong>{item.identifier}</strong>
                  <StatusPill tone={stateToKind(item.state) === "merging" ? "violet" : "amber"} label={item.state} />
                </div>
                <h3>{item.title}</h3>
                <p>{item.packet.summary[0] ?? (item.packet.missing.length > 0 ? `Missing ${item.packet.missing.join(", ")}` : item.reviewerStatus)}</p>
                <div className="card-footer">
                  <span>{item.packet.pr_url ? "PR linked" : "PR missing"}</span>
                  <span>{item.lastActivity}</span>
                </div>
              </button>
            ))
          )}
        </div>
      </div>

      <div className="review-detail panel">
        {selected ? (
          <>
            <div className="review-titlebar">
              <div>
                <p className="eyebrow">human approval</p>
                <h2>{selected.identifier}: {selected.title}</h2>
                <p>{selected.reviewerStatus}</p>
              </div>
              <div className="review-command-row">
                <button onClick={() => openLinear(selected.identifier)}><ExternalLink size={15} />Open Linear</button>
                <button onClick={() => onAction("open-workspace", () => openWorkspace(selected.workspace))} disabled={!selected.workspace || busyAction !== ""}><FolderOpen size={15} />Open Workspace</button>
              </div>
            </div>

            <div className="review-action-bar">
              <button
                onClick={() => onAction("approve-merge", () => moveIssue(selected.issueId, "Merging"))}
                disabled={stateToKind(selected.state) !== "human-review" || busyAction !== ""}
              >
                <ShieldCheck size={15} />Approve for Merge
              </button>
              <button
                onClick={() => onAction("request-rework", () => moveIssue(selected.issueId, "Rework"))}
                disabled={stateToKind(selected.state) !== "human-review" || busyAction !== ""}
              >
                <MessageSquare size={15} />Request Rework
              </button>
              <button
                onClick={() => onAction("cancel", () => moveIssue(selected.issueId, "Canceled"))}
                disabled={busyAction !== ""}
              >
                <XCircle size={15} />Cancel
              </button>
            </div>

            <div className="review-meta-grid">
              <ReviewFact icon={<ShieldCheck size={16} />} label="Packet" value={selected.packet.ready_for_human_review ? "Ready" : `Missing ${selected.packet.missing.join(", ") || "evidence"}`} />
              <ReviewFact icon={<ClipboardCheck size={16} />} label="Workpad" value={selected.packet.workpad_status || "-"} />
              <ReviewFact icon={<LinkIcon size={16} />} label="PR" value={selected.packet.pr_url ?? "-"} />
              <ReviewFact icon={<GitBranch size={16} />} label="Branch" value={selected.branch ?? "-"} />
              <ReviewFact icon={<FolderOpen size={16} />} label="Workspace" value={selected.workspace || "-"} />
              <ReviewFact icon={<Activity size={16} />} label="Last Activity" value={selected.lastActivity} />
              <ReviewFact icon={<ClipboardCheck size={16} />} label="Runtime" value={selected.completed?.status ?? selected.running?.last_event ?? "Linear queue"} />
            </div>

            <div className="review-sections">
              <ReviewSection icon={<FileText size={16} />} title="Review Packet" items={selected.packet.summary} fallback={selected.packet.raw || selected.issue.description || "No review packet text found."} />
              <ReviewSection icon={<ListChecks size={16} />} title="Changed Files" items={selected.packet.files} fallback="No changed-file list found in the packet." mono />
              <ReviewSection icon={<ClipboardCheck size={16} />} title="Validation Results" items={selected.packet.validation} fallback="No validation result found in the packet." />
              <ReviewSection icon={<LinkIcon size={16} />} title="Workspace / Branch / PR Links" items={[selected.packet.pr_url, ...selected.packet.artifact, ...selected.packet.links].filter((item): item is string => Boolean(item))} fallback={selected.branch ?? selected.url ?? "No artifact or PR link found in the packet."} />
              <ReviewSection icon={<AlertTriangle size={16} />} title="Risks" items={selected.packet.risks} fallback="No risks listed." />
              <ReviewSection icon={<MessageSquare size={16} />} title="Follow-up Issues" items={selected.packet.followUps} fallback="No follow-up issues listed." />
            </div>
          </>
        ) : (
          <div className="empty-inspector">No review work selected.</div>
        )}
      </div>
    </section>
  );
}

function WorkspacesWorkspace({
  inventory,
  busyAction,
  onAction
}: {
  inventory: WorkspaceInventoryPayload | null;
  busyAction: string;
  onAction: (name: string, action: () => Promise<void>) => void;
}) {
  const items = inventory?.items ?? [];
  const [selectedKey, setSelectedKey] = useState("");

  useEffect(() => {
    if ((!selectedKey || !items.some((item) => workspaceKey(item) === selectedKey)) && items.length > 0) {
      setSelectedKey(workspaceKey(items[0]));
    }
  }, [items, selectedKey]);

  const selected = items.find((item) => workspaceKey(item) === selectedKey) ?? items[0];
  const eligible = items.filter((item) => item.can_cleanup).length;
  const retained = items.filter((item) => !item.can_cleanup).length;
  const totalBytes = items.reduce((sum, item) => sum + (item.disk_bytes ?? 0), 0);

  return (
    <section className="workspaces-workspace">
      <div className="workspaces-main panel">
        <div className="section-heading compact">
          <div>
            <h2>Workspaces</h2>
            <p>Local workspace inventory, durable artifacts, and cleanup decisions.</p>
          </div>
          <div className="metric-row">
            <Metric label="workspaces" value={items.length} />
            <Metric label="eligible" value={eligible} />
            <Metric label="retained" value={retained} />
            <Metric label="disk" value={formatBytes(totalBytes)} />
          </div>
        </div>

        <div className="workspace-root-strip">
          <span>Root</span>
          <strong>{inventory?.workspace_root ?? "-"}</strong>
          <span>Generated</span>
          <strong>{inventory ? formatTime(inventory.generated_at) : "-"}</strong>
        </div>

        <div className="workspace-table">
          <div className="workspace-row workspace-header">
            <span>Decision</span>
            <span>Issue</span>
            <span>State</span>
            <span>Artifacts</span>
            <span>Git</span>
            <span>Disk</span>
            <span>Activity</span>
          </div>
          {items.length === 0 ? (
            <div className="empty-card">No workspaces found under the configured root or recent run history.</div>
          ) : (
            items.map((item) => (
              <button
                key={workspaceKey(item)}
                className={`workspace-row ${item.can_cleanup ? "cleanup-ready" : "cleanup-blocked"} ${selected && workspaceKey(selected) === workspaceKey(item) ? "selected" : ""}`}
                onClick={() => setSelectedKey(workspaceKey(item))}
              >
                <StatusPill tone={item.can_cleanup ? "green" : item.retained ? "amber" : "slate"} label={item.cleanup_outcome} />
                <strong>{item.issue_identifier}</strong>
                <span>{item.state}</span>
                <span>{artifactSummary(item)}</span>
                <span>{gitSummary(item)}</span>
                <span>{formatBytes(item.disk_bytes ?? 0)}</span>
                <span>{item.last_activity ? formatDate(item.last_activity) : "-"}</span>
              </button>
            ))
          )}
        </div>
      </div>

      <aside className="workspace-detail panel">
        {selected ? (
          <>
            <div className="workspace-titlebar">
              <div>
                <p className="eyebrow">workspace selection</p>
                <h2>{selected.issue_identifier}</h2>
                <p>{selected.title}</p>
              </div>
              <StatusPill tone={selected.can_cleanup ? "green" : selected.retained ? "amber" : "slate"} label={selected.cleanup_outcome} />
            </div>

            <div className="workspace-action-row">
              <button onClick={() => openLinear(selected.issue_identifier)} disabled={!selected.issue_identifier}><ExternalLink size={15} />Open Issue</button>
              <button onClick={() => selected.pr_url && window.open(selected.pr_url, "_blank", "noopener,noreferrer")} disabled={!selected.pr_url}><LinkIcon size={15} />Open PR</button>
              <button onClick={() => onAction("open-workspace", () => openWorkspace(selected.workspace_path ?? ""))} disabled={!selected.workspace_path || busyAction !== ""}><FolderOpen size={15} />Open Path</button>
              <button onClick={() => onAction("retain-workspace", () => retainWorkspace(selected))} disabled={!selected.workspace_path || selected.retained || busyAction !== ""}><ShieldCheck size={15} />Retain</button>
              <button onClick={() => onAction("cleanup-workspace", () => cleanupWorkspace(selected))} disabled={!selected.can_cleanup || busyAction !== ""}><Trash2 size={15} />Clean Up</button>
            </div>

            {!selected.can_cleanup && (
              <div className="run-alert">
                <AlertTriangle size={16} />
                <strong>{selected.cleanup_blocked_reason}</strong>
              </div>
            )}

            <div className="workspace-fact-grid">
              <WorkspaceFact icon={<FolderOpen size={16} />} label="Path" value={selected.workspace_path ?? "-"} />
              <WorkspaceFact icon={<HardDrive size={16} />} label="Disk" value={formatBytes(selected.disk_bytes ?? 0)} />
              <WorkspaceFact icon={<GitBranch size={16} />} label="Branch" value={selected.branch ?? "-"} />
              <WorkspaceFact icon={<ClipboardCheck size={16} />} label="Git Status" value={selected.git_status ?? "-"} />
              <WorkspaceFact icon={<Activity size={16} />} label="Source" value={selected.source} />
              <WorkspaceFact icon={<Clock size={16} />} label="Last Activity" value={selected.last_activity ? formatDate(selected.last_activity) : "-"} />
              <WorkspaceFact icon={<LinkIcon size={16} />} label="PR URL" value={selected.pr_url ?? "-"} />
              <WorkspaceFact icon={<ClipboardCheck size={16} />} label="Workpad" value={selected.workpad_status} />
              <WorkspaceFact icon={<FileText size={16} />} label="Run Artifact" value={selected.has_run_artifact ? "recorded" : "missing"} />
              <WorkspaceFact icon={<ShieldCheck size={16} />} label="Retention" value={selected.retained ? `${selected.retained_reason ?? "retained"} ${selected.retained_at ? `at ${formatDate(selected.retained_at)}` : ""}` : "not marked retained"} />
            </div>

            <div className="message-box">
              <h3>Cleanup Decision</h3>
              <pre>{selected.cleanup_blocked_reason}</pre>
            </div>
          </>
        ) : (
          <div className="empty-inspector">No workspace selected.</div>
        )}
      </aside>
    </section>
  );
}

function WorkspaceFact({ icon, label, value }: { icon: React.ReactNode; label: string; value: string }) {
  return (
    <div className="workspace-fact">
      {icon}
      <span>{label}</span>
      <strong>{value}</strong>
    </div>
  );
}

function RunsWorkspace({
  state,
  health,
  busyAction,
  onAction
}: {
  state: SymphonyState | null;
  health: Health | null;
  busyAction: string;
  onAction: (name: string, action: () => Promise<void>) => void;
}) {
  const runs = useMemo(() => buildRunItems(state), [state]);
  const [selectedKey, setSelectedKey] = useState("");

  useEffect(() => {
    if ((!selectedKey || !runs.some((run) => run.key === selectedKey)) && runs.length > 0) {
      setSelectedKey(runs[0].key);
    }
  }, [runs, selectedKey]);

  const selected = runs.find((run) => run.key === selectedKey) ?? runs[0];
  const staleCount = runs.filter((run) => run.stale).length;
  const failedCount = runs.filter((run) => run.lifecycle === "failed" || run.lifecycle === "canceled").length;
  const rateLimitStatus = describeRateLimits(state?.rate_limits);

  return (
    <section className="runs-workspace">
      <div className="runs-main panel">
        <div className="section-heading compact">
          <div>
            <h2>Runs</h2>
            <p>Live sessions, retries, terminal runs, capacity, and telemetry.</p>
          </div>
          <div className="metric-row">
            <Metric label="active" value={state?.counts.running ?? 0} />
            <Metric label="retrying" value={state?.counts.retrying ?? 0} />
            <Metric label="completed" value={state?.counts.completed ?? 0} />
            <Metric label="stale" value={staleCount} />
            <Metric label="failed" value={failedCount} />
          </div>
        </div>

        <div className="run-health-strip">
          <div>
            <span>Operator</span>
            <strong>{health?.operator_actions_available ? "controls bound" : "controls disconnected"}</strong>
          </div>
          <div>
            <span>Poll</span>
            <strong>{pollingStatus(state)}</strong>
          </div>
          <div>
            <span>Rate limits</span>
            <strong>{rateLimitStatus}</strong>
          </div>
          <div>
            <span>Tokens</span>
            <strong>{formatNumber(state?.codex_totals.total_tokens ?? 0)}</strong>
          </div>
          <div>
            <span>Heartbeat</span>
            <strong>{heartbeatConfigLabel(state)}</strong>
          </div>
        </div>

        <div className="runs-grid">
          <div className="run-row run-header">
            <span>Status</span>
            <span>Issue</span>
            <span>Last meaningful event</span>
            <span>Heartbeat</span>
            <span>Worker</span>
            <span>Tokens</span>
            <span>Retry</span>
          </div>
          {runs.length === 0 ? (
            <div className="empty-card">No run telemetry has been captured.</div>
          ) : (
            runs.map((run) => (
              <button
                key={run.key}
                className={`run-row lifecycle-${run.lifecycle} ${run.stale ? "stale" : ""} ${selected?.key === run.key ? "selected" : ""}`}
                onClick={() => setSelectedKey(run.key)}
              >
                <StatusPill tone={runTone(run)} label={run.status} />
                <strong>{run.identifier}</strong>
                <span>{run.lastEvent}</span>
                <span>{run.heartbeatLabel}</span>
                <span>{run.worker}</span>
                <span>{formatNumber(run.tokens.total_tokens)}</span>
                <span>{run.retryAttempt}</span>
              </button>
            ))
          )}
        </div>
      </div>

      <aside className="run-detail panel">
        {selected ? (
          <>
            <div className="run-titlebar">
              <div>
                <p className="eyebrow">run selection</p>
                <h2>{selected.identifier}</h2>
                <p>{selected.status} - {selected.lastEvent}</p>
              </div>
              <StatusPill tone={runTone(selected)} label={selected.stale ? "stale" : selected.lifecycle} />
            </div>

            <div className="run-action-row">
              <button onClick={() => openLinear(selected.identifier)}><ExternalLink size={15} />Open Linear</button>
              <button onClick={() => onAction("open-workspace", () => openWorkspace(selected.workspace))} disabled={!selected.workspace || busyAction !== ""}><FolderOpen size={15} />Open Workspace</button>
              <button
                onClick={() => onAction("stop", () => post(`/api/v1/runs/${encodeURIComponent(selected.issueId)}/stop`, { cleanup_workspace: false }))}
                disabled={!selected.canStop || busyAction !== ""}
              >
                <Square size={15} />Stop
              </button>
              <button
                onClick={() => onAction("retry", () => post(`/api/v1/runs/${encodeURIComponent(selected.issueId)}/retry`))}
                disabled={!selected.canRetry || busyAction !== ""}
              >
                <RotateCcw size={15} />Retry
              </button>
            </div>

            {selected.stale && (
              <div className="run-alert">
                <AlertTriangle size={16} />
                <strong>{selected.staleReason}</strong>
              </div>
            )}

            <div className="run-meta-grid">
              <RunFact icon={<Activity size={16} />} label="Lifecycle" value={selected.lifecycle} />
              <RunFact icon={<Clock size={16} />} label="Heartbeat Age" value={selected.heartbeatLabel} />
              <RunFact icon={<Timer size={16} />} label="Started" value={selected.startedAt ? formatDate(selected.startedAt) : "-"} />
              <RunFact icon={<CheckCircle2 size={16} />} label="Completed" value={selected.completedAt ? formatDate(selected.completedAt) : "-"} />
              <RunFact icon={<MessageSquare size={16} />} label="Session" value={selected.sessionId} />
              <RunFact icon={<GitBranch size={16} />} label="Thread" value={selected.threadId} />
              <RunFact icon={<ListChecks size={16} />} label="Turn" value={selected.turnId} />
              <RunFact icon={<RefreshCcw size={16} />} label="Retry Attempt" value={selected.retryAttempt} />
              <RunFact icon={<FolderOpen size={16} />} label="Workspace" value={selected.workspace || "-"} />
              <RunFact icon={<ClipboardCheck size={16} />} label="Workspace Status" value={selected.workspaceStatus} />
              <RunFact icon={<Activity size={16} />} label="Rate Limits" value={selected.rateLimitStatus} />
              <RunFact icon={<FileText size={16} />} label="Error" value={selected.error || "None."} />
            </div>

            <div className="run-telemetry-grid">
              <div className="message-box">
                <h3>Last Meaningful Event</h3>
                <pre>{selected.lastMessage || selected.lastEvent || "No event payload captured."}</pre>
              </div>
              <div className="message-box">
                <h3>Raw Payload</h3>
                <pre>{formatRawPayload(selected.raw)}</pre>
              </div>
            </div>
          </>
        ) : (
          <div className="empty-inspector">No run selected.</div>
        )}
      </aside>
    </section>
  );
}

function RunFact({ icon, label, value }: { icon: React.ReactNode; label: string; value: string }) {
  return (
    <div className="run-fact">
      {icon}
      <span>{label}</span>
      <strong>{value}</strong>
    </div>
  );
}

function ReviewFact({ icon, label, value }: { icon: React.ReactNode; label: string; value: string }) {
  return (
    <div className="review-fact">
      {icon}
      <span>{label}</span>
      <strong>{value}</strong>
    </div>
  );
}

function ReviewSection({
  icon,
  title,
  items,
  fallback,
  mono = false
}: {
  icon: React.ReactNode;
  title: string;
  items: string[];
  fallback: string;
  mono?: boolean;
}) {
  const visibleItems = items.filter(Boolean);
  return (
    <section className="review-section">
      <h3>{icon}{title}</h3>
      {visibleItems.length > 0 ? (
        <ul className={mono ? "mono-list" : undefined}>
          {visibleItems.map((item, index) => <li key={`${title}-${index}`}>{item}</li>)}
        </ul>
      ) : (
        <p>{fallback}</p>
      )}
    </section>
  );
}

async function fetchJson<T>(url: string): Promise<T> {
  const response = await fetch(url);
  if (!response.ok) {
    throw new Error(`${url} failed: ${response.status}`);
  }
  return (await response.json()) as T;
}

async function post(url: string, body?: unknown): Promise<void> {
  const response = await fetch(url, {
    method: "POST",
    headers: body ? { "Content-Type": "application/json" } : undefined,
    body: body ? JSON.stringify(body) : undefined
  });
  if (!response.ok) {
    let message = `${url} failed: ${response.status}`;
    try {
      const payload = await response.json();
      message = payload?.error?.message ?? message;
    } catch {
      // Keep the status-only fallback when the server does not return JSON.
    }
    throw new Error(message);
  }
}

async function waitForRestart(): Promise<void> {
  await delay(1500);
  const deadline = Date.now() + 45_000;
  let lastError = "";

  while (Date.now() < deadline) {
    try {
      const response = await fetch(`/api/v1/health?restart=${Date.now()}`, { cache: "no-store" });
      if (response.ok) {
        return;
      }
      lastError = `health returned ${response.status}`;
    } catch (err) {
      lastError = err instanceof Error ? err.message : String(err);
    }
    await delay(1000);
  }

  throw new Error(`Service restart did not come back online within 45s${lastError ? ` (${lastError})` : ""}.`);
}

function delay(ms: number): Promise<void> {
  return new Promise((resolve) => window.setTimeout(resolve, ms));
}

async function moveIssue(issueId: string, state: string): Promise<void> {
  await post(`/api/v1/issues/${encodeURIComponent(issueId)}/state`, { state });
}

function buildRunItems(state: SymphonyState | null): RunItem[] {
  if (!state) {
    return [];
  }

  const rateLimitStatus = describeRateLimits(state.rate_limits);
  return [
    ...state.running.map((item) => runningRunItem(item, rateLimitStatus)),
    ...state.retrying.map((item) => retryRunItem(item, rateLimitStatus)),
    ...state.completed.map((item) => completedRunItem(item, rateLimitStatus))
  ].sort((left, right) => runSortTime(right) - runSortTime(left));
}

function runningRunItem(item: RunningItem, rateLimitStatus: string): RunItem {
  const heartbeatAt = item.heartbeat_at ?? item.last_event_at ?? item.started_at;
  const heartbeatAgeMs = item.heartbeat_age_ms ?? ageMs(heartbeatAt);
  const heartbeatStatus = item.heartbeat_status ?? localHeartbeatStatus(heartbeatAgeMs, item.quiet_threshold_ms, item.stale_threshold_ms);
  const stale = item.stale ?? heartbeatStatus === "Stale";
  const lifecycle = heartbeatStatus === "Quiet" ? "quiet" : stale ? "stale" : "active";
  return {
    key: `run:active:${item.issue_id}`,
    issueId: item.issue_id,
    identifier: item.issue_identifier,
    lifecycle,
    status: heartbeatStatus,
    state: item.state,
    worker: item.worker_host ?? "worker",
    workspace: item.workspace_path ?? "",
    sessionId: item.session_id ?? "-",
    threadId: item.thread_id ?? "-",
    turnId: item.turn_id ?? "-",
    turnCount: item.turn_count,
    retryAttempt: item.retry_attempt ? String(item.retry_attempt) : "-",
    lastEvent: item.last_meaningful_event_category ?? item.last_event ?? "started",
    lastMessage: item.last_message ?? "",
    heartbeatAt,
    heartbeatAgeMs,
    heartbeatLabel: heartbeatLabel(heartbeatAgeMs, heartbeatAt),
    startedAt: item.started_at,
    tokens: item.tokens,
    rateLimitStatus,
    error: "",
    workspaceStatus: workspaceStatus(item.workspace_status, item.workspace_clean),
    raw: item,
    canStop: true,
    canRetry: true,
    stale,
    staleReason: stale ? staleReason("active run", heartbeatAgeMs, heartbeatAt) : ""
  };
}

function localHeartbeatStatus(
  heartbeatAgeMs: number | undefined,
  quietThresholdMs = 120_000,
  staleThresholdMs = 900_000
): "Active" | "Quiet" | "Stale" {
  if (heartbeatAgeMs === undefined || heartbeatAgeMs > staleThresholdMs) {
    return "Stale";
  }

  return heartbeatAgeMs > quietThresholdMs ? "Quiet" : "Active";
}

function retryRunItem(item: RetryItem, rateLimitStatus: string): RunItem {
  const dueAgeMs = ageMs(item.due_at);
  const overdueMs = dueAgeMs === undefined ? undefined : Math.max(0, dueAgeMs);
  const stale = overdueMs !== undefined && overdueMs > 5 * 60 * 1000;
  return {
    key: `run:retry:${item.issue_id}:${item.attempt}`,
    issueId: item.issue_id,
    identifier: item.issue_identifier,
    lifecycle: stale ? "stale" : "retrying",
    status: stale ? "retry overdue" : "retrying",
    state: "Retrying",
    worker: item.worker_host ?? "worker",
    workspace: item.workspace_path ?? "",
    sessionId: "-",
    threadId: "-",
    turnId: "-",
    turnCount: 0,
    retryAttempt: String(item.attempt),
    lastEvent: item.error ? "retry scheduled after failure" : "retry scheduled",
    lastMessage: item.error ?? "",
    heartbeatAt: item.due_at,
    heartbeatAgeMs: dueAgeMs,
    heartbeatLabel: retryDueLabel(item.due_at),
    tokens: emptyTokens(),
    rateLimitStatus,
    error: item.error ?? "",
    workspaceStatus: workspaceStatus(item.workspace_status, item.workspace_clean),
    raw: item,
    canStop: false,
    canRetry: true,
    stale,
    staleReason: stale ? staleReason("retry", overdueMs, item.due_at) : ""
  };
}

function completedRunItem(item: CompletedItem, rateLimitStatus: string): RunItem {
  const lifecycle = completedLifecycle(item.status);
  return {
    key: `run:completed:${item.issue_id}:${item.completed_at}`,
    issueId: item.issue_id,
    identifier: item.issue_identifier,
    lifecycle,
    status: item.status || lifecycle,
    state: item.state,
    worker: item.worker_host ?? "worker",
    workspace: item.workspace_path ?? "",
    sessionId: item.session_id ?? "-",
    threadId: item.thread_id ?? "-",
    turnId: item.turn_id ?? "-",
    turnCount: item.turn_count,
    retryAttempt: "-",
    lastEvent: item.last_event ?? item.status,
    lastMessage: item.last_message ?? item.error ?? "",
    heartbeatAt: item.completed_at,
    heartbeatAgeMs: ageMs(item.completed_at),
    heartbeatLabel: item.completed_at ? `${formatDuration(ageMs(item.completed_at) ?? 0)} ago` : "-",
    startedAt: item.started_at,
    completedAt: item.completed_at,
    tokens: item.tokens,
    rateLimitStatus,
    error: item.error ?? "",
    workspaceStatus: workspaceStatus(item.workspace_status, item.workspace_clean),
    raw: item,
    canStop: false,
    canRetry: false,
    stale: false,
    staleReason: ""
  };
}

function completedLifecycle(status: string): RunState {
  const normalized = status.toLowerCase();
  if (normalized.includes("cancel")) {
    return "canceled";
  }
  if (normalized.includes("fail") || normalized.includes("stall") || normalized.includes("timeout")) {
    return "failed";
  }
  return "succeeded";
}

function runSortTime(run: RunItem): number {
  return new Date(run.completedAt ?? run.heartbeatAt ?? run.startedAt ?? 0).getTime();
}

function runTone(run: RunItem) {
  if (run.stale || run.lifecycle === "failed") {
    return "red";
  }
  if (run.lifecycle === "retrying") {
    return "amber";
  }
  if (run.lifecycle === "quiet") {
    return "blue";
  }
  if (run.lifecycle === "canceled") {
    return "slate";
  }
  return "green";
}

function pollingStatus(state: SymphonyState | null) {
  if (!state?.polling) {
    return "not reported";
  }

  if (state.polling.in_progress) {
    return "polling now";
  }

  if (state.polling.next_poll_at) {
    return `next ${formatTime(state.polling.next_poll_at)}`;
  }

  return state.polling.last_poll_at ? `last ${formatTime(state.polling.last_poll_at)}` : "idle";
}

function heartbeatConfigLabel(state: SymphonyState | null) {
  if (!state?.heartbeat) {
    return "default thresholds";
  }

  return `quiet ${formatDuration(state.heartbeat.quiet_threshold_ms)}, stale ${formatDuration(state.heartbeat.stale_threshold_ms)}`;
}

function workspaceStatus(status?: string, clean?: boolean) {
  const state = status?.trim() || "unknown";
  if (clean === undefined) {
    return state;
  }
  return `${state}; ${clean ? "clean" : "dirty or unknown"}`;
}

function ageMs(value?: string) {
  if (!value) {
    return undefined;
  }

  const time = new Date(value).getTime();
  return Number.isNaN(time) ? undefined : Date.now() - time;
}

function heartbeatLabel(value?: number, timestamp?: string) {
  if (!timestamp || value === undefined) {
    return "no heartbeat";
  }

  if (value < 0) {
    return `in ${formatDuration(Math.abs(value))}`;
  }

  return `${formatDuration(value)} ago`;
}

function retryDueLabel(value: string) {
  const dueAge = ageMs(value);
  if (dueAge === undefined) {
    return "-";
  }

  return dueAge < 0
    ? `due in ${formatDuration(Math.abs(dueAge))}`
    : `due ${formatDuration(dueAge)} ago`;
}

function staleReason(subject: string, age: number | undefined, timestamp?: string) {
  if (!timestamp || age === undefined) {
    return `${subject} has no heartbeat timestamp.`;
  }

  return `${subject} has had no fresh signal for ${formatDuration(age)}.`;
}

function formatDuration(ms: number) {
  const totalSeconds = Math.max(0, Math.floor(ms / 1000));
  const hours = Math.floor(totalSeconds / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = totalSeconds % 60;

  if (hours > 0) {
    return `${hours}h ${minutes}m`;
  }
  if (minutes > 0) {
    return `${minutes}m ${seconds}s`;
  }
  return `${seconds}s`;
}

function describeRateLimits(rateLimits: unknown) {
  if (!rateLimits) {
    return "no signal";
  }

  const record = asRecord(rateLimits);
  const updated = stringValue(record?.updatedAt) ?? stringValue(record?.updated_at);
  const values = asRecord(record?.values ?? record?.Values);
  const summary = values
    ? Object.entries(values)
        .slice(0, 2)
        .map(([key, value]) => `${humanizeRateLimitKey(key)} ${summarizeUnknown(value)}`)
        .join("; ")
    : "";

  return [updated ? `updated ${formatTime(updated)}` : "reported", summary].filter(Boolean).join(" - ");
}

function humanizeRateLimitKey(value: string) {
  return value
    .replaceAll("_", " ")
    .replaceAll("-", " ")
    .replace(/\s+/g, " ")
    .trim();
}

function summarizeUnknown(value: unknown) {
  const record = asRecord(value);
  const unwrapped = record && ("value" in record || "Value" in record)
    ? record.value ?? record.Value
    : value;
  if (typeof unwrapped === "string" || typeof unwrapped === "number" || typeof unwrapped === "boolean") {
    return String(unwrapped);
  }
  return JSON.stringify(unwrapped);
}

function asRecord(value: unknown): Record<string, unknown> | undefined {
  return value && typeof value === "object" && !Array.isArray(value)
    ? value as Record<string, unknown>
    : undefined;
}

function stringValue(value: unknown) {
  return typeof value === "string" ? value : undefined;
}

function formatRawPayload(value: unknown) {
  return JSON.stringify(value, null, 2) ?? "";
}

function buildReviewItems(board: BoardPayload | null, state: SymphonyState | null): ReviewItem[] {
  if (!board) {
    return [];
  }

  const runningByIssue = new Map((state?.running ?? []).map((item) => [item.issue_id, item]));
  const completedByIssue = new Map<string, CompletedItem>();
  for (const item of (state?.completed ?? [])
    .slice()
    .sort((left, right) => new Date(right.completed_at).getTime() - new Date(left.completed_at).getTime())) {
    if (!completedByIssue.has(item.issue_id)) {
      completedByIssue.set(item.issue_id, item);
    }
  }

  return board.lanes
    .flatMap((lane) => lane.issues)
    .filter((issue) => {
      const kind = stateToKind(issue.state);
      return kind === "human-review" || kind === "merging";
    })
    .map((issue) => {
      const running = runningByIssue.get(issue.issue_id);
      const completed = completedByIssue.get(issue.issue_id);
      const packetText = [issue.description, completed?.last_message, running?.last_message]
        .filter((value): value is string => Boolean(value?.trim()))
        .join("\n\n");
      const packet = issue.review_packet ?? parseReviewPacket(packetText);
      const workspace = running?.workspace_path ?? completed?.workspace_path ?? "";
      const lastActivitySource = running?.last_event_at ?? completed?.completed_at ?? issue.updated_at ?? issue.created_at ?? "";

      return {
        key: `review:${issue.issue_id}`,
        issueId: issue.issue_id,
        identifier: issue.issue_identifier,
        title: issue.title,
        state: issue.state,
        url: issue.url,
        branch: issue.branch_name ?? completed?.workspace_base_branch,
        workspace,
        reviewerStatus: reviewerStatus(issue, running, completed),
        lastActivity: lastActivitySource ? formatDate(lastActivitySource) : "-",
        packet,
        issue,
        running,
        completed
      };
    })
    .sort((left, right) => {
      const leftTime = new Date(left.issue.updated_at ?? left.completed?.completed_at ?? 0).getTime();
      const rightTime = new Date(right.issue.updated_at ?? right.completed?.completed_at ?? 0).getTime();
      return rightTime - leftTime;
    });
}

function reviewerStatus(issue: BoardIssue, running?: RunningItem, completed?: CompletedItem): string {
  if (running && stateToKind(running.state) === "merging") {
    return `Landing active: ${running.last_event ?? "working"}`;
  }

  if (running) {
    return `Agent active: ${running.last_event ?? "working"}`;
  }

  if (completed?.last_message) {
    return `Last agent activity: ${completed.last_event ?? completed.status}`;
  }

  if (stateToKind(issue.state) === "merging") {
    return "Marked Merging; waiting for the landing agent to finish.";
  }

  return "Waiting on human approval.";
}

function parseReviewPacket(text: string): ReviewPacket {
  const packet: ReviewPacket = {
    summary: [],
    files: [],
    validation: [],
    links: [],
    risks: [],
    followUps: [],
    artifact: [],
    workpad_status: text.includes("## Codex Workpad") ? "found" : "missing",
    ready_for_human_review: false,
    missing: [],
    raw: text.trim()
  };

  let current: PacketListKey = "summary";
  for (const rawLine of text.split(/\r?\n/)) {
    const line = rawLine.trim();
    if (!line || line === "---") {
      continue;
    }

    if (/^[-*]\s+\[\s\]/.test(line)) {
      continue;
    }

    const heading = normalizePacketHeading(line);
    if (heading) {
      current = heading.section;
      if (heading.inline) {
        packet[current].push(cleanPacketLine(heading.inline));
      }
      continue;
    }

    const cleaned = cleanPacketLine(line);
    if (!cleaned) {
      continue;
    }

    const inferred = inferPacketSection(cleaned);
    packet[inferred ?? current].push(cleaned);
  }

  packet.links.push(...extractUrls(text));
  return withReadiness(dedupePacket(packet));
}

function normalizePacketHeading(line: string): { section: PacketListKey; inline?: string } | null {
  const isMarkdownHeading = line.trimStart().startsWith("#");
  const normalized = line
    .replace(/^#+\s*/, "")
    .replace(/^\*\*(.*)\*\*$/, "$1")
    .replace(/^[-*]\s*/, "")
    .trim();
  const match = normalized.match(/^([^:]+):\s*(.*)$/);
  if (!match && !isMarkdownHeading) {
    return null;
  }

  const label = (match?.[1] ?? normalized).toLowerCase();
  const inline = match?.[2]?.trim();

  if (label.includes("file")) {
    return { section: "files", inline };
  }
  if (label.includes("validation") || label.includes("test")) {
    return { section: "validation", inline };
  }
  if (label.includes("artifact") || label.includes("workspace") || label.includes("branch") || label.includes("pr url") || label.includes("pull request")) {
    return { section: "artifact", inline };
  }
  if (label.includes("risk") || label.includes("blocker")) {
    return { section: "risks", inline };
  }
  if (label.includes("follow")) {
    return { section: "followUps", inline };
  }
  if (label.includes("link")) {
    return { section: "links", inline };
  }
  if (label.includes("summary")) {
    return { section: "summary", inline };
  }

  return null;
}

function inferPacketSection(line: string): PacketListKey | null {
  const lower = line.toLowerCase();
  if (/^[\w./\\-]+\.(cs|tsx|ts|css|json|slnx|xaml|md)$/i.test(line) || lower.includes("files changed")) {
    return "files";
  }
  if (lower.includes("dotnet build") || lower.includes("npm run") || lower.includes("passed") || lower.includes("failed")) {
    return "validation";
  }
  if (lower.includes("http://") || lower.includes("https://") || lower.includes("branch") || lower.includes("commit") || lower.includes("workspace")) {
    return "artifact";
  }
  if (lower.includes("risk") || lower.includes("blocker")) {
    return "risks";
  }
  if (lower.includes("follow-up") || lower.includes("follow up")) {
    return "followUps";
  }
  return null;
}

function cleanPacketLine(line: string) {
  return line
    .replace(/^[-*]\s*/, "")
    .replace(/^\d+\.\s*/, "")
    .replace(/^`([^`]+)`$/, "$1")
    .trim();
}

function extractUrls(text: string) {
  return Array.from(text.matchAll(/https?:\/\/\S+/g), (match) => match[0].replace(/[),.;]+$/, ""));
}

function firstPrUrl(packet: ReviewPacket) {
  return packet.pr_url ?? [...packet.links, ...packet.artifact].find((item) =>
    /github\.com\/[^/\s]+\/[^/\s]+\/pull\/\d+/i.test(item));
}

function dedupePacket(packet: ReviewPacket): ReviewPacket {
  const dedupe = (items: string[]) => Array.from(new Set(items.map((item) => item.trim()).filter(Boolean)));
  return {
    summary: dedupe(packet.summary),
    files: dedupe(packet.files),
    validation: dedupe(packet.validation),
    links: dedupe(packet.links),
    risks: dedupe(packet.risks),
    followUps: dedupe(packet.followUps),
    artifact: dedupe(packet.artifact),
    pr_url: packet.pr_url,
    workpad_status: packet.workpad_status,
    ready_for_human_review: packet.ready_for_human_review,
    missing: dedupe(packet.missing),
    raw: packet.raw
  };
}

function withReadiness(packet: ReviewPacket): ReviewPacket {
  const prUrl = packet.pr_url ?? firstPrUrl(packet);
  const missing = [
    packet.summary.length === 0 ? "summary" : "",
    packet.files.length === 0 ? "changed files" : "",
    packet.validation.length === 0 ? "validation evidence" : "",
    prUrl ? "" : "PR URL",
    packet.workpad_status === "complete" ? "" : "completed workpad"
  ].filter(Boolean);

  return {
    ...packet,
    pr_url: prUrl,
    missing,
    ready_for_human_review: missing.length === 0
  };
}

function buildLanes(state: SymphonyState | null, board: BoardPayload | null): Lane[] {
  const runningByIssue = new Map((state?.running ?? []).map((item) => [item.issue_id, item]));
  const retryingByIssue = new Map((state?.retrying ?? []).map((item) => [item.issue_id, item]));
  const completedByIssue = latestCompletedByIssue(state);
  const boardIssueIds = new Set<string>();
  const boardCards = board
    ? board.lanes.flatMap((lane) => lane.issues)
        .map((issue) => {
          boardIssueIds.add(issue.issue_id);
          return boardIssueCard(
            issue,
            runningByIssue.get(issue.issue_id),
            retryingByIssue.get(issue.issue_id),
            completedByIssue.get(issue.issue_id));
        })
    : [];
  const runtimeOnlyCards = state
    ? [
        ...state.running.filter((item) => !boardIssueIds.has(item.issue_id)).map(runningCard),
        ...state.retrying
          .filter((item) => !boardIssueIds.has(item.issue_id))
          .map((item) => retryCard(item, completedByIssue.get(item.issue_id)))
      ]
    : [];
  const cards = [...boardCards, ...runtimeOnlyCards];
  return laneMeta.map((lane) => ({
    ...lane,
    cards: cards.filter((card) => card.kind === lane.key)
  }));
}

function latestCompletedByIssue(state: SymphonyState | null): Map<string, CompletedItem> {
  const completedByIssue = new Map<string, CompletedItem>();
  for (const item of (state?.completed ?? [])
    .slice()
    .sort((left, right) => new Date(right.completed_at).getTime() - new Date(left.completed_at).getTime())) {
    if (!completedByIssue.has(item.issue_id)) {
      completedByIssue.set(item.issue_id, item);
    }
  }
  return completedByIssue;
}

function boardIssueCard(
  issue: BoardIssue,
  running?: RunningItem,
  retrying?: RetryItem,
  completed?: CompletedItem
): WorkCard {
  const kind = stateToKind(issue.state);
  const tokens = running?.tokens ?? completed?.tokens ?? emptyTokens();
  const workspace = running?.workspace_path ?? retrying?.workspace_path ?? completed?.workspace_path ?? "";
  const worker = running?.worker_host
    ?? retrying?.worker_host
    ?? completed?.worker_host
    ?? (kind === "human-review" ? "human" : kind === "merging" ? "land" : kind === "rework" ? "agent" : "linear");
  const packet = issue.review_packet ?? parseReviewPacket([issue.description, completed?.last_message, running?.last_message]
    .filter((value): value is string => Boolean(value?.trim()))
    .join("\n\n"));
  const prUrl = packet.pr_url ?? firstPrUrl(packet);
  const validation = packet.validation[0] ?? "-";
  const workpad = packet.workpad_status || "-";
  const lastRun = completed ? `${completed.status} at ${formatDate(completed.completed_at)}` : "-";
  const runtimeStatus = running?.last_event ?? retrying?.error ?? completed?.status ?? "Linear";
  const message = running?.last_message ?? completed?.last_message ?? retrying?.error ?? issue.description ?? "";
  return {
    key: `board:${issue.issue_id}`,
    kind,
    issueId: issue.issue_id,
    identifier: issue.issue_identifier,
    title: issue.title,
    subtitle: running?.last_event ?? retrying?.error ?? (issue.labels.length > 0 ? issue.labels.join(", ") : issue.state),
    worker,
    workspace,
    primaryTime: running?.last_event_at ? formatTime(running.last_event_at) : issue.updated_at ? formatTime(issue.updated_at) : "-",
    tokens,
    message,
    state: issue.state,
    status: runtimeStatus,
    details: [
      ["State", issue.state || "-"],
      ["Priority", issue.priority?.toString() ?? "-"],
      ["Branch", issue.branch_name ?? "-"],
      ...tokenDetails(tokens),
      ["PR", prUrl ?? "-"],
      ["Workpad", workpad],
      ["Review Packet", packet.ready_for_human_review ? "ready" : `missing ${packet.missing.join(", ") || "evidence"}`],
      ["Workspace", workspace || "-"],
      ["Last run", lastRun],
      ["Runtime", runtimeStatus],
      ["Validation", validation],
      ["PR / Landing", completed?.status ?? completed?.cleanup_outcome ?? "-"],
      ["Labels", issue.labels.length > 0 ? issue.labels.join(", ") : "-"],
      ["Updated", issue.updated_at ? formatDate(issue.updated_at) : "-"],
      ["Created", issue.created_at ? formatDate(issue.created_at) : "-"]
    ]
  };
}

function stateToKind(state: string): CardKind {
  const normalized = state.toLowerCase();
  if (normalized === "todo") {
    return "todo";
  }
  if (normalized === "running" || normalized === "in progress") {
    return "in-progress";
  }
  if (normalized === "human review") {
    return "human-review";
  }
  if (normalized === "merging") {
    return "merging";
  }
  if (normalized === "rework" || normalized === "blocked") {
    return "rework";
  }
  if (["done", "closed", "canceled", "cancelled", "duplicate", "merged"].includes(normalized)) {
    return "done";
  }
  return "rework";
}

function runningCard(item: RunningItem): WorkCard {
  const kind = stateToKind(item.state);
  const title = kind === "merging"
    ? "Landing in progress"
    : kind === "rework"
      ? "Rework in progress"
      : "Implementation in progress";
  return {
    key: `running:${item.issue_id}`,
    kind: kind === "done" || kind === "human-review" || kind === "todo" ? "in-progress" : kind,
    issueId: item.issue_id,
    identifier: item.issue_identifier,
    title,
    subtitle: item.last_event ?? item.state,
    worker: item.worker_host ?? "worker",
    workspace: item.workspace_path ?? "",
    primaryTime: formatTime(item.started_at),
    tokens: item.tokens,
    message: item.last_message ?? "",
    state: item.state,
    status: item.last_event ?? "active",
    details: [
      ["State", item.state || "-"],
      ...tokenDetails(item.tokens),
      ["Worker", item.worker_host ?? "-"],
      ["Session", item.session_id ?? "-"],
      ["Thread", item.thread_id ?? "-"],
      ["Turn", item.turn_id ?? "-"],
      ["Retry", item.retry_attempt ? String(item.retry_attempt) : "-"],
      ["Started", formatDate(item.started_at)],
      ["Last event", item.last_event ?? "-"],
      ["Heartbeat", heartbeatLabel(ageMs(item.last_event_at ?? item.started_at), item.last_event_at ?? item.started_at)],
      ["Workspace", item.workspace_path ?? "-"]
    ]
  };
}

function retryCard(item: RetryItem, completed?: CompletedItem): WorkCard {
  const tokens = completed?.tokens ?? emptyTokens();
  return {
    key: `retry:${item.issue_id}`,
    kind: "rework",
    issueId: item.issue_id,
    identifier: item.issue_identifier,
    title: `Retry attempt ${item.attempt}`,
    subtitle: item.error ?? "Retry scheduled",
    worker: item.worker_host ?? "worker",
    workspace: item.workspace_path ?? "",
    primaryTime: formatTime(item.due_at),
    tokens,
    message: item.error ?? "",
    state: "Rework",
    status: "retrying",
    details: [
      ["Attempt", String(item.attempt)],
      ...tokenDetails(tokens),
      ["Due", formatDate(item.due_at)],
      ["Worker", item.worker_host ?? "-"],
      ["Workspace", item.workspace_path ?? "-"],
      ["Error", item.error ?? "-"]
    ]
  };
}

function completedCard(item: CompletedItem): WorkCard {
  const kind = stateToKind(item.state);
  const title = kind === "human-review"
    ? "Ready for human review"
    : kind === "merging"
      ? "Landing run completed"
      : kind === "rework"
        ? "Needs rework"
        : item.status;
  return {
    key: `completed:${item.issue_id}`,
    kind,
    issueId: item.issue_id,
    identifier: item.issue_identifier,
    title,
    subtitle: item.state || item.last_event || item.status,
    worker: item.worker_host ?? "worker",
    workspace: item.workspace_path ?? "",
    primaryTime: formatTime(item.completed_at),
    tokens: item.tokens,
    message: item.last_message ?? item.error ?? "",
    state: item.state,
    status: item.status,
    details: [
      ["State", item.state || "-"],
      ["Status", item.status || "-"],
      ...tokenDetails(item.tokens),
      ["Worker", item.worker_host ?? "-"],
      ["Session", item.session_id ?? "-"],
      ["Started", formatDate(item.started_at)],
      ["Completed", formatDate(item.completed_at)],
      ["Cleanup", item.cleanup_outcome || "-"],
      ["Workspace", item.workspace_path ?? "-"]
    ]
  };
}

function buildTimeline(selected: WorkCard | undefined, state: SymphonyState | null) {
  const rows = [];
  if (selected) {
    rows.push({ time: selected.primaryTime, title: `${selected.identifier} selected`, detail: selected.subtitle });
    rows.push({ time: "now", title: selected.status || "Current status", detail: selected.message || "Awaiting next meaningful event." });
  }
  if (state) {
    rows.push({
      time: formatTime(state.generated_at),
      title: "State refreshed",
      detail: `${state.counts.running} running, ${state.counts.retrying} retrying, ${state.counts.completed} completed`
    });
  }
  return rows;
}

function LogLine({ line }: { line: string }) {
  const parsed = parseLog(line);
  return (
    <div className="log-row">
      <span>{parsed.time}</span>
      <b>{parsed.scope}</b>
      <p>{parsed.message}</p>
    </div>
  );
}

function parseLog(line: string) {
  const match = line.match(/^(\S+)\s+(\S+)\s+(.*)$/);
  if (!match) {
    return { time: "", scope: "service", message: humanize(line) };
  }
  return { time: formatTime(match[1]), scope: match[2], message: humanize(match[3]) };
}

function humanize(value: string) {
  return value
    .replaceAll("notification", "agent event")
    .replaceAll("item/started", "reasoning started")
    .replaceAll("item/completed", "reasoning completed")
    .replaceAll("account/rateLimits/updated", "rate limits updated");
}

function StatusPill({ tone, label }: { tone: string; label: string }) {
  return <span className={`status-pill tone-${tone}`}>{label}</span>;
}

function Metric({ label, value }: { label: string; value: number | string }) {
  return (
    <div className="metric">
      <strong>{value}</strong>
      <span>{label}</span>
    </div>
  );
}

function openLinear(identifier: string) {
  window.open(`https://linear.app/iomancer/issue/${identifier}`, "_blank", "noopener,noreferrer");
}

async function openWorkspace(workspace: string) {
  if (!workspace) {
    return;
  }
  await post("/api/v1/workspaces/open", { path: workspace });
}

async function retainWorkspace(item: WorkspaceInventoryItem) {
  await post("/api/v1/workspaces/retain", {
    issue_id: item.issue_id,
    workspace_path: item.workspace_path
  });
}

async function cleanupWorkspace(item: WorkspaceInventoryItem) {
  await post("/api/v1/workspaces/cleanup", {
    issue_id: item.issue_id,
    workspace_path: item.workspace_path,
    force: false
  });
}

function workspaceKey(item: WorkspaceInventoryItem) {
  return `${item.worker_host ?? "local"}:${item.workspace_path ?? item.issue_identifier}`;
}

function artifactSummary(item: WorkspaceInventoryItem) {
  return [
    item.has_pr_artifact ? "PR" : "",
    item.has_workpad_artifact ? "workpad" : "",
    item.has_run_artifact ? "run" : ""
  ].filter(Boolean).join(" + ") || "missing";
}

function gitSummary(item: WorkspaceInventoryItem) {
  if (item.git_clean === undefined) {
    return item.git_status ?? "unknown";
  }

  return item.git_clean ? "clean" : "dirty";
}

function formatTime(value: string) {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? "-" : date.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });
}

function formatDate(value: string) {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? "-" : date.toLocaleString();
}

function emptyTokens(): Tokens {
  return {
    input_tokens: 0,
    output_tokens: 0,
    total_tokens: 0
  };
}

function tokenDetails(tokens: Tokens): Array<[string, string]> {
  return [
    ["Input tokens", formatNumber(tokens.input_tokens)],
    ["Output tokens", formatNumber(tokens.output_tokens)],
    ["Total tokens", formatNumber(tokens.total_tokens)]
  ];
}

function formatTokenTitle(tokens: Tokens) {
  return `Input ${formatNumber(tokens.input_tokens)} | Output ${formatNumber(tokens.output_tokens)} | Total ${formatNumber(tokens.total_tokens)}`;
}

function formatCompactTokens(value: number, noun = "tok") {
  return `${formatCompactNumber(value)} ${noun}`;
}

function formatCompactNumber(value: number) {
  return new Intl.NumberFormat(undefined, {
    notation: "compact",
    maximumFractionDigits: 1
  }).format(value).toLowerCase();
}

function formatNumber(value: number) {
  return new Intl.NumberFormat().format(value);
}

function formatBytes(value: number) {
  if (value <= 0) {
    return "0 B";
  }

  const units = ["B", "KB", "MB", "GB"];
  let size = value;
  let unit = 0;
  while (size >= 1024 && unit < units.length - 1) {
    size /= 1024;
    unit += 1;
  }

  return `${size.toFixed(unit === 0 ? 0 : 1)} ${units[unit]}`;
}

createRoot(document.getElementById("root")!).render(<App />);

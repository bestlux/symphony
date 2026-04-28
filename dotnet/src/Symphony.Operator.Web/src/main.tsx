import React, { useEffect, useMemo, useState } from "react";
import { createRoot } from "react-dom/client";
import {
  Activity,
  AlertTriangle,
  CheckCircle2,
  CircleDot,
  ClipboardCheck,
  ExternalLink,
  FileText,
  FolderOpen,
  GitBranch,
  Link as LinkIcon,
  ListChecks,
  MessageSquare,
  Pause,
  PlayCircle,
  RefreshCcw,
  RotateCcw,
  ShieldCheck,
  Square,
  Timer,
  Trash2,
  UserCheck,
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
  session_id?: string;
  turn_count: number;
  last_event?: string;
  last_message?: string;
  started_at: string;
  last_event_at?: string;
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
  codex_totals: {
    input_tokens: number;
    output_tokens: number;
    total_tokens: number;
    seconds_running: number;
  };
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

type CardKind = "todo" | "running" | "ready-review" | "reviewing" | "blocked" | "done" | "merged";

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
  tokens: number;
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
  raw: string;
};

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

const laneMeta: Array<Omit<Lane, "cards">> = [
  { key: "todo", name: "Todo", caption: "Queued", tone: "blue", icon: <CircleDot size={16} /> },
  { key: "running", name: "Running", caption: "Implementer active", tone: "green", icon: <PlayCircle size={16} /> },
  { key: "ready-review", name: "Ready for Review", caption: "Needs neutral agent", tone: "amber", icon: <ShieldCheck size={16} /> },
  { key: "reviewing", name: "Reviewing", caption: "Reviewer active", tone: "violet", icon: <Activity size={16} /> },
  { key: "blocked", name: "Blocked", caption: "Needs intervention", tone: "red", icon: <AlertTriangle size={16} /> },
  { key: "done", name: "Done", caption: "Approved, not merged", tone: "slate", icon: <CheckCircle2 size={16} /> },
  { key: "merged", name: "Merged", caption: "Landed in checkout", tone: "green", icon: <GitBranch size={16} /> }
];

function App() {
  const [state, setState] = useState<SymphonyState | null>(null);
  const [board, setBoard] = useState<BoardPayload | null>(null);
  const [health, setHealth] = useState<Health | null>(null);
  const [logs, setLogs] = useState<string[]>([]);
  const [activeTab, setActiveTab] = useState<"board" | "review">("board");
  const [selectedKey, setSelectedKey] = useState<string>("");
  const [selectedReviewKey, setSelectedReviewKey] = useState<string>("");
  const [busyAction, setBusyAction] = useState<string>("");
  const [error, setError] = useState<string>("");

  async function load() {
    try {
      setError("");
      const [nextBoard, nextHealth, nextLogs] = await Promise.all([
        fetchJson<BoardPayload>("/api/v1/board"),
        fetchJson<Health>("/api/v1/health"),
        fetchJson<{ lines: string[] }>("/api/v1/logs/recent?count=120")
      ]);
      setBoard(nextBoard);
      setState(nextBoard.runtime);
      setHealth(nextHealth);
      setLogs(nextLogs.lines ?? []);
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

  return (
    <main className="app-shell">
      <aside className="sidebar">
        <div className="brand-mark">S</div>
        <nav>
          <button className={activeTab === "board" ? "active" : ""} onClick={() => setActiveTab("board")}><Workflow size={18} />Board</button>
          <button className={activeTab === "review" ? "active" : ""} onClick={() => setActiveTab("review")}><ShieldCheck size={18} />Review</button>
          <button disabled><GitBranch size={18} />Workspaces</button>
          <button disabled><Timer size={18} />Runs</button>
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
            <StatusPill tone="blue" label="GPT-5.5 Medium" />
            <StatusPill tone="slate" label={`${formatNumber(state?.codex_totals.total_tokens ?? 0)} tokens`} />
            <button onClick={() => runAction("refresh", () => post("/api/v1/refresh"))} disabled={busyAction !== ""}>
              <RefreshCcw size={16} /> Refresh
            </button>
          </div>
        </header>

        <section className="hero-strip">
          <div>
            <span>Flow</span>
            <strong>{"Todo -> Running -> Ready for Review -> Reviewing -> Blocked -> Done"}</strong>
          </div>
          <div>
            <span>Rule</span>
            <strong>Implementers submit. Neutral reviewers approve.</strong>
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
                            <span>{formatNumber(card.tokens)} tok</span>
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
                    disabled={selected.kind !== "running" || busyAction !== ""}
                  >
                    <Square size={15} />Stop
                  </button>
                  <button
                    onClick={() => runAction("retry", () => post(`/api/v1/runs/${encodeURIComponent(selected.issueId)}/retry`))}
                    disabled={busyAction !== ""}
                  >
                    <RotateCcw size={15} />Retry
                  </button>
                </div>

                <div className="review-actions">
                  <button
                    onClick={() => runAction("start-review", () => moveIssue(selected.issueId, "Reviewing"))}
                    disabled={selected.kind !== "ready-review" || busyAction !== ""}
                  >
                    <ShieldCheck size={15} />Start Review
                  </button>
                  <button
                    onClick={() => runAction("approve", () => moveIssue(selected.issueId, "Done"))}
                    disabled={selected.kind !== "reviewing" && selected.kind !== "ready-review" || busyAction !== ""}
                  >
                    <CheckCircle2 size={15} />Approve
                  </button>
                  <button
                    onClick={() => runAction("block", () => moveIssue(selected.issueId, "Blocked"))}
                    disabled={busyAction !== ""}
                  >
                    <XCircle size={15} />Block
                  </button>
                  <button
                    onClick={() => runAction("request-revision", () => moveIssue(selected.issueId, "Todo"))}
                    disabled={(selected.kind !== "reviewing" && selected.kind !== "ready-review") || busyAction !== ""}
                  >
                    <MessageSquare size={15} />Revise
                  </button>
                  <button
                    onClick={() => runAction("merge", () => mergeIssue(selected.issueId, selected.workspace))}
                    disabled={selected.kind !== "done" || !selected.workspace || busyAction !== ""}
                  >
                    <GitBranch size={15} />Merge
                  </button>
                  <button
                    onClick={() => runAction("cleanup", () => cleanupWorkspace(selected.issueId, selected.workspace))}
                    disabled={selected.kind !== "merged" || !selected.workspace || busyAction !== ""}
                  >
                    <Trash2 size={15} />Cleanup
                  </button>
                  <small>Reviewer agents automatically claim Ready for Review; manual controls are here for intervention.</small>
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
        </div> : (
          <ReviewWorkspace
            items={reviewItems}
            selected={selectedReview}
            selectedKey={selectedReviewKey}
            busyAction={busyAction}
            onSelect={setSelectedReviewKey}
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
  const ready = items.filter((item) => stateToKind(item.state) === "ready-review").length;
  const reviewing = items.filter((item) => stateToKind(item.state) === "reviewing").length;

  return (
    <section className="review-workspace">
      <div className="review-list panel">
        <div className="section-heading compact">
          <div>
            <h2>Review Queue</h2>
            <p>Ready and active review work, sorted by most recently touched.</p>
          </div>
          <div className="metric-row">
            <Metric label="ready" value={ready} />
            <Metric label="reviewing" value={reviewing} />
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
                  <StatusPill tone={stateToKind(item.state) === "reviewing" ? "violet" : "amber"} label={item.state} />
                </div>
                <h3>{item.title}</h3>
                <p>{item.packet.summary[0] ?? item.reviewerStatus}</p>
                <div className="card-footer">
                  <span>{item.workspace ? "workspace linked" : "workspace missing"}</span>
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
                <p className="eyebrow">operator review</p>
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
                onClick={() => onAction("start-review", () => moveIssue(selected.issueId, "Reviewing"))}
                disabled={stateToKind(selected.state) !== "ready-review" || busyAction !== ""}
              >
                <UserCheck size={15} />Start Review
              </button>
              <button
                onClick={() => onAction("approve", () => moveIssue(selected.issueId, "Done"))}
                disabled={busyAction !== ""}
              >
                <CheckCircle2 size={15} />Approve
              </button>
              <button
                onClick={() => onAction("request-revision", () => moveIssue(selected.issueId, "Todo"))}
                disabled={busyAction !== ""}
              >
                <MessageSquare size={15} />Request Revision
              </button>
              <button
                onClick={() => onAction("block", () => moveIssue(selected.issueId, "Blocked"))}
                disabled={busyAction !== ""}
              >
                <XCircle size={15} />Block
              </button>
            </div>

            <div className="review-meta-grid">
              <ReviewFact icon={<GitBranch size={16} />} label="Branch" value={selected.branch ?? "-"} />
              <ReviewFact icon={<FolderOpen size={16} />} label="Workspace" value={selected.workspace || "-"} />
              <ReviewFact icon={<Activity size={16} />} label="Reviewer Activity" value={selected.lastActivity} />
              <ReviewFact icon={<ClipboardCheck size={16} />} label="Runtime" value={selected.completed?.status ?? selected.running?.last_event ?? "Linear queue"} />
            </div>

            <div className="review-sections">
              <ReviewSection icon={<FileText size={16} />} title="Review Packet" items={selected.packet.summary} fallback={selected.packet.raw || selected.issue.description || "No review packet text found."} />
              <ReviewSection icon={<ListChecks size={16} />} title="Changed Files" items={selected.packet.files} fallback="No changed-file list found in the packet." mono />
              <ReviewSection icon={<ClipboardCheck size={16} />} title="Validation Results" items={selected.packet.validation} fallback="No validation result found in the packet." />
              <ReviewSection icon={<LinkIcon size={16} />} title="Workspace / Branch / PR Links" items={[...selected.packet.artifact, ...selected.packet.links]} fallback={selected.branch ?? selected.url ?? "No artifact or PR link found in the packet."} />
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

async function moveIssue(issueId: string, state: string): Promise<void> {
  await post(`/api/v1/issues/${encodeURIComponent(issueId)}/state`, { state });
}

async function mergeIssue(issueId: string, workspace: string): Promise<void> {
  await post(`/api/v1/issues/${encodeURIComponent(issueId)}/merge`, { workspace_path: workspace, cleanup_workspace: false });
}

async function cleanupWorkspace(issueId: string, workspace: string): Promise<void> {
  await post("/api/v1/workspaces/cleanup", { issue_id: issueId, workspace_path: workspace, force: false });
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
      return kind === "ready-review" || kind === "reviewing";
    })
    .map((issue) => {
      const running = runningByIssue.get(issue.issue_id);
      const completed = completedByIssue.get(issue.issue_id);
      const packetText = [issue.description, completed?.last_message, running?.last_message]
        .filter((value): value is string => Boolean(value?.trim()))
        .join("\n\n");
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
        packet: parseReviewPacket(packetText),
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
  if (running && isReviewer(running)) {
    return `Reviewer active: ${running.last_event ?? "working"}`;
  }

  if (completed?.last_message) {
    return `Last implementer activity: ${completed.last_event ?? completed.status}`;
  }

  if (stateToKind(issue.state) === "reviewing") {
    return "Marked Reviewing; no live reviewer session is currently reported.";
  }

  return "Awaiting reviewer pickup.";
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
    raw: text.trim()
  };

  let current: keyof Omit<ReviewPacket, "raw"> = "summary";
  for (const rawLine of text.split(/\r?\n/)) {
    const line = rawLine.trim();
    if (!line || line === "---") {
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
  return dedupePacket(packet);
}

function normalizePacketHeading(line: string): { section: keyof Omit<ReviewPacket, "raw">; inline?: string } | null {
  const normalized = line
    .replace(/^#+\s*/, "")
    .replace(/^\*\*(.*)\*\*$/, "$1")
    .replace(/^[-*]\s*/, "")
    .trim();
  const match = normalized.match(/^([^:]+):\s*(.*)$/);
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

function inferPacketSection(line: string): keyof Omit<ReviewPacket, "raw"> | null {
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
    raw: packet.raw
  };
}

function buildLanes(state: SymphonyState | null, board: BoardPayload | null): Lane[] {
  const activeRuntimeCards = state ? [...state.running.map(runningCard), ...state.retrying.map(retryCard)] : [];
  const activeRuntimeKeys = new Set(activeRuntimeCards.map((card) => card.issueId));
  const completedByIssue = latestCompletedByIssue(state);
  const boardCards = board
    ? board.lanes.flatMap((lane) => lane.issues)
        .filter((issue) => !activeRuntimeKeys.has(issue.issue_id))
        .map((issue) => boardIssueCard(issue, completedByIssue.get(issue.issue_id)))
    : [];
  const cards = [...activeRuntimeCards, ...boardCards];
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

function boardIssueCard(issue: BoardIssue, completed?: CompletedItem): WorkCard {
  const kind = completed && isMergedCompletion(completed) ? "merged" : stateToKind(issue.state);
  const workspace = completed?.workspace_path ?? "";
  return {
    key: `board:${issue.issue_id}`,
    kind,
    issueId: issue.issue_id,
    identifier: issue.issue_identifier,
    title: issue.title,
    subtitle: kind === "merged" ? completed?.cleanup_outcome ?? "merged" : issue.labels.length > 0 ? issue.labels.join(", ") : issue.state,
    worker: kind === "ready-review" || kind === "reviewing" ? "reviewer" : "linear",
    workspace,
    primaryTime: issue.updated_at ? formatTime(issue.updated_at) : "-",
    tokens: completed?.tokens.total_tokens ?? 0,
    message: completed?.last_message ?? issue.description ?? "",
    state: issue.state,
    status: kind === "merged" ? completed?.status ?? "Merged" : "Linear",
    details: [
      ["State", issue.state || "-"],
      ["Priority", issue.priority?.toString() ?? "-"],
      ["Branch", issue.branch_name ?? "-"],
      ["Workspace", workspace || "-"],
      ["Merge", completed?.status === "Merged" ? "merged" : completed?.cleanup_outcome ?? "-"],
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
    return "running";
  }
  if (normalized === "ready for review" || normalized === "in review") {
    return "ready-review";
  }
  if (normalized === "reviewing") {
    return "reviewing";
  }
  if (normalized === "blocked" || normalized === "canceled" || normalized === "cancelled") {
    return "blocked";
  }
  if (normalized === "merged") {
    return "merged";
  }
  return "done";
}

function runningCard(item: RunningItem): WorkCard {
  const reviewer = isReviewer(item);
  return {
    key: `running:${item.issue_id}`,
    kind: reviewer ? "reviewing" : "running",
    issueId: item.issue_id,
    identifier: item.issue_identifier,
    title: reviewer ? "Neutral review in progress" : "Implementation in progress",
    subtitle: item.last_event ?? item.state,
    worker: item.worker_host ?? "worker",
    workspace: item.workspace_path ?? "",
    primaryTime: formatTime(item.started_at),
    tokens: item.tokens.total_tokens,
    message: item.last_message ?? "",
    state: item.state,
    status: item.last_event ?? "active",
    details: [
      ["State", item.state || "-"],
      ["Worker", item.worker_host ?? "-"],
      ["Session", item.session_id ?? "-"],
      ["Started", formatDate(item.started_at)],
      ["Last event", item.last_event ?? "-"],
      ["Workspace", item.workspace_path ?? "-"]
    ]
  };
}

function retryCard(item: RetryItem): WorkCard {
  return {
    key: `retry:${item.issue_id}`,
    kind: "blocked",
    issueId: item.issue_id,
    identifier: item.issue_identifier,
    title: `Retry attempt ${item.attempt}`,
    subtitle: item.error ?? "Retry scheduled",
    worker: item.worker_host ?? "worker",
    workspace: item.workspace_path ?? "",
    primaryTime: formatTime(item.due_at),
    tokens: 0,
    message: item.error ?? "",
    state: "Blocked",
    status: "retrying",
    details: [
      ["Attempt", String(item.attempt)],
      ["Due", formatDate(item.due_at)],
      ["Worker", item.worker_host ?? "-"],
      ["Workspace", item.workspace_path ?? "-"],
      ["Error", item.error ?? "-"]
    ]
  };
}

function completedCard(item: CompletedItem): WorkCard {
  if (isMergedCompletion(item)) {
    return {
      key: `completed:${item.issue_id}`,
      kind: "merged",
      issueId: item.issue_id,
      identifier: item.issue_identifier,
      title: "Merged into checkout",
      subtitle: item.cleanup_outcome || item.status,
      worker: item.worker_host ?? "operator",
      workspace: item.workspace_path ?? "",
      primaryTime: formatTime(item.completed_at),
      tokens: item.tokens.total_tokens,
      message: item.last_message ?? "",
      state: item.state,
      status: item.status,
      details: [
        ["State", item.state || "-"],
        ["Status", item.status || "-"],
        ["Completed", formatDate(item.completed_at)],
        ["Cleanup", item.cleanup_outcome || "-"],
        ["Workspace", item.workspace_path ?? "-"]
      ]
    };
  }

  const status = `${item.status} ${item.state} ${item.error ?? ""}`.toLowerCase();
  const blocked = status.includes("fail")
    || status.includes("block")
    || status.includes("cancel")
    || status.includes("error");
  const ready = item.state.toLowerCase().includes("review") || item.last_message?.toLowerCase().includes("review") || item.status.toLowerCase() === "completed";
  const kind: CardKind = blocked ? "blocked" : ready ? "ready-review" : "done";
  return {
    key: `completed:${item.issue_id}`,
    kind,
    issueId: item.issue_id,
    identifier: item.issue_identifier,
    title: kind === "ready-review" ? "Ready for neutral review" : item.status,
    subtitle: item.state || item.last_event || item.status,
    worker: item.worker_host ?? "worker",
    workspace: item.workspace_path ?? "",
    primaryTime: formatTime(item.completed_at),
    tokens: item.tokens.total_tokens,
    message: item.last_message ?? item.error ?? "",
    state: item.state,
    status: item.status,
    details: [
      ["State", item.state || "-"],
      ["Status", item.status || "-"],
      ["Worker", item.worker_host ?? "-"],
      ["Session", item.session_id ?? "-"],
      ["Started", formatDate(item.started_at)],
      ["Completed", formatDate(item.completed_at)],
      ["Cleanup", item.cleanup_outcome || "-"],
      ["Workspace", item.workspace_path ?? "-"]
    ]
  };
}

function isMergedCompletion(item: CompletedItem): boolean {
  return item.status.toLowerCase() === "merged"
    || item.status.toLowerCase() === "cleaned"
    || item.cleanup_outcome.toLowerCase().includes("merged")
    || item.cleanup_outcome.toLowerCase().includes("cleaned")
    || item.state.toLowerCase() === "merged";
}

function isReviewer(item: RunningItem): boolean {
  const kind = stateToKind(item.state);
  return kind === "ready-review" || kind === "reviewing";
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

function Metric({ label, value }: { label: string; value: number }) {
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

function formatTime(value: string) {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? "-" : date.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });
}

function formatDate(value: string) {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? "-" : date.toLocaleString();
}

function formatNumber(value: number) {
  return new Intl.NumberFormat().format(value);
}

createRoot(document.getElementById("root")!).render(<App />);

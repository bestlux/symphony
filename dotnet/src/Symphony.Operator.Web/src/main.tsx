import React, { useEffect, useMemo, useState } from "react";
import { createRoot } from "react-dom/client";
import {
  Activity,
  AlertTriangle,
  CheckCircle2,
  CircleDot,
  ExternalLink,
  FolderOpen,
  GitBranch,
  Pause,
  PlayCircle,
  RefreshCcw,
  RotateCcw,
  ShieldCheck,
  Square,
  Timer,
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
  session_id?: string;
  thread_id?: string;
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

type CardKind = "todo" | "running" | "ready-review" | "reviewing" | "blocked" | "done";

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

const laneMeta: Array<Omit<Lane, "cards">> = [
  { key: "todo", name: "Todo", caption: "Queued", tone: "blue", icon: <CircleDot size={16} /> },
  { key: "running", name: "Running", caption: "Implementer active", tone: "green", icon: <PlayCircle size={16} /> },
  { key: "ready-review", name: "Ready for Review", caption: "Needs neutral agent", tone: "amber", icon: <ShieldCheck size={16} /> },
  { key: "reviewing", name: "Reviewing", caption: "Reviewer active", tone: "violet", icon: <Activity size={16} /> },
  { key: "blocked", name: "Blocked", caption: "Needs intervention", tone: "red", icon: <AlertTriangle size={16} /> },
  { key: "done", name: "Done", caption: "Reviewed and closed", tone: "slate", icon: <CheckCircle2 size={16} /> }
];

function App() {
  const [state, setState] = useState<SymphonyState | null>(null);
  const [board, setBoard] = useState<BoardPayload | null>(null);
  const [health, setHealth] = useState<Health | null>(null);
  const [logs, setLogs] = useState<string[]>([]);
  const [selectedKey, setSelectedKey] = useState<string>("");
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

  useEffect(() => {
    if (!selectedKey && cards.length > 0) {
      setSelectedKey(cards[0].key);
    }
  }, [cards, selectedKey]);

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
          <a className="active"><Workflow size={18} />Board</a>
          <a><ShieldCheck size={18} />Review</a>
          <a><GitBranch size={18} />Workspaces</a>
          <a><Timer size={18} />Runs</a>
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

        <div className="content-grid">
          <section className="board-panel">
            <div className="section-heading">
              <div>
                <h2>Workflow Board</h2>
                <p>Runtime state now, Linear-backed queue next.</p>
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
                  <button onClick={() => openWorkspace(selected.workspace)} disabled={!selected.workspace}><FolderOpen size={15} />Workspace</button>
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
        </div>

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
    throw new Error(`${url} failed: ${response.status}`);
  }
}

async function moveIssue(issueId: string, state: string): Promise<void> {
  await post(`/api/v1/issues/${encodeURIComponent(issueId)}/state`, { state });
}

function buildLanes(state: SymphonyState | null, board: BoardPayload | null): Lane[] {
  const activeRuntimeCards = state ? [...state.running.map(runningCard), ...state.retrying.map(retryCard)] : [];
  const activeRuntimeKeys = new Set(activeRuntimeCards.map((card) => card.issueId));
  const boardCards = board
    ? board.lanes.flatMap((lane) => lane.issues)
        .filter((issue) => !activeRuntimeKeys.has(issue.issue_id))
        .map(boardIssueCard)
    : [];
  const cards = [...activeRuntimeCards, ...boardCards];
  return laneMeta.map((lane) => ({
    ...lane,
    cards: cards.filter((card) => card.kind === lane.key)
  }));
}

function boardIssueCard(issue: BoardIssue): WorkCard {
  const kind = stateToKind(issue.state);
  return {
    key: `board:${issue.issue_id}`,
    kind,
    issueId: issue.issue_id,
    identifier: issue.issue_identifier,
    title: issue.title,
    subtitle: issue.labels.length > 0 ? issue.labels.join(", ") : issue.state,
    worker: kind === "ready-review" || kind === "reviewing" ? "reviewer" : "linear",
    workspace: "",
    primaryTime: issue.updated_at ? formatTime(issue.updated_at) : "-",
    tokens: 0,
    message: issue.description ?? "",
    state: issue.state,
    status: "Linear",
    details: [
      ["State", issue.state || "-"],
      ["Priority", issue.priority?.toString() ?? "-"],
      ["Branch", issue.branch_name ?? "-"],
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

function isReviewer(item: RunningItem): boolean {
  const haystack = `${item.state} ${item.last_event ?? ""} ${item.last_message ?? ""}`.toLowerCase();
  return haystack.includes("review");
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

function openWorkspace(workspace: string) {
  if (!workspace) {
    return;
  }
  window.alert(`Workspace path:\n${workspace}`);
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

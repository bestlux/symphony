# Service-Hosted Web Operator Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the WPF-first Operator direction with a polished local web cockpit served by `Symphony.Service` at `/operator`.

**Architecture:** Add a Vite React/TypeScript frontend under `dotnet/src/Symphony.Operator.Web` that builds into `dotnet/src/Symphony.Service/wwwroot/operator`. The Service serves the built app and keeps the existing JSON/control API as the cockpit backend. The first web pass renders the approved workflow lanes and wires refresh/stop/retry to real endpoints.

**Tech Stack:** .NET 10 Service, ASP.NET Core static files, Vite, React, TypeScript, CSS.

---

### Task 1: Web App Scaffold

**Files:**
- Create: `dotnet/src/Symphony.Operator.Web/package.json`
- Create: `dotnet/src/Symphony.Operator.Web/index.html`
- Create: `dotnet/src/Symphony.Operator.Web/tsconfig.json`
- Create: `dotnet/src/Symphony.Operator.Web/vite.config.ts`
- Create: `dotnet/src/Symphony.Operator.Web/src/main.tsx`
- Create: `dotnet/src/Symphony.Operator.Web/src/styles.css`

- [x] Scaffold a Vite React app that builds into the Service `wwwroot/operator` folder.
- [x] Model the workflow lanes as `Todo`, `Running`, `Ready for Review`, `Reviewing`, `Blocked`, and `Done`.
- [x] Fetch Service health, state, and recent logs from existing `/api/v1/*` endpoints.

### Task 2: Service Hosting

**Files:**
- Modify: `dotnet/src/Symphony.Service/Program.cs`
- Modify: `dotnet/src/Symphony.Service/Observability/HttpApi.cs`

- [x] Enable ASP.NET Core static file serving.
- [x] Route `/operator` and `/operator/` to the built web app.
- [x] Keep `/` as a simple redirect to `/operator` while preserving API routes.

### Task 3: Cockpit UI

**Files:**
- Modify: `dotnet/src/Symphony.Operator.Web/src/main.tsx`
- Modify: `dotnet/src/Symphony.Operator.Web/src/styles.css`

- [x] Implement the board, inspector, timeline, and log panels.
- [x] Show implementer/reviewer workflow explicitly.
- [x] Wire Refresh, Stop, Retry, Open Workspace, and Open Linear actions where backend support exists.
- [x] Keep future review actions visible but marked as pending backend support.

### Task 4: Validation

**Files:**
- No new source files expected.

- [x] Run `npm install` and `npm run build` in `dotnet/src/Symphony.Operator.Web`.
- [x] Run `dotnet build dotnet/Symphony.slnx`.
- [x] Restart the Service and verify `/operator` loads.
- [x] Capture a screenshot and compare against the concept direction.

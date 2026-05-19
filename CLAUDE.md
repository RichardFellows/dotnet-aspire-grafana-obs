# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

A .NET Aspire 13 distributed application demonstrating observability with OpenTelemetry. Targets **net10.0**. Three projects in the solution:

- `dotnet-obs.AppHost` — Aspire orchestrator (entry point for local dev)
- `dotnet-obs.Server` — ASP.NET Core 10 backend (API + static file host)
- `frontend` — React 19 + TypeScript + Vite SPA

## Commands

### Start the observability stack first
```bash
cd observability
docker compose up -d
```
Grafana is at http://localhost:3000. Datasources (Loki, Tempo, Prometheus) are provisioned automatically.

### Run the full app stack
```bash
cd dotnet-obs
dotnet run --project dotnet-obs.AppHost
```
This starts Redis, the ASP.NET server, and the Vite dev server together via Aspire. The server sends OTLP telemetry to the OTel Collector on `localhost:4317`. Override the endpoint with `OTEL_EXPORTER_OTLP_ENDPOINT` in the environment or `appsettings.Development.json` of the AppHost.

### Build
```bash
cd dotnet-obs
dotnet build dotnet-obs.sln
cd frontend && npm run build
```

### Frontend dev (standalone)
```bash
cd dotnet-obs/frontend
npm install
npm run dev
```
Requires `SERVER_HTTPS` or `SERVER_HTTP` env var pointing to the running server (Aspire injects these automatically).

### Frontend lint
```bash
cd dotnet-obs/frontend
npm run lint
```

## Architecture

### Aspire orchestration (`AppHost.cs`)
The AppHost wires up the dependency graph:
1. **Redis** (`cache`) — started first
2. **Server** — waits for Redis, health-checked at `/health`, external HTTP endpoints exposed
3. **Vite frontend** — waits for Server, receives `SERVER_HTTPS`/`SERVER_HTTP` env vars via `WithReference(server)`

For production container builds, `PublishWithContainerFiles` bundles the frontend build output into the server's `wwwroot`.

### Server (`dotnet-obs.Server`)
- All API routes are under `/api` (mapped via `MapGroup`)
- `UseFileServer()` serves the frontend SPA from `wwwroot` in production
- Redis output caching is registered via `AddRedisClientBuilder("cache").WithOutputCache()`
- `Extensions.cs` (service defaults) wires OpenTelemetry for logs, metrics, and traces; also adds service discovery and standard HTTP resilience

### OpenTelemetry
Configured in `Extensions.cs`:
- **Metrics**: ASP.NET Core, HTTP client, and runtime instrumentation
- **Traces**: ASP.NET Core + HTTP client; health check paths (`/health`, `/alive`) are filtered out
- **Exporter**: OTLP if `OTEL_EXPORTER_OTLP_ENDPOINT` is set. `AppHost.cs` sets this to `http://localhost:4317` (the OTel Collector) by default, overridable via the `OTEL_EXPORTER_OTLP_ENDPOINT` configuration key in the AppHost environment.

### Local observability stack (`observability/`)
A standalone Docker Compose stack:

| Service | Role | Internal port |
|---------|------|---------------|
| **otelcol** (collector-contrib) | Receives OTLP, fans out to Loki/Tempo/Prometheus | 4317 gRPC, 4318 HTTP (host-exposed) |
| **Loki** | Log aggregation | 3100 |
| **Tempo** | Distributed tracing | 4317 (OTLP in), 3200 (query) |
| **Prometheus** | Metrics (scrapes otelcol :8889) | 9090 |
| **Grafana** | Visualization | 3000 (host-exposed) |

Data flow: Aspire server → OTel Collector → Loki (logs), Tempo (traces), Prometheus (metrics) → Grafana.

Grafana datasources are pre-provisioned in `grafana/provisioning/datasources/`. Cross-signal linking (trace → logs, trace → metrics) is configured via Tempo's `tracesToLogsV2` and `tracesToMetrics` Grafana datasource settings. Tempo's metrics generator produces service-graph and span-metric data visible in the Prometheus datasource.

### Frontend proxy
In dev mode, `vite.config.ts` proxies `/api/*` to the backend using `process.env.SERVER_HTTPS` or `SERVER_HTTP`. This is injected by Aspire at startup — no manual configuration needed when running via AppHost.

### Health checks
- `/health` — full readiness check (all registered checks)
- `/alive` — liveness check (only checks tagged `"live"`)
- Both are only mapped in the Development environment

# Vigil

> *Vigil (noun): a period of keeping awake to watch over something.*

Vigil watches investor-portal account journeys and flags account takeover and registration fraud **before** bank details change or shares are sold. Cheap rules filter everything, AI agents investigate the risky few, and a human decides. See [PROJECT.md](PROJECT.md) for the full design and stage plan.

## Status

**Stage 0 — Foundations.** Aspire runs Postgres, Neo4j and the API. Portal Events are ingested idempotently and the simulator replays two scripted scenarios. Detection starts in Stage 1.

**Stage 1 — Deterministic MVP (in progress).** Each ingest scores its Accounts against `config/rules.v1.json` and stores an Alert with Rule Hits and evidence EventIds: `GET /alerts?band=`, `GET /alerts/{id}`, `GET /rules`.

## Quick start

Prerequisites: .NET 10 SDK, Docker.

```bash
dotnet run --project src/Vigil.AppHost
```

1. Open the dashboard URL printed in the terminal. `api`, `postgres` and `neo4j` should turn healthy.
2. Click **Start** on the `simulator` resource. It posts Margaret's takeover and Mr Patel's house move.
3. Open the API's `/scalar` page, or:

```bash
curl http://localhost:5107/accounts/ACC-MARGARET/timeline
```

Running the simulator again is safe: events are idempotent on `EventId`, so the response reports them as already ingested.

Neo4j Browser: http://localhost:7474 (user `neo4j`, password in the AppHost's user-secrets: `dotnet user-secrets list --project src/Vigil.AppHost`).

## Tests

```bash
dotnet test
```

API tests start a throwaway Postgres container via Testcontainers.

## Layout

| Path | What |
|---|---|
| `src/Vigil.AppHost` | Aspire orchestration |
| `src/Vigil.ServiceDefaults` | OpenTelemetry, health checks, resilience |
| `src/Vigil.Domain` | Portal Event, rule set, Rule Hit and Alert records |
| `src/Vigil.Detection` | Rules engine: timeline + rule set → Alerts (pure) |
| `src/Vigil.Infrastructure` | EF Core event and alert stores, migrations |
| `src/Vigil.Api` | Ingestion, timeline, alert and rules endpoints |
| `src/Vigil.Simulator` | Scripted scenarios |
| `tests/Vigil.Detection.Tests` | Detection tests against the shipped rules file |
| `tests/Vigil.Api.Tests` | API tests against real Postgres |
| `config/rules.v1.json` | Versioned rules, windows, points and band thresholds |
| `docs/adr` | Architecture decisions |

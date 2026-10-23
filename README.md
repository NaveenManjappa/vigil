# Vigil

> *Vigil (noun): a period of keeping awake to watch over something.*

Vigil watches investor-portal account journeys and flags account takeover and registration fraud **before** bank details change or shares are sold. Cheap rules filter everything, AI agents investigate the risky few, and a human decides. See [PROJECT.md](PROJECT.md) for the full design and stage plan.

## Status

**Stage 0 — Foundations.** Aspire runs Postgres, Neo4j and the API. Portal Events are ingested idempotently and the simulator replays two scripted scenarios. Detection starts in Stage 1.

**Stage 1 — Deterministic MVP (in progress).** Each ingest scores its Accounts against `config/rules.v1.json` and stores an Alert with Rule Hits and evidence EventIds; an Alert reaching High opens a Case: `GET /alerts?band=`, `GET /alerts/{id}`, `GET /cases?status=`, `GET /rules`. A rules-only evaluation scores a seeded, labelled Dataset and writes a report to `eval-reports/`.

## Quick start

Prerequisites: .NET 10 SDK, Docker.

```bash
dotnet run --project src/Vigil.AppHost
```

1. Open the dashboard URL printed in the terminal. `api`, `postgres` and `neo4j` should turn healthy.
2. Click **Start** on the `simulator` resource. It posts every Scenario in `scenarios/`: Margaret's takeover and Mr Patel's house move.
3. Open the API's `/scalar` page, or:

```bash
curl http://localhost:5107/accounts/ACC-MARGARET/timeline
```

Running the simulator again is safe: events are idempotent on `EventId`, so the response reports them as already ingested.

To generate a labelled Dataset from the Section 10.1 Personas (seeded, so the same arguments always give the same file; it is written locally and never posted to the API):

```bash
dotnet run --project src/Vigil.Simulator -- generate --seed 42 --holders 1000
# Wrote datasets/seed-42-holders-1000.jsonl
```

To score that Dataset with the rules alone (in memory: no API, no database, no LLM, so it is free to run any time):

```bash
dotnet run --project src/Vigil.Evaluation
# Wrote eval-reports/<run ID>.md
```

It generates the Dataset first if the file is missing. Options: `--seed 42`, `--holders 1000`, `--rules config/rules.v1.json`, `--out eval-reports`. The report gives detection rate, false-positive rate, per-rule precision and early-detection rate, plus a per-Persona breakdown, and records the run ID, seed and Rules Version. See [docs/architecture.md](docs/architecture.md) for how the pieces fit together.

### Try a scenario

A Scenario is a JSON file, so a new journey needs no code. Copy `scenarios/margaret-ato.json` and edit the steps. Each step has an `after` offset from the start (`10m`, `4h`, `1d5h`), an event `type`, an optional actor `as` (the holder on the baseline device if left out) and optional `attributes`. Leave out `baseline` for a brand-new Account.

Dry-run it in memory to see the Score and Risk Band after each step:

```bash
dotnet run --project src/Vigil.Evaluation -- scenario scenarios/my-story.json
# | 3 | +10m | EmailChanged | fraudster | R2 +20: Password reset then contact change, 10 min apart; window 24h | 60 | High |
# First reached High at step 3, EmailChanged (+10m); peak Critical.
# Graph Rules G1, G2, G3 skipped: they need the graph, which a dry run does not have, so the live Score can be higher.
```

The dry run uses the API's rules, `config/rules.v2.json`, but has no graph, so it scores sequence and first-sighting Rules only. Margaret's dry run peaks at 90; posted to the API after the D-77 Scenarios, she also gets G2 and G3.

To try a rule change, edit a copy of the rules file and pass it with `--rules my-rules.json`. The scenario stays the same and only the verdict changes. Run the full evaluation with the same `--rules` to see what the change does to detection and false-positive rates.

Without `--scenario`, the simulator posts every shipped Scenario in order of its first event, so the earlier D-77 Accounts are in the graph before Margaret's takeover arrives. To post one to the running API, with the AppHost up, point the simulator at the API yourself, because outside Aspire it has no service discovery:

```bash
services__api__http__0=http://localhost:5107 dotnet run --project src/Vigil.Simulator -- --scenario scenarios/my-story.json
```

Files kept in `scenarios/` are copied beside the simulator at build time, so they are posted by the dashboard's Start button after the next AppHost restart.

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
| `src/Vigil.Api` | Ingestion, timeline, alert, case and rules endpoints |
| `src/Vigil.Simulator` | Posts Scenario files; the seeded Persona Dataset generator |
| `src/Vigil.Evaluation` | Rules-only evaluation console: Dataset in, markdown report out; `scenario` dry run |
| `tests/Vigil.Detection.Tests` | Detection tests against the shipped rules file |
| `tests/Vigil.Evaluation.Tests` | Metrics on a hand-labelled Dataset, the seed-42 known misses, the report |
| `tests/Vigil.Simulator.Tests` | Dataset generator tests (determinism, Persona mix, slow-burn spacing) and Scenario file loading |
| `tests/Vigil.Api.Tests` | API tests against real Postgres |
| `config/rules.v1.json` | Versioned rules, windows, points and band thresholds |
| `scenarios` | Scenario files (JSON), posted by the simulator and dry-run by evaluation |
| `eval-reports` | Generated evaluation reports |
| `docs/architecture.md` | Components, data flow, Margaret's case through Stage 1 |
| `docs/adr` | Architecture decisions |

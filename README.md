# Vigil

> *Vigil (noun): a period of keeping awake to watch over something.*

Vigil watches investor-portal accounts and flags account takeover **before** bank details change or shares are sold. Cheap rules look at every event, an AI agent investigates the risky few when you ask it to, and a human decides.

It is built to be followed: you send one event at a time from Scalar and see what each one changed.

## Quick start

You need the .NET 10 SDK and Docker running.

```bash
dotnet run --project src/Vigil.AppHost
```

1. Open the dashboard URL that the terminal prints. Wait until `api`, `postgres` and `neo4j` are running.
2. Select the `/scalar` link on the `api` row.
3. Follow the [walkthrough in the user guide](docs/user-guide.md#4-walk-through-margarets-takeover): twelve calls, from Margaret's first login to a closed Case.

It runs with a free Fake model until you configure Azure OpenAI ([user guide section 6](docs/user-guide.md#6-use-a-real-model-on-azure-openai)).

## Documentation

| Document | Read it to |
|---|---|
| [docs/user-guide.md](docs/user-guide.md) | Run Vigil, walk a fraud case through it, use a real model |
| [docs/architecture.md](docs/architecture.md) | See how the parts fit together, on one page |
| [docs/runbook.md](docs/runbook.md) | Reset data and fix common failures |
| [docs/handover.md](docs/handover.md) | Know the limits and how to read the core modules |
| [docs/industrialisation.md](docs/industrialisation.md) | See what to add back for real data and production |
| [PROJECT.md](PROJECT.md) | Read the problem, the design and the stage plan |
| [GLOSSARY.md](GLOSSARY.md) | Look up a domain term |
| [docs/adr/](docs/adr/) | Read why each decision was made |

## Tests

```bash
dotnet test
```

The API and graph tests start throwaway Postgres and Neo4j containers through Testcontainers, so Docker must be running. No test calls a real LLM.

## Layout

| Path | What |
|---|---|
| `src/Vigil.AppHost` | Aspire: starts Postgres, Neo4j and the API |
| `src/Vigil.ServiceDefaults` | OpenTelemetry, health checks, resilience |
| `src/Vigil.Domain` | Shared records: Portal Event, rule set, Rule Hit, Alert, Case status, Neighbourhood |
| `src/Vigil.Detection` | The Detector: which Rules did this event trigger? (pure) |
| `src/Vigil.Graph` | Neo4j: project each event, Graph Rules G1 and G2, who shares what |
| `src/Vigil.Agents` | The Investigator agent, its three tools, the Fake and Azure OpenAI chat clients |
| `src/Vigil.Api` | Endpoints, ingestion, the EF Core database and its migration |
| `tests/` | One test project per module; `Vigil.Api.Tests/WalkthroughTests` is the user guide's walkthrough |
| `config/rules.json` | The Rules, their points and Windows, and the band thresholds |
| `config/prompts/investigator.md` | The Investigator's instructions |
| `docs/` | User guide, architecture, runbook, handover, ADRs |

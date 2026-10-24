# Vigil

> *Vigil (noun): a period of keeping awake to watch over something.*

Vigil watches investor-portal account journeys and flags account takeover and registration fraud **before** bank details change or shares are sold. Cheap rules filter everything, AI agents investigate the risky few, and a human decides. See [PROJECT.md](PROJECT.md) for the full design and stage plan.

## Status

Stages 0–4 are built: rules-based detection, the knowledge graph, agentic investigation with a human decision, and evaluation with a feedback loop. Stage 5 (validation and handover) is in progress. [docs/handover.md](docs/handover.md) has the current status and the known limits.

## Quick start

You need the .NET 10 SDK and Docker running.

```bash
dotnet run --project src/Vigil.AppHost
```

1. Open the dashboard URL that the terminal prints. Wait until `api`, `postgres` and `neo4j` are healthy.
2. Select **Start** on the `simulator` resource. It sends Margaret's takeover and the other Scenarios.
3. Look at the result: `curl "http://localhost:5107/cases?status=AwaitingApproval"`.

Everything runs with the free Fake LLM until you configure Azure OpenAI. For the full walk-through, from Azure setup to a decided Case and a rule change, read the [user guide](docs/user-guide.md).

## Documentation

| Document | Read it to |
|---|---|
| [docs/user-guide.md](docs/user-guide.md) | Set up Azure OpenAI, configure every setting, and operate Vigil from the first event to a rule change |
| [docs/architecture.md](docs/architecture.md) | Understand how the parts work together |
| [docs/runbook.md](docs/runbook.md) | Reset data, recover, and fix common failures |
| [docs/handover.md](docs/handover.md) | Know the status, the limits, who owns what, and how to read the core modules |
| [docs/industrialisation.md](docs/industrialisation.md) | See the path from the MVP to production |
| [PROJECT.md](PROJECT.md) | Read the problem, the design and the stage plan |
| [GLOSSARY.md](GLOSSARY.md) | Look up a domain term |
| [docs/adr/](docs/adr/) | Read why each decision was made |

## Tests

```bash
dotnet test
```

API, graph and evaluation tests start throwaway Postgres and Neo4j containers through Testcontainers, so Docker must be running. No test calls a real LLM.

## Layout

| Path | What |
|---|---|
| `src/Vigil.AppHost` | Aspire orchestration |
| `src/Vigil.ServiceDefaults` | OpenTelemetry, health checks, resilience |
| `src/Vigil.Domain` | Shared records: Portal Event, rule set, Rule Hit, Alert, Case status, Confirmed Fraud, Dataset and Label |
| `src/Vigil.Detection` | Rules engine: timeline + rule set + stored Alerts + graph hits → Alerts (pure) |
| `src/Vigil.Graph` | Neo4j Projection, Graph Rules G1–G3 as Cypher queries, the Neighbourhood for the Investigator |
| `src/Vigil.Agents` | The Investigation: Investigator and tools, Reviewer, validation, masking, Evidence Refs, MAF workflow, shared LLM wiring |
| `src/Vigil.Infrastructure` | EF Core stores (events, Alerts, Cases, decisions, Interventions, Confirmed Frauds, Audit Entries, workflow checkpoints), migrations |
| `src/Vigil.Api` | Ingestion; timeline, alert, case, decision, audit and rules endpoints; Case queue and Investigation worker; `rebuild-graph` |
| `src/Vigil.Simulator` | Posts Scenario files; the seeded Persona Dataset generator; `post-dataset` and the Stand-in Analyst (`decide --by-label`) |
| `src/Vigil.Evaluation` | Evaluation console: Dataset in, graph off vs on, Agent Sample and Blind Spot markdown report out; `scenario` dry run; `generate-scenarios` (Scenario Generator); `tune` (Rule-Tuning Report) |
| `tests/Vigil.Detection.Tests` | Detection tests against the shipped rules file |
| `tests/Vigil.Graph.Tests` | Projection and Graph Rule queries against a real Neo4j |
| `tests/Vigil.Agents.Tests` | Masking, Evidence Refs, validation, the Investigation loop and workflow paths with a scripted LLM |
| `tests/Vigil.Evaluation.Tests` | Metrics on a hand-labelled Dataset, a Ring flagged only with the graph on, the seed-42 known misses, the Agent Sample, Evidence Grounding and Verdict Agreement (Fake LLM), the Judge's parsing and means, the report |
| `tests/Vigil.Simulator.Tests` | Dataset generator tests (determinism, Persona mix, slow-burn spacing) and Scenario file loading |
| `tests/Vigil.Api.Tests` | API tests against real Postgres and Neo4j: ingestion, Cases, the Investigation queue and budget, decisions and Act, graph rebuild, telemetry, the Stand-in Analyst |
| `config/rules.v1.json`, `rules.v2.json` | Versioned rules, windows, points and band thresholds; v2 adds the Graph Rules and is what the API runs |
| `config/prompts` | Versioned agent prompts: Investigator, Reviewer, Judge, Scenario Generator |
| `scenarios` | Scenario files (JSON), posted by the simulator and dry-run by evaluation |
| `datasets/generated` | Generated Scenarios, committed for review; Datasets themselves are generated locally and not committed |
| `eval-reports` | Generated evaluation and Rule-Tuning reports |
| `docs/architecture.md` | Components, data model, ingestion and detection, the graph, the Investigation, decisions, Margaret end to end, the evaluation and feedback loop |
| `docs/graph-primer.md` | The Cypher Vigil uses, on Vigil's own data |
| `docs/user-guide.md` | Set up (Azure included), configure and operate Vigil from the start to the end |
| `docs/runbook.md` | Reset, recovery and common failures |
| `docs/handover.md` | Status, known limits, ownership, and a reading guide to the core modules |
| `docs/industrialisation.md` | MVP to production target state |
| `docs/adr` | Architecture decisions |

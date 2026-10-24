# Vigil

> *Vigil (noun): a period of keeping awake to watch over something.*

Vigil watches investor-portal account journeys and flags account takeover and registration fraud **before** bank details change or shares are sold. Cheap rules filter everything, AI agents investigate the risky few, and a human decides. See [PROJECT.md](PROJECT.md) for the full design and stage plan.

## Status

**Stage 0 — Foundations.** Aspire runs Postgres, Neo4j and the API. Portal Events are ingested idempotently and the simulator replays two scripted scenarios. Detection starts in Stage 1.

**Stage 1 — Deterministic MVP (in progress).** Each ingest scores its Accounts against `config/rules.v1.json` and stores an Alert with Rule Hits and evidence EventIds; an Alert reaching High opens a Case: `GET /alerts?band=`, `GET /alerts/{id}`, `GET /cases?status=`, `GET /rules`. A rules-only evaluation scores a seeded, labelled Dataset and writes a report to `eval-reports/`.

**Stage 3 — Agentic investigation with MAF.** A Case opened by a High Alert is investigated in the background by a Microsoft Agent Framework workflow: an Investigator agent with three Account-bound tools, code validation of its Evidence Refs, and a Reviewer agent, all on masked Pseudonyms. The workflow pauses for an Analyst Decision, checkpointed in Postgres, and Act records the Intervention and Audit Entries. See [Margaret's Investigation](#margarets-investigation-stage-3-demo).

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

To score that Dataset with the graph off (v1) and on (v2) side by side (no API, no Postgres, no LLM, so it is free to run any time; Docker must be running):

```bash
dotnet run --project src/Vigil.Evaluation
# Wrote eval-reports/<run ID>.md
# v1: detection rate 17/30, false-positive rate 20/970, early-detection rate 9/9
# v2: detection rate 22/30, false-positive rate 20/970, early-detection rate 9/9
```

It generates the Dataset first if the file is missing. v1 is scored in memory. For v2 it starts a throwaway Neo4j container, so the demo graph is never touched, and replays the Dataset's events into it in OccurredAt order, so no Graph Rule sees a link that formed after the event being scored (about a minute for 1,000 holders). Options: `--seed 42`, `--holders 1000`, `--rules-off config/rules.v1.json`, `--rules-on config/rules.v2.json`, `--out eval-reports`. The report gives detection rate, false-positive rate, per-rule precision and early-detection rate, plus a per-Persona breakdown, for both Rules Versions, and records the run ID and seed. See [docs/architecture.md](docs/architecture.md) for how the pieces fit together.

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

To try a rule change, edit a copy of the rules file and pass it with `--rules my-rules.json`. The scenario stays the same and only the verdict changes. Run the full evaluation with it as `--rules-on my-rules.json` to see what the change does to detection and false-positive rates.

Without `--scenario`, the simulator posts every shipped Scenario in order of its first event, so the earlier D-77 Accounts are in the graph before Margaret's takeover arrives. To post one to the running API, with the AppHost up, point the simulator at the API yourself, because outside Aspire it has no service discovery:

```bash
services__api__http__0=http://localhost:5107 dotnet run --project src/Vigil.Simulator -- --scenario scenarios/my-story.json
```

Files kept in `scenarios/` are copied beside the simulator at build time, so they are posted by the dashboard's Start button after the next AppHost restart.

### Rebuild the graph

Neo4j holds a disposable Projection of Postgres. If Neo4j data is lost (for example, the `vigil-neo4j-data` volume is deleted) or the projection model changes, rebuild it: click **Start** on the `rebuild-graph` resource in the dashboard. It wipes Neo4j, recreates the constraints, re-projects every Portal Event and Confirmed Fraud from Postgres, then exits. Don't run the simulator during a rebuild: an event scored against a half-built graph misses its graph Rule Hits.

From the command line, it is the API binary run with `rebuild-graph`. Outside Aspire, give it the connection details. Copy them from the `api` resource's environment variables in the dashboard:

```bash
ConnectionStrings__vigil="Host=localhost;Port=<port>;Database=vigil;Username=postgres;Password=<password>" \
Neo4j__Uri=bolt://localhost:7687 Neo4j__Password=<password> \
dotnet run --project src/Vigil.Api -- rebuild-graph
```

Neo4j Browser: http://localhost:7474 (user `neo4j`, password in the AppHost's user-secrets: `dotnet user-secrets list --project src/Vigil.AppHost`).

## Margaret's Investigation (Stage 3 demo)

The free demo uses the `Fake` provider, the Development default: a scripted Investigator that makes one graph tool call and writes a ref-cited Finding, and a Reviewer that approves. No key and no spend.

1. Start everything: `dotnet run --project src/Vigil.AppHost`, and open the dashboard.
2. Click **Start** on `simulator`. Margaret's batch opens a Case, which the worker investigates in the background.
3. List the Cases waiting for a decision, worst first, and take Margaret's `id`:

   ```bash
   curl "http://localhost:5107/cases?status=AwaitingApproval"
   ```

4. Read the Case: the unmasked Finding (verdict, confidence, summary with her real AccountId, recommendation), each cited Evidence Ref resolved to EventIds, and the Review Flag and InvestigationFailed flags:

   ```bash
   curl http://localhost:5107/cases/<id>
   ```

5. Restart the API mid-approval: **Stop**, then **Start**, on the `api` resource in the dashboard. The Case is still AwaitingApproval; its workflow checkpoint is in Postgres.
6. Decide. The workflow resumes from the checkpoint without calling the model, and Act records the Analyst Decision and its Intervention and closes the Case:

   ```bash
   curl -X POST http://localhost:5107/cases/<id>/decision -H "Content-Type: application/json" \
     -d '{"outcome":"ConfirmedFraud","action":"BlockBankChanges","analystId":"analyst-7","notes":"Holder confirmed by phone it was not her."}'
   ```

7. Read the audit: InvestigationCompleted and ReviewCompleted (actor, model, Prompt Version, Rules Version, cited refs with EventIds, tokens), then DecisionRecorded and ActionTaken with the analyst:

   ```bash
   curl http://localhost:5107/cases/<id>/audit
   ```

In the dashboard's **Traces** view, Margaret's ingest request is one trace: `POST /events`, `graph.project`, `detect` and `graph.rules` per Account, then `investigate case` with the workflow's spans, `invoke_agent Investigator(vigil-investigator)` with its `chat` and `execute_tool QueryGraphNeighbourhood` spans, and `invoke_agent Reviewer(vigil-reviewer)`. The decision is its own trace, under `POST /cases/{id}/decision`, and its `decide case` span links back to `investigate case`. **Metrics** has `vigil.llm.tokens` (by `agent` and `direction`), `vigil.llm.cost` (£ per Investigation) and `vigil.investigation.duration`; with Fake, tokens and cost are 0.

### Using Azure OpenAI

1. Put the real key in the AppHost's user-secrets, replacing the generated placeholder:

   ```bash
   dotnet user-secrets set "Parameters:azure-openai-key" "<key>" --project src/Vigil.AppHost
   ```

2. Switch the provider and name the resource and deployment, in `src/Vigil.Api/appsettings.Development.json` or as environment variables on the `api` resource:

   ```json
   "Llm": {
     "Provider": "AzureOpenAI",
     "Endpoint": "https://<resource>.openai.azure.com/",
     "Deployment": "<deployment name>"
   }
   ```

   The API checks these at startup and names any that are missing. Set `Llm:InputPricePer1kTokens` and `Llm:OutputPricePer1kTokens` (in £) to your deployment's prices so `vigil.llm.cost` and the token log lines match the Azure cost view.

3. Restart the AppHost.

**Budget warning:** every new Case now costs real tokens. `Investigation:DailyLimit` (default 20 per UTC day) caps Investigations started; Cases over it stay Open, marked not investigated, and are not re-queued. Set `Investigation:Enabled` to `false` before replaying a large Dataset into the API. Each LLM call logs its tokens and estimated £, so spend can be reconciled call by call.

## Tests

```bash
dotnet test
```

API tests start throwaway Postgres and Neo4j containers via Testcontainers, so Docker must be running. No test calls a real LLM.

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
| `src/Vigil.Evaluation` | Evaluation console: Dataset in, graph off vs on markdown report out; `scenario` dry run |
| `tests/Vigil.Detection.Tests` | Detection tests against the shipped rules file |
| `tests/Vigil.Evaluation.Tests` | Metrics on a hand-labelled Dataset, a Ring flagged only with the graph on, the seed-42 known misses, the report |
| `tests/Vigil.Simulator.Tests` | Dataset generator tests (determinism, Persona mix, slow-burn spacing) and Scenario file loading |
| `tests/Vigil.Api.Tests` | API tests against real Postgres |
| `config/rules.v1.json` | Versioned rules, windows, points and band thresholds |
| `scenarios` | Scenario files (JSON), posted by the simulator and dry-run by evaluation |
| `eval-reports` | Generated evaluation reports |
| `docs/architecture.md` | Components, data flow, Margaret's case through Stage 1 |
| `docs/adr` | Architecture decisions |

# Vigil

> *Vigil (noun): a period of keeping awake to watch over something.*

Vigil watches investor-portal account journeys and flags account takeover and registration fraud **before** bank details change or shares are sold. Cheap rules filter everything, AI agents investigate the risky few, and a human decides. See [PROJECT.md](PROJECT.md) for the full design and stage plan.

## Status

Stages 0–4 are built. [docs/architecture.md](docs/architecture.md) explains how the parts fit together.

**Stage 0 — Foundations.** Aspire runs Postgres, Neo4j and the API. Portal Events are ingested idempotently into an append-only event store, and the simulator replays scripted Scenarios.

**Stage 1 — Deterministic MVP.** Each ingest scores its Accounts against versioned rules and stores an Alert per Risk Episode, with Rule Hits and evidence EventIds. An Alert reaching High opens a Case. Endpoints: `GET /alerts?band=`, `GET /alerts/{id}`, `GET /cases?status=`, `GET /rules`. A rules-only evaluation scores a seeded, labelled Dataset and writes a report to `eval-reports/`.

**Stage 2 — Knowledge graph.** Every Portal Event is projected into Neo4j inside the ingest request. The Graph Rules (G1 shared bank account, G2 known-fraud proximity, G3 ring indicator) feed the same Detector, and ingestion re-scores the Neighbours a new link touches. `rules.v2.json`, which the API runs, adds the Graph Rules to v1. The evaluation reports graph off against graph on.

**Stage 3 — Agentic investigation with MAF.** A Case opened by a High Alert is investigated in the background by a Microsoft Agent Framework workflow: an Investigator agent with three Account-bound tools, code validation of its Evidence Refs, and a Reviewer agent, all on masked Pseudonyms. The workflow pauses for an Analyst Decision, checkpointed in Postgres, and Act records the Intervention and Audit Entries. See [Margaret's Investigation](#margarets-investigation-stage-3-demo).

**Stage 4 — Training agents, evaluation, feedback.** One evaluation command scores the rules with the graph off and on, runs the agents on a seeded Agent Sample with a Judge, and lists the Blind Spots a red-team Scenario Generator found. A Stand-in Analyst decides Cases through the API, and a Rule-Tuning Report suggests weight changes from those decisions for a human to approve. See the [Stage 4 demo](#evaluation-and-feedback-stage-4-demo) and the [runbook](docs/runbook.md).

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

To score it with the rules and run the agents on a sample, see the [Stage 4 demo](#evaluation-and-feedback-stage-4-demo).

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

## Evaluation and feedback (Stage 4 demo)

Everything here runs with the free Fake LLM by default. The [runbook](docs/runbook.md) has every option, the Azure runs and the common failures; [docs/architecture.md](docs/architecture.md#evaluation-and-feedback-loop) explains how the loop fits together.

**1. Evaluate the rules and the agents.** With Docker running, and no AppHost needed:

```bash
dotnet run --project src/Vigil.Evaluation
# Wrote eval-reports/<run ID>.md
# v1: detection rate 17/30, false-positive rate 20/970, early-detection rate 9/9
# v2: detection rate 22/30, false-positive rate 20/970, early-detection rate 9/9
# agents (Fake, not meaningful): Evidence Grounding 25/25, Verdict Agreement 10/25, 0 tokens, £0.0000; Judge 25/25 judged, 0 tokens
# Blind Spots: 1 of 1 fraud Generated Scenarios never reached High on v2
```

Open the report in `eval-reports/`. Things to point at:

- **Graph off vs on.** v2's Graph Rules lift the five Fraud ring Accounts from Medium to Critical, so detection rises from 17 to 22 of 30. The slow-burn fraudster stays a known miss either way.
- **False positives.** The 20 Travelling holders reach High under both versions: the false positives the feedback loop learns from.
- **Agents.** 25 Investigations (20 Flagged Accounts plus 5 Near-misses) with Evidence Grounding, Verdict Agreement, Reviewer rejection and Review Flag rates, cost per Investigation, and the Judge's four scores. With Fake they only show the harness runs; `--llm AzureOpenAI` gives real ones for the cost of the Agent Sample alone.
- **Blind Spots.** Generated Scenarios of fraud that never reaches High.

**2. Find Blind Spots.** The Scenario Generator agent reads the rules and writes Generated Scenarios of fraud they would miss to `datasets/generated/`. It needs Azure for real output; the committed one is a Fake-written placeholder that the first Azure run replaces ([runbook](docs/runbook.md#generate-scenarios-find-blind-spots)):

```bash
dotnet run --project src/Vigil.Evaluation -- generate-scenarios --count 5 --llm AzureOpenAI
```

**3. Learn from decisions.** With the AppHost up, `Investigation:DailyLimit` raised and `Llm:Provider` Fake, post the Dataset to the API, let the Stand-in Analyst decide every Case from its Label, then build the Rule-Tuning Report from Postgres ([runbook](docs/runbook.md#the-stand-in-analyst-and-the-rule-tuning-report) for the setup):

```bash
services__api__http__0=http://localhost:5107 dotnet run --project src/Vigil.Simulator -- post-dataset
services__api__http__0=http://localhost:5107 dotnet run --project src/Vigil.Simulator -- decide --by-label
dotnet run --project src/Vigil.Evaluation -- tune --connection "<the api's ConnectionStrings__vigil>"
# Wrote eval-reports/rule-tuning-<run ID>.md
```

The report gives each Rule's precision from the decisions, suggests halving the points of a Rule that is wrong more often than not, and shows each suggestion's What-if over the Dataset. It never edits a rules file: a person copies the rules to a new version, re-runs step 1 with `--rules-off config/rules.v2.json --rules-on config/rules.v3.json`, and decides (ADR-0012).

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
| `docs/runbook.md` | Run, reset data, evaluations, the Stand-in and tune flow, common failures |
| `docs/industrialisation.md` | MVP to production target state |
| `docs/adr` | Architecture decisions |

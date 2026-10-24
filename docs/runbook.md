# Runbook

How to run Vigil, reset its data, run evaluations, and work through the Stand-in Analyst and Rule-Tuning flow. It is written for a developer new to the repo. Run every command from the repo root: the evaluation and simulator commands read `config/`, `scenarios/` and `datasets/` relative to the current directory.

Terms in capitals (Case, Dataset, Agent Sample, Blind Spot, and so on) are defined in [GLOSSARY.md](../GLOSSARY.md). The design behind each step is in [architecture.md](architecture.md) and the ADRs in [adr/](adr/).

## Prerequisites

- .NET 10 SDK. `global.json` opts `dotnet test` into Microsoft.Testing.Platform, which xunit.v3 needs.
- Docker, running. The AppHost runs Postgres and Neo4j in containers. The evaluation, `tune` and the API tests each start throwaway containers through Testcontainers.
- For an Azure run only: an Azure OpenAI resource with a chat model deployment and its key. Nothing else in this runbook needs a key or costs money.

## Run the stack

```bash
dotnet run --project src/Vigil.AppHost
```

Open the dashboard URL printed in the terminal. `postgres`, `neo4j` and `api` should turn healthy; the API migrates the database on startup. `simulator` and `rebuild-graph` wait for an explicit **Start**.

| What | Where |
|---|---|
| API | http://localhost:5107 (`/scalar` for the API reference) |
| Neo4j Browser | http://localhost:7474, user `neo4j`, password from `dotnet user-secrets list --project src/Vigil.AppHost` (`Parameters:neo4j-password`) |
| Postgres connection string | dashboard, `api` resource, environment variable `ConnectionStrings__vigil`. The host port changes on every AppHost start, so copy it again after a restart. |

The LLM provider is `Fake` in Development (`src/Vigil.Api/appsettings.Development.json`), so Investigations are scripted and free. To use Azure OpenAI in the API, follow "Using Azure OpenAI" in the [README](../README.md#using-azure-openai).

### Post Scenarios or a Dataset

Click **Start** on `simulator` to post every Scenario in `scenarios/`. From a terminal, the simulator has no Aspire service discovery, so give it the API's address:

```bash
services__api__http__0=http://localhost:5107 dotnet run --project src/Vigil.Simulator -- --scenario scenarios/margaret-ato.json
```

Posting is idempotent on `EventId`, so posting the same thing twice is safe.

## Reset data

Postgres and Neo4j keep their data in the Docker volumes `vigil-postgres-data` and `vigil-neo4j-data`. To start from empty:

1. Stop the AppHost (Ctrl+C). Aspire removes its containers when it stops.
2. Delete both volumes:

   ```bash
   docker volume rm vigil-postgres-data vigil-neo4j-data
   ```

3. Start the AppHost again. The API recreates the schema; the Projection starts empty.

**The password gotcha.** The Postgres and Neo4j passwords are generated once into the AppHost's user-secrets and written into each volume on its first start. If you reset the user-secrets (or clone the repo on a new machine but keep old volumes), the stored passwords no longer match and the database refuses the connection. Delete the volumes too, then start again.

**Neo4j only.** If only the graph is lost or its model changed, you don't need to reset Postgres. Postgres is the source of truth and the graph is a disposable Projection (ADR-0015): click **Start** on `rebuild-graph`. Don't post events while it runs.

**Stale Datasets.** Datasets are not committed. Each command that needs `datasets/seed-<seed>-holders-<holders>.jsonl` generates it if the file is missing, and uses it as is if it exists. After a change to the Personas (in `src/Vigil.Simulator/Personas/`), delete the old file so it is regenerated:

```bash
rm datasets/seed-42-holders-1000.jsonl
```

## Run the evaluation

### With the Fake LLM (free)

```bash
dotnet run --project src/Vigil.Evaluation
```

It needs Docker but no AppHost, API or Postgres. It takes about a minute and a half:

1. It generates the seed-42, 1,000-holder Dataset if it is missing.
2. It scores the Dataset in memory with `config/rules.v1.json` (graph off).
3. It replays all of the Dataset's events into a throwaway Neo4j container (about 20,000 events, the slowest step) and scores it with `config/rules.v2.json` (graph on).
4. It investigates an Agent Sample of 25 Accounts (20 Flagged plus 5 Near-misses) with the Investigator, Reviewer and Judge, in process.
5. It dry-runs every Generated Scenario in `datasets/generated/` for Blind Spots.

It writes `eval-reports/<run ID>.md` and prints a summary:

```text
Wrote eval-reports/20261024T155914Z-v1-v2-seed42.md
v1: detection rate 17/30, false-positive rate 20/970, early-detection rate 9/9
v2: detection rate 22/30, false-positive rate 20/970, early-detection rate 9/9
agents (Fake, not meaningful): Evidence Grounding 25/25, Verdict Agreement 10/25, 0 tokens, £0.0000; Judge 25/25 judged, 0 tokens
Blind Spots: 1 of 1 fraud Generated Scenarios never reached High on v2
```

With Fake every Finding is the same scripted one, so the agent numbers only show that the harness runs; the report says so. The rules metrics are real.

Options:

| Option | Default | |
|---|---|---|
| `--seed` | `42` | Dataset seed, also used to draw the Agent Sample |
| `--holders` | `1000` | Dataset size |
| `--rules-off` | `config/rules.v1.json` | Rules Version scored with the graph off |
| `--rules-on` | `config/rules.v2.json` | Rules Version scored with the graph on; the Agent Sample and Blind Spots use it |
| `--out` | `eval-reports` | Report folder |
| `--llm` | `Fake` | `Fake` or `AzureOpenAI` |
| `--agent-sample` | `20` | Flagged Accounts in the Agent Sample; Near-misses add up to a quarter more |

Commit a report you want to keep. Each run writes a new file named by its run ID, so delete the older one if the new one replaces it.

### With Azure OpenAI (costs money)

The evaluation reads its LLM settings from environment variables, not from the AppHost's user-secrets:

```bash
export Llm__Endpoint=https://<resource>.openai.azure.com/
export Llm__Deployment=<deployment name>
export Llm__ApiKey=<key>
# Optional: your deployment's prices in £ per 1,000 tokens, so the report's £ figures match the Azure cost view.
export Llm__InputPricePer1kTokens=0.0003
export Llm__OutputPricePer1kTokens=0.0012

dotnet run --project src/Vigil.Evaluation -- --llm AzureOpenAI
```

A missing setting fails in the first second, before the slow graph replay, and names the setting. The spend is bounded by the Agent Sample, never the whole Dataset (ADR-0016): 25 Investigations (each up to two Investigator attempts and two Reviewer calls) and 25 Judge calls. Each call logs its tokens and estimated £, and the report totals them. To make a smaller first run, pass `--agent-sample 4`.

Check the Azure cost view afterwards; the logged £ is an estimate from the prices above.

### Dry-run one Scenario

```bash
dotnet run --project src/Vigil.Evaluation -- scenario scenarios/margaret-ato.json
```

It prints the Score and Risk Band after each step, scored with `config/rules.v2.json` (or `--rules <file>`). It has no graph, so Graph Rules are skipped. Nothing is written.

## Generate Scenarios (find Blind Spots)

The Scenario Generator agent writes labelled Generated Scenarios of fraud the current rules would miss. It makes one LLM call:

```bash
dotnet run --project src/Vigil.Evaluation -- generate-scenarios --count 5 --llm AzureOpenAI
```

Use the same `Llm__*` environment variables as the Azure evaluation. Options: `--count` (default 5, at most 10), `--rules` (default `config/rules.v2.json`, the rules it tries to evade), `--llm` (default `Fake`), `--out` (default `datasets/generated`).

Each Scenario it returns is parsed strictly, must carry a `label`, and is kept only if its name and shape (event types in order, gaps rounded to the hour) are new. Kept ones are written to `datasets/generated/<name>.json`; rejected ones are printed with the reason. The next evaluation run lists every fraud Generated Scenario that never reaches High as a Blind Spot.

### Replace the Fake placeholder

`datasets/generated/fake-late-mfa-slow-cash-out.json` was written by the Fake provider so the Blind Spot section had something to show before an Azure run. Its description and intent say so. After your first Azure run:

1. Read each new file in `datasets/generated/`. Dry-run any you doubt with `scenario datasets/generated/<name>.json`.
2. Delete the placeholder: `git rm datasets/generated/fake-late-mfa-slow-cash-out.json`.
3. Re-run the evaluation so the report's Blind Spots come from the real Scenarios, and commit the Generated Scenarios with that report.

`generate-scenarios --llm Fake` writes the placeholder again, so don't run it after deleting the placeholder. While the placeholder is there, a Fake run keeps nothing: it prints `Rejected` because that name and shape already exist.

## The Stand-in Analyst and the Rule-Tuning Report

The Rule-Tuning Report learns from Analyst Decisions on real Cases. To get enough of them, post the Dataset to the API, let the Fake agents investigate every Case, have the Stand-in Analyst decide each one from its Label, then build the report from Postgres. The whole flow uses the Fake LLM and costs nothing.

### 1. Prepare the API

The flow needs two API settings, and both must be in place before the Dataset is posted:

- `Llm:Provider` must be `Fake`. It is the Development default. Check that you haven't switched it to `AzureOpenAI` for the README's Azure demo: the post opens about 40 Cases, and each would be a real Investigation.
- `Investigation:DailyLimit` must be above the number of Cases the post opens. The default of 20 per UTC day leaves the rest Open, marked not investigated, and the budget guard never re-queues those (ADR-0004), so they can never be decided. Raise it to 100.

Set both in `src/Vigil.Api/appsettings.Development.json` (don't commit the change):

```json
"Llm": { "Provider": "Fake" },
"Investigation": { "DailyLimit": 100 }
```

If you run the API outside Aspire, environment variables do the same: `Llm__Provider=Fake Investigation__DailyLimit=100`.

Reset the data first (see above) so the report counts only this run's decisions, then start the AppHost.

### 2. Post the Dataset

```bash
dotnet run --project src/Vigil.Simulator -- generate --seed 42 --holders 1000
services__api__http__0=http://localhost:5107 dotnet run --project src/Vigil.Simulator -- post-dataset
```

`post-dataset` posts every event of `datasets/seed-42-holders-1000.jsonl` (`--dataset <file>` for another) without its Labels, in OccurredAt order across all Accounts, in batches of 200. Each Account that reaches High opens a Case, and the worker investigates it in the background, one Case at a time. Re-running it after a failure is safe.

### 3. Decide every Case

```bash
services__api__http__0=http://localhost:5107 dotnet run --project src/Vigil.Simulator -- decide --by-label
```

For each Case in AwaitingApproval whose Account is in the Dataset, the Stand-in Analyst posts a decision to `POST /cases/{id}/decision`, recorded under the analyst `stand-in`: ConfirmedFraud with BlockBankChanges for a fraud Label, FalsePositive with NoAction for a genuine one. Cases on Accounts outside the Dataset, such as the Scenarios', are listed and left for a human.

If it warns that Cases are still Open or Investigating, the worker hasn't finished. Wait a few seconds and run `decide --by-label` again, until no Case is left. Watch the worker's progress in the dashboard's `api` logs, or with `curl "http://localhost:5107/cases?status=Investigating"`.

### 4. Build the Rule-Tuning Report

```bash
dotnet run --project src/Vigil.Evaluation -- tune --connection "Host=localhost;Port=<port>;Database=vigil;Username=postgres;Password=<password>"
```

Copy the connection string from the `api` resource's `ConnectionStrings__vigil` in the dashboard. Options: `--rules` (default `config/rules.v2.json`, the version the API scored with), `--seed` and `--holders` (default 42 and 1000; name the Dataset you posted), `--out` (default `eval-reports`).

It reads Analyst Decision → Case → Alert → Rule Hits, gives each Rule's precision from the decisions, and suggests halving the points (to the nearest 5) of any Rule below 50% precision with at least 5 decisions. For each suggestion it re-runs detection over the Dataset, with a throwaway Neo4j when the rules have Graph Rules, and shows the What-if: detection, false-positive and early-detection rates before and after. It writes `eval-reports/rule-tuning-<run ID>.md` and prints one line per suggestion.

### 5. Apply a suggestion (a human decision)

`tune` never edits a rules file (ADR-0012). If you accept a suggestion, after weighing its What-if:

1. Copy `config/rules.v2.json` to a new version, such as `config/rules.v3.json`, set its `version`, and change the points.
2. Compare it with the current version: `dotnet run --project src/Vigil.Evaluation -- --rules-off config/rules.v2.json --rules-on config/rules.v3.json`.
3. If it is better, point the API at it with `Rules:Path` in `src/Vigil.Api/appsettings.json`. Every Alert scored from then on records the new Rules Version.

## Tests

```bash
dotnet test
```

Docker must be running: the API tests use Testcontainers for Postgres and Neo4j. No test calls a real LLM.

## Common failures

| Symptom | Cause | Fix |
|---|---|---|
| The evaluation, `tune` or `dotnet test` fails with a Docker or Testcontainers connection error | Docker isn't running | Start Docker Desktop and run again |
| `postgres` or `neo4j` never turns healthy, or the API logs an authentication failure | The volume holds an old password: user-secrets were reset after the volume was created | Stop the AppHost, `docker volume rm vigil-postgres-data vigil-neo4j-data`, start again |
| `neo4j` fails to start: port 7474 or 7687 in use | Another Neo4j (a local install, or a container from another checkout) holds the AppHost's fixed ports | `docker ps` or `lsof -i :7687`, and stop it |
| Report numbers or Persona counts don't match the code after a Persona change | A stale Dataset file was reused | `rm datasets/seed-42-holders-1000.jsonl` and run again |
| `Llm:Endpoint is required when Llm:Provider is AzureOpenAI` | An `Llm__*` environment variable isn't set in this shell | Export `Llm__Endpoint`, `Llm__Deployment` and `Llm__ApiKey` |
| `Llm:Provider '...' is not supported` | A typo in `--llm` or `Llm:Provider` | Use `Fake` or `AzureOpenAI` |
| Azure calls fail with 401 or 404 | Wrong key, or `Llm__Deployment` is the model name instead of the deployment name | Check both in the Azure portal; the endpoint is the resource root, without `/openai/v1/` |
| The simulator fails to resolve `https+http://api` | It was run from a terminal, outside Aspire | Prefix it with `services__api__http__0=http://localhost:5107` |
| `post-dataset` fails with `Could not find file ... seed-42-holders-1000.jsonl` | The Dataset hasn't been generated, or the command wasn't run from the repo root | Run `generate` from the repo root first |
| `decide --by-label` keeps reporting Cases still Open | The budget guard skipped them: `Investigation:DailyLimit` was below the number of Cases | They are never re-queued. Raise the limit, reset the data and post again |
| `decide --by-label` decides 0 Cases and leaves none for later | Nothing reached AwaitingApproval: the Dataset wasn't posted, or `Investigation:Enabled` is `false` | Check `curl http://localhost:5107/cases` |
| `tune` reports no suggestions | Too few decisions: no Rule has 5 decided Cases | Check the Stand-in decided every Case, then run `tune` again |
| `tune` fails to connect | The Postgres port changed when the AppHost restarted | Copy `ConnectionStrings__vigil` from the dashboard again |
| `generate-scenarios` prints only `Rejected` lines | Every returned Scenario was invalid, had no label, or repeated an existing shape | Read the reasons; run again or with a higher `--count` |
| The evaluation fails with `--rules is for the scenario dry run` | The full evaluation takes `--rules-off` and `--rules-on` | Use those, or run `scenario` |

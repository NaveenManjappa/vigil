# Runbook

How to reset Vigil's data, recover from a lost graph, and fix common failures. For how to set up, configure and run each part, read the [user guide](user-guide.md): it is the one place for procedures, and this runbook does not repeat them.

Run every command from the repository root.

## Reset data

Postgres and Neo4j keep their data in the Docker volumes `vigil-postgres-data` and `vigil-neo4j-data`. To start from empty:

1. Stop the AppHost (Ctrl+C). Aspire removes its containers when it stops.
2. Delete both volumes:

   ```bash
   docker volume rm vigil-postgres-data vigil-neo4j-data
   ```

3. Start the AppHost again. The API recreates the schema; the Projection starts empty.

**Passwords live in the volumes.** The Postgres and Neo4j passwords are generated once into the AppHost's user-secrets and written into each volume on its first start. If you reset the user-secrets (or clone the repo on a new machine but keep old volumes), the stored passwords no longer match and the database refuses the connection. Delete the volumes too, then start again.

**Neo4j only.** If only the graph is lost or its model changed, you don't need to reset Postgres. Postgres is the source of truth and the graph is a disposable Projection (ADR-0015): click **Start** on `rebuild-graph`. Don't post events while it runs. From a terminal, run the API binary with `rebuild-graph`, giving it the connection details from the `api` resource's environment in the dashboard:

```bash
ConnectionStrings__vigil="Host=localhost;Port=<port>;Database=vigil;Username=postgres;Password=<password>" \
Neo4j__Uri=bolt://localhost:7687 Neo4j__Password=<password> \
dotnet run --project src/Vigil.Api -- rebuild-graph
```


**Stale Datasets.** Datasets are not committed. Each command that needs `datasets/seed-<seed>-holders-<holders>.jsonl` generates it if the file is missing, and uses it as is if it exists. After a change to the Personas (in `src/Vigil.Simulator/Personas/`), delete the old file so it is regenerated:

```bash
rm datasets/seed-42-holders-1000.jsonl
```

## Find the Postgres connection string

`tune` and the command-line `rebuild-graph` need the connection string of the AppHost's Postgres. Either copy `ConnectionStrings__vigil` from the `api` resource in the dashboard, or build it:

- **Port:** `docker ps --format '{{.Names}} {{.Ports}}' | grep postgres`. The port is the number before `->5432`, and it changes on every AppHost start.
- **Password:** the `Parameters:postgres-password` secret, from `dotnet user-secrets list --project src/Vigil.AppHost`.

```text
Host=localhost;Port=<port>;Database=vigil;Username=postgres;Password=<password>
```

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
| `decide --by-label` keeps reporting Cases still Open | The budget guard skipped them: `Investigation:DailyLimit` was below the number of Cases | They are never re-queued. Raise the limit, reset the data and post again ([user guide 14.1](user-guide.md#141-prepare-the-api)) |
| `decide --by-label` leaves some Cases for a human | Their Accounts are not in the Dataset, for example the Scenario Accounts | Expected. Decide them with `POST /cases/{id}/decision`, or leave them |
| The Rule-Tuning Report counts more decisions than the Stand-in made | Decisions from earlier runs, or a Scenario decided by hand, are still in Postgres | Reset the data before the flow if the report should count one run only |
| `decide --by-label` decides 0 Cases and leaves none for later | Nothing reached AwaitingApproval: the Dataset wasn't posted, or `Investigation:Enabled` is `false` | Check `curl http://localhost:5107/cases` |
| `tune` reports no suggestions | Too few decisions: no Rule has 5 decided Cases | Check the Stand-in decided every Case, then run `tune` again |
| `tune` fails to connect | The Postgres port changed when the AppHost restarted | Copy `ConnectionStrings__vigil` from the dashboard again |
| `generate-scenarios` prints only `Rejected` lines | Every returned Scenario was invalid, had no label, or repeated an existing shape | Read the reasons; run again or with a higher `--count` |
| The evaluation fails with `--rules is for the scenario dry run` | The full evaluation takes `--rules-off` and `--rules-on` | Use those, or run `scenario` |

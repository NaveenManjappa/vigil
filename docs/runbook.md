# Runbook

How to reset Vigil's data and fix common failures. To set up, configure and run Vigil, read the [user guide](user-guide.md); this runbook does not repeat it.

Run every command from the repository root.

## Reset data

**To start the walkthrough again**, send `POST /reset` in Scalar. It deletes every event, Alert and Case from Postgres and every node from Neo4j. It exists in Development only.

**To delete the databases themselves**, for example after an upgrade:

1. Stop the AppHost (Ctrl+C). Aspire removes its containers when it stops.
2. Delete both volumes:

   ```bash
   docker volume rm vigil-postgres-data vigil-neo4j-data
   ```

3. Start the AppHost again. The API creates the schema; the graph starts empty.

**Upgrading from `v1-full`.** The simplification (ADR-0017) replaced every migration with one new `InitialCreate`. A Postgres volume from the earlier version still has the old tables and migration history, so the API fails at startup. Delete the volumes as above, once.

**Passwords live in the volumes.** The Postgres and Neo4j passwords are generated once into the AppHost's user-secrets and written into each volume on its first start. If you reset the user-secrets, or clone the repo on a new machine but keep old volumes, the stored passwords no longer match and the database refuses the connection. Delete the volumes too, then start again.

## Common failures

| Symptom | Cause | Fix |
|---|---|---|
| `dotnet test` fails with a Docker or Testcontainers connection error | Docker isn't running | Start Docker Desktop and run again |
| `postgres` or `neo4j` never turns healthy, or the API logs an authentication failure | The volume holds an old password | Stop the AppHost, delete both volumes (above), start again |
| The API fails at startup with `relation "..." already exists` | A Postgres volume from before ADR-0017 | Delete both volumes (above), once |
| `neo4j` fails to start: port 7474 or 7687 in use | Another Neo4j holds the AppHost's fixed ports | `docker ps` or `lsof -i :7687`, and stop it |
| `POST /cases/{id}/investigate` returns 409 | The Case is not Open: it was already investigated or decided | Look at it with `GET /cases/{id}`; a Case is investigated once |
| `POST /cases/{id}/decision` returns 409 | The Case is not AwaitingDecision | Investigate it first, or it is already Closed |
| `POST /cases/{id}/investigate` returns 502 | The model call failed or its reply was not a Finding; the Case stays Open | Read the API log in the dashboard, fix the cause, send the request again |
| `Llm:Endpoint is required when Llm:Provider is AzureOpenAI` | A setting is missing | Set `Llm:Endpoint` and `Llm:Deployment` in `appsettings.Development.json` ([user guide 6.4](user-guide.md#64-configure-vigil)) |
| Azure calls fail with 401 or 404 | Wrong key, or the model name used instead of the deployment name | Check both in the Azure portal; the endpoint is the resource root, without `/openai/v1/` |
| A step in the walkthrough gives a different event number or Score | Data from an earlier run is still there | Send `POST /reset` and start the walkthrough from step 0 |

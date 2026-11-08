# Industrialisation

Vigil is now a walkable demo (ADR-0017): one user, one event at a time, synthetic data, an agent you call by hand. This page says what to add back to run it on real data, in the order it matters, and then what changes in production. The full earlier version, with most of the first list built, is tagged `v1-full`; look there before writing any of it again.

The principles stay the same at every step: rules first and AI second (ADR-0001), versioned rules (ADR-0002), a human deciding every action and every rule change (ADR-0012).

## Add back before real data

| # | What | Why it matters | Where it was in `v1-full` |
|---|---|---|---|
| 1 | **PII masking** before any LLM call | Real identifiers must never reach a model. Mask per Investigation, store the map on the Case, unmask only for the analyst | ADR-0008, `Vigil.Agents/Masking` |
| 2 | **Audit Entries** | A regulator asks who or what acted, on which evidence, with which model, prompt and rules version | `audit_entries`, `AuditStore` |
| 3 | **Locking and one transaction per ingest** | Two concurrent events for one Account can both open an Alert or a Case | ADR-0015, advisory locks in `Ingestion` |
| 4 | **Re-scoring Neighbours** | Today G1 and G2 only look at the new event, so an Account linked *later* to fraud is not re-scored | ADR-0015, `GraphRules.NeighboursOfAsync` |
| 5 | **Automatic investigation with a budget** | An analyst should find a Finding waiting, within a daily LLM budget | ADR-0004, `InvestigationWorker` |
| 6 | **Durable pause for the decision** | An approval can wait days and must survive a restart | ADR-0005, MAF workflow with Postgres checkpoints |
| 7 | **Checks on the Finding** | Reject a Finding that cites events it was never shown or is too long, before an analyst reads it; optionally a Reviewer agent | ADR-0006, `FindingValidation`, `Reviewer` |
| 8 | **Evaluation** | Measure detection rate, false positives and agent quality before changing a rule or a prompt | ADR-0016, `Vigil.Evaluation` |
| 9 | **Late events** | Today an event that arrives late is scored against what happened before it, but never completes a pattern after the fact | ADR-0013, Risk Episodes |

## Production target

| Area | Demo today | Target state |
|---|---|---|
| Ingestion | One event per call from Scalar | Event Hubs fed by change data capture from the portal's Source Systems; ingestion is already idempotent on `EventId` |
| Investigation | `POST /cases/{id}/investigate` by hand | Service Bus queue and worker instances, with a shared budget limiter |
| Event store | PostgreSQL in Docker (ADR-0010) | Azure Database for PostgreSQL |
| Graph | Neo4j 5 Community in Docker (ADR-0003) | Neo4j Aura; later RDF/OWL in Stardog for reasoning over source systems |
| LLM | Azure OpenAI by key, or Fake (ADR-0009) | Azure OpenAI by managed identity, private endpoint |
| Observability | OpenTelemetry to the Aspire dashboard (ADR-0011) | Application Insights with Azure Monitor alerts on ingest latency, Investigation failures and LLM cost |
| Secrets and identity | User-secrets; no analyst login | Key Vault; Entra ID with analyst and supervisor roles |
| Hosting | Aspire on a laptop | Azure Container Apps, deployed with Bicep or `azd` |
| Analyst UI | Scalar | A Case queue with a graph view, on the same endpoints |
| Detection | Weighted rules | Rules plus a per-Holder anomaly score, later supervised models trained on Analyst Decisions |
| Rule changes | A human edits `config/rules.json` | Change request with model-risk sign-off, four-eyes approval and a shadow run of the new version |

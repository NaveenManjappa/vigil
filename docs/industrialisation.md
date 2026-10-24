# Industrialisation

Vigil is an MVP: one machine, one API instance, simulated sources, a £5–10 LLM budget. This page describes what changes to run it for real and what stays. It follows PROJECT.md Section 17 and the "At industrialisation" sections of the ADRs.

The design was chosen so most of these are swaps behind a seam that already exists, not rewrites. The parts that make Vigil defensible in a regulated firm stay the same: rules first and AI second (ADR-0001), versioned rules (ADR-0002), PII masked before any LLM call (ADR-0008), a human approving every action and every rule change (ADR-0005, ADR-0012), and an Audit Entry for every step.

## At a glance

| Area | MVP today | Target state | Seam that makes it a swap |
|---|---|---|---|
| Ingestion | Simulator posts batches to `POST /events` | Event Hubs fed by change data capture (CDC) from the portal's Source Systems | The canonical Portal Event: only the adapter in front of it changes |
| Case queue | In-process `Channel<T>`, single API instance (ADR-0004) | Azure Service Bus, several worker instances | `ICaseQueue` |
| Event store | PostgreSQL in Docker (ADR-0010) | Azure Database for PostgreSQL or Azure SQL | EF Core provider |
| Graph | Neo4j 5 Community in Docker (ADR-0003) | Neo4j Aura; later RDF/OWL in Stardog for reasoning and virtual graphs over source systems | The graph is a disposable Projection of Postgres (ADR-0015), so moving it is a rebuild |
| LLM | Azure OpenAI by key, or Fake (ADR-0009) | Azure OpenAI (or Foundry) by managed identity, private endpoint | `IChatClient` and `Llm:Provider` |
| Observability | OpenTelemetry to the Aspire dashboard (ADR-0011) | The same telemetry to Application Insights, with Azure Monitor alerts | OpenTelemetry exporter configuration |
| Secrets and identity | User-secrets; no analyst login | Key Vault; Entra ID with analyst and supervisor roles | ASP.NET Core configuration and authentication |
| Hosting | Aspire on a laptop | Azure Container Apps, deployed with Bicep or `azd`, private networking | The AppHost already describes every resource |
| Analyst UI | `curl` and the Scalar page | Angular Case queue with graph visualisation | The Case, decision and audit endpoints |
| Detection | Deterministic, weighted rules | Rules blended with a per-holder anomaly score, later supervised models | Detection returns Rule Hits and a Score; a model adds a hit source |
| Rule changes | Rule-Tuning Report, human edits the file (ADR-0012) | Change request with model-risk owner sign-off, four-eyes approval and a shadow run | The report and its What-ifs |
| Agent evaluation | Seeded Agent Sample, Judge on the same deployment (ADR-0016) | Versioned golden set with human-rated Findings, calibrated Judge, CI gate on prompt changes | The evaluation command and its report |

## Ingestion and processing

Today the API scores each batch synchronously inside the ingest request, then queues any Case it opened on an in-process channel. In production:

- **Event Hubs** takes the portal's events, fed by CDC from each Source System, and a consumer normalises them into Portal Events and calls the same ingest code. Ingestion is already idempotent on `EventId`, so at-least-once delivery is safe.
- **Service Bus** replaces the channel behind `ICaseQueue`. The in-process design assumes one API instance: the budget guard counts Investigations started today with a single worker taking one Case at a time (ADR-0004). With several workers, that count needs a shared limiter, such as a Postgres row lock or a Service Bus session.
- The startup scan for unfinished Investigations remains as a safety net, but a durable queue makes it a rarity rather than the recovery path.

## Data stores

- **Postgres stays the source of truth** for Portal Events, Alerts, Cases, decisions and Audit Entries. Moving to Azure Database for PostgreSQL is a connection string; Azure SQL is an EF Core provider swap and a new migration set.
- **The graph stays a Projection.** Because it can be wiped and rebuilt from Postgres (ADR-0015), moving to Neo4j Aura, or adding Stardog beside it, is a rebuild, not a migration. Stardog would express the same model as RDF/OWL, which adds reasoning (for example, inferring that two Holders are one person) and virtual graphs over source systems without copying their data. It is parked because it has no free self-hosted licence (ADR-0003).
- Retention and erasure need a policy: append-only events and Audit Entries must be kept for the regulator, while GDPR erasure requests must still be honoured, for example by crypto-shredding identifiers.

## Detection: from rules to probabilistic

The rules engine stays: it is explainable, cheap and fast, and every Alert can be reproduced from its Rules Version. What changes is what sits beside it.

1. **Per-holder anomaly score.** A Baseline per Holder (usual countries, devices, login times and frequency) gives an anomaly score for each event. It enters detection as another source of Rule Hits with points, so the Score, Risk Bands and explanations keep working.
2. **Supervised models** once there are enough Analyst Decisions to learn from. A logistic regression over Rule Hits and anomaly features is the first step: it replaces hand-set points with learned weights while staying explainable. ADR-0012 records why the MVP doesn't do this yet: it needs far more labelled decisions, and a model-risk reviewer has to validate coefficients rather than read points.
3. **The slow-burn fraudster**, the known miss that evades every Window, is the case for this path: no fixed Window catches it, but an anomaly score over weeks can.

## Rule governance

The Rule-Tuning Report already suggests and measures, but never applies (ADR-0012). In production it becomes a **change request** reviewed by a model-risk owner, with **four-eyes approval** before a Rules Version is promoted. The candidate version first runs in **shadow** beside the live one, scoring real Alerts without opening Cases, so its effect is measured on live traffic, not only on the Dataset's What-if. Decisions from real analysts replace the Stand-in Analyst, and the report is run on a schedule.

## Agents and their evaluation

- **The Agent Sample becomes a golden set** (ADR-0016): a fixed, versioned set of Cases with human-rated Findings. The Judge is calibrated against those ratings, ideally on a different model from the Investigator to remove self-preference bias. A prompt change whose Evidence Grounding or Verdict Agreement drops below a threshold fails in CI.
- **The Scenario Generator runs on a schedule**, and each new Blind Spot becomes a backlog item for a Rule or an anomaly feature.
- **Feedback to prompts and models.** Labelled Analyst Decisions feed prompt optimisation first and, later, supervised models. The agents are never fine-tuned on unreviewed output.
- **Cost controls** move from the daily Investigation limit to per-tenant budgets and Azure cost alerts. Tokens and £ are already logged per call and per Investigation.

## Security, privacy and audit

- **Entra ID** for analysts and supervisors, with roles: an analyst decides Cases; a supervisor approves rule changes and sees the audit trail.
- **Key Vault** for every secret; **managed identity** for Azure OpenAI, Postgres and Service Bus, so no key sits in configuration.
- **Private networking**: the LLM endpoint, databases and graph reachable only from the Container Apps environment.
- Masking (ADR-0008) and the append-only Audit Entries stay as they are. The audit trail goes to immutable storage with a retention period set by the regulator.

## Observability

The services already emit OpenTelemetry traces, metrics and logs (ADR-0011). Production points the exporter at Application Insights and adds Azure Monitor alerts, for example on ingest latency, queue depth, Investigation failures, `vigil.llm.cost` per day and the false-positive rate of the live Rules Version.

## Deployment and CI/CD

- **Bicep or `azd`** generates the infrastructure from the same shape the AppHost describes: Container Apps for the API and workers, managed Postgres, Neo4j Aura, Service Bus, Event Hubs, Key Vault and Application Insights.
- **CI** builds, tests (Testcontainers needs a Docker-capable runner) and runs the free Fake evaluation on every pull request, so a rules change shows its detection and false-positive rates in review. The paid agent evaluation runs on prompt changes only, against the golden set.
- **CD** promotes through environments, with rules files and Prompt Versions released like code.

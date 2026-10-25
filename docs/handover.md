# Handover

For the engineer or team taking over Vigil. It covers:
- what is built and what state it is in;
- what it cannot do yet;
- who should own each moving part;
- how to read the three core modules without the original author.

Read these alongside it:

| Document | Read it for |
|---|---|
| [user-guide.md](user-guide.md) | How to operate the system |
| [architecture.md](architecture.md) | How it works |
| [runbook.md](runbook.md) | How to recover it |
| [adr/](adr/) | Why each decision was made |

## Status

| Stage | What it delivered | State |
|---|---|---|
| 0 Foundations | Aspire AppHost, append-only Portal Event store, idempotent ingestion, simulator | Done |
| 1 Deterministic MVP | Versioned Rules, Risk Episodes, Alerts with evidence, Cases ordered by priority, Personas and a seeded Dataset, rules-only evaluation | Done |
| 2 Knowledge graph | Neo4j Projection inside ingestion, Graph Rules G1–G3, Neighbour re-scoring, `rebuild-graph`, graph off vs on evaluation | Done |
| 3 Agentic investigation | MAF workflow (Enrich → Investigate → Await Approval → Act), Investigator with three Account-bound tools, code validation, Reviewer, masking, Postgres checkpoints, Analyst Decision, Intervention, audit, one trace | Done |
| 4 Evaluation and feedback | Agent Sample with Evidence Grounding, Verdict Agreement and a Judge; red-team Scenario Generator and Blind Spots; Travelling holder Persona; Stand-in Analyst; Rule-Tuning Report with What-if | Done |
| 5 Validation and handover | Clean-start run of every flow, real Rule-Tuning Report, user guide, this document | In progress (see [Outstanding](#outstanding)) |

Latest measured results:

| Measure | Value | Source |
|---|---|---|
| Detection rate, graph off (v1) | 17/30 | `eval-reports/20261024T162209Z-v1-v2-seed42.md` |
| Detection rate, graph on (v2) | 22/30 | same report |
| False-positive rate | 20/970, all Travelling holders | same report |
| Early-detection rate | 9/9 | same report |
| Known miss | The slow-burn fraudster, 0/8 | same report |
| Rule-Tuning Report (43 decisions) | Suggests R0 10→5, R1 30→15 and R2 20→10. Each What-if removes all 20 false positives and all early detection. | `eval-reports/rule-tuning-20261024T172201Z-v2-seed42.md` |

The agent metrics in the committed evaluation report come from the Fake provider and mean nothing yet.

## Outstanding

| Item | Owner | How |
|---|---|---|
| Real agent evaluation (Azure, Agent Sample of 25) | Developer with the Azure key | [User guide 17](user-guide.md#17-run-the-evaluation-with-azure-openai) |
| Real Scenario Generator run; replace the Fake placeholder in `datasets/generated/` | Developer with the Azure key | [User guide 18](user-guide.md#18-find-blind-spots-with-the-scenario-generator) |
| Check total LLM spend against the £5–10 budget in the Azure cost view | Developer | [User guide 3.2](user-guide.md#32-set-a-budget-alert-before-you-spend-money) |
| Review of each core module by an engineer who did not write it | Receiving engineer | [Reading the core modules](#reading-the-core-modules) below |
| Decide whether to accept any Rule-Tuning suggestion | Fraud lead, with model risk | ADR-0012. The What-ifs say no: each one costs all early detection. |

## Known limits

- **Slow-burn fraud is missed.** It spaces each step just beyond its Rule's Window, and no fixed Window can catch it. The planned answer is a per-Holder anomaly score ([industrialisation.md](industrialisation.md)).
- **Travelling holders are false positives.** A genuine Holder who resets, enrols MFA and changes phone abroad reaches High. Every weight cut that clears them also delays every takeover that has no graph evidence. This needs a better signal, not a lower weight.
- **Graph Rules are one hop** (ADR-0014). G3 does not follow chains of different identifiers, and G2 does not look two hops out.
- **One API instance.** The in-process Case queue and the one-at-a-time worker assume it (ADR-0004).
- **Two stores, no shared transaction.** A Postgres commit failure after the Neo4j write can leave stray links until a retry or a rebuild (ADR-0015).
- **Budget-skipped Cases are never re-queued.** A Case over `Investigation:DailyLimit` waits Open for a human with no Finding.
- **The Judge may favour its own model.** It runs on the same deployment as the Investigator (ADR-0016).
- **No authentication.** Anyone who can reach the API can decide a Case. Entra ID is on the industrialisation path.
- **Vigil records Interventions but does not enforce them.** The portal must read and apply them.

## Ownership

| What | Lives in | Suggested owner | Change process |
|---|---|---|---|
| Rules and weights | `config/rules.vN.json`, `Rules:Path` | Fraud lead | A new version file, compared by evaluation, approved by a person (ADR-0012, [user guide 20](user-guide.md#20-change-a-rule-safely)) |
| Agent prompts | `config/prompts/*.vN.md` | AI engineer | A new version file, measured by the Azure evaluation before release; the file name is the Prompt Version |
| LLM budget | `Investigation:DailyLimit`, `Llm:*Price*`, the Azure budget alert | Engineering lead | Settings change; check spend against the cost view monthly |
| Evaluation and Rule-Tuning Reports | `eval-reports/` | Fraud lead and AI engineer | Run after every Rules or prompt change; commit the report with the change |
| Generated Scenarios and Blind Spots | `datasets/generated/` | Fraud lead | Review each new Blind Spot, then decide whether it needs a Rule |
| Interventions | `interventions` table | Portal team | The portal reads and enforces them |
| Secrets | AppHost user-secrets (MVP) | Engineering lead | Key Vault and managed identity in production |

## Open questions

The open questions for production are in [PROJECT.md Section 19](../PROJECT.md#19-design-principles-and-open-questions).

The Stage 5 run added one more. The Rule-Tuning Report shows that weights alone cannot remove the Travelling holder false positives. Which new signal should separate them? Candidates:
- a travel notice that the Holder confirms;
- a device later seen on the Holder's usual network;
- the anomaly baseline.

## Reading the core modules

`Vigil.Detection`, `Vigil.Graph` and `Vigil.Agents` hold the logic that decides what is risky and what the AI may do. Each is small and depends only on `Vigil.Domain`. Read each in the order given. For each file:
- know what it is for;
- check the line marked "read carefully";
- run the named tests while you read.

### Vigil.Detection

Main flow: `Detector.Detect` → `SequenceMatcher` / `FirstSighting` for each event and Rule → `RiskEpisodes.Assemble` → Alerts.

| Order | File | What it does |
|---|---|---|
| 1 | `Detector.cs` | The single entry point. It orders the timeline, collects candidate hits from every Rule, adds the graph hits, and assembles Alerts. |
| 2 | `SequenceMatcher.cs` | One two-step Rule on one event: this event is step B, and the latest earlier step-A event must be inside the Window. |
| 3 | `FirstSighting.cs` | R0: a device or country not seen earlier on the Account. |
| 4 | `RiskEpisodes.cs` | Places each hit in an Alert, one hit per Rule per Alert, and never rewrites a stored hit. |

**Read carefully:** in `RiskEpisodes.EpisodeFor`, the fallback after `previous ??`. A late event can complete a pattern before an Alert that is already stored. The hit then joins that Alert instead of opening an earlier one, which keeps an analyst's view of past Alerts stable (ADR-0013).

Tests: `tests/Vigil.Detection.Tests`. They cover Windows, boundaries, out-of-order events, duplicates and episodes.

### Vigil.Graph

Main flow: `GraphProjector.ProjectAsync` → MERGE per event → `GraphRules.NeighboursOfAsync` → `GraphRules.HitsForAsync` per Account.

| Order | File | What it does |
|---|---|---|
| 1 | `IdentifierKind.cs` | Which event field or attribute becomes which node and relationship, and which identifiers are Linking Identifiers |
| 2 | `GraphProjector.cs` | MERGEs Holders, Accounts and identifiers. It keeps each link's earliest `firstSeen` and `firstEventId`, creates constraints, and rebuilds. |
| 3 | `GraphRules.cs` | The three Cypher queries (G1 `SharedBankAccount`, G2 `KnownFraudProximity`, G3 `Ring`) and the Neighbour lookups |
| 4 | `GraphNeighbourhood.cs` | The one-hop view the Investigator's graph tool reads |

**Read carefully:** the `Ring` query and `RingAsync` in `GraphRules.cs`, where `links[^1].At` is the trigger time. G3's trigger time is the moment the group, with this Account in it, reached the threshold. That is the latest of this Account's link and the earliest links of `accountThreshold - 1` others. So the first member of a Ring is lifted by the third member's event (ADR-0014).

Tests: `tests/Vigil.Graph.Tests`, which run against a real Neo4j through Testcontainers.

### Vigil.Agents

Main flow: `InvestigationWorkflow.InvestigateAsync` → `EnrichExecutor` (`Enrichment`, `MaskedEvidence`) → `InvestigateExecutor` → `InvestigationLoop.RunAsync` (`Investigator` → `FindingValidation` → `Reviewer`) → `RequestPort` pause → `DecideAsync` → Act.

| Order | Folder and file | What it does |
|---|---|---|
| 1 | `Masking/PseudonymMasker.cs` | Per-Investigation Pseudonyms, replaced by value in one regex pass (ADR-0008) |
| 2 | `Evidence/EvidenceRefTable.cs` | `E-n` and `G-n` refs and the EventIds behind them; `FindUnknown` |
| 3 | `Investigation/MaskedEvidence.cs` | Writes every line the agents see: registers identifiers, issues refs, masks |
| 4 | `Investigation/Enrichment.cs`, `InvestigatorTools.cs`, `Investigator.cs` | The brief, the three Account-bound tools, and the agent |
| 5 | `Review/FindingValidation.cs`, `Reviewer.cs` | Code checks first, then the Reviewer on cited evidence only |
| 6 | `Review/InvestigationLoop.cs` | Investigate → Validate → Review with one retry; every path returns an outcome |
| 7 | `Workflow/InvestigationWorkflow.cs`, `InvestigationExecutors.cs`, `DecisionCheckpointStore.cs` | The MAF workflow, the approval pause and the resume |
| 8 | `Llm/` | Provider switch, the Fake, token logging (ADR-0009) |

**Read carefully:** in `InvestigationLoop.RunAsync`, the line `var unknownRefs = enrichment.Evidence.Refs.FindUnknown(...)`. It is taken on each attempt, before validation. A ref the tools issue during the retry would otherwise make the first attempt look grounded, and Evidence Grounding would always read 100% (ADR-0016).

**Also read:** the constants at the top of `InvestigationWorkflow` (`enrich`, `investigate`, `await-approval`, `act`). They are part of every checkpoint. Renaming one strands every Case already waiting for a decision (ADR-0005).

Tests:
- `tests/Vigil.Agents.Tests`: masking, refs, validation, the loop and the workflow paths, with a scripted LLM.
- `tests/Vigil.Api.Tests`: restart, decision and Act.

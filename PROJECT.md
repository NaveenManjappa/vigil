# Vigil — Agentic Account-Takeover Detection for Investor Portals

> **Vigil** *(noun)*: a period of keeping awake to watch over something.
> Vigil watches every investor account journey, spots takeover and registration fraud **before** bank details change or shares are sold, investigates with AI agents, and lets a human decide.

This document is the single source of truth for the project: what it is, why each decision was made, how it is built in stages, and how each stage is verified. It is written to be read by **both** the developer (for interview preparation) and **Claude Code** (as build instructions).

---

## Table of contents

1. [Why this project exists](#1-why-this-project-exists)
2. [The application in plain English](#2-the-application-in-plain-english)
3. [End-to-end flow (worked example)](#3-end-to-end-flow-worked-example)
4. [Architecture](#4-architecture)
5. [Tech stack](#5-tech-stack)
6. [Domain model](#6-domain-model)
7. [Detection: rules, windows, scoring, bands](#7-detection-rules-windows-scoring-bands)
8. [Knowledge graph (Neo4j)](#8-knowledge-graph-neo4j)
9. [Agents and workflow (Microsoft Agent Framework)](#9-agents-and-workflow-microsoft-agent-framework)
10. [Simulation, evaluation and feedback](#10-simulation-evaluation-and-feedback)
11. [Engineering: observability, security, testing, CI](#11-engineering-observability-security-testing-ci)
12. [API surface](#12-api-surface)
13. [Solution structure](#13-solution-structure)
14. [Build stages](#14-build-stages)
15. [Documentation plan](#15-documentation-plan)
16. [Job-spec coverage map](#16-job-spec-coverage-map)
17. [Out of scope (parked) and the industrialisation path](#17-out-of-scope-parked-and-the-industrialisation-path)
18. [Working agreement for Claude Code](#18-working-agreement-for-claude-code)
19. [Interview talking points](#19-interview-talking-points)
20. [Glossary](#20-glossary)

---

## 1. Why this project exists

Interview preparation for **Senior AI Engineer – Fraud Detection**. The client runs online investor portals where shareholders view holdings, manage payment instructions, and sell or transfer shares. The engagement:

- **MVP:** deterministic, rules-based detection of **account takeover (ATO)** and **registration fraud**, scoring sequences of events and routing high-risk accounts for intervention.
- **Industrialisation:** extend with probabilistic generative AI, graph analytics and agentic investigation.

Vigil reproduces that journey end to end on synthetic data so every architecture decision can be explained, defended and changed during the interview.

**Constraints**

| Constraint | Value |
|---|---|
| Time | ~4 build days + 1 rehearsal day |
| Budget | £5–10 total (Azure OpenAI pay-as-you-go) |
| Builder | Claude Code, in stages; developer must understand flow and code at a high level, and core modules deeply |
| Interview style | Oral technical discussion / code walkthrough; candidate will state upfront that MAF experience is new |

---

## 2. The application in plain English

**Definition.** Vigil ingests every action holders take on an investor portal (logins, password resets, MFA changes, contact/address changes, bank-detail changes, sale/transfer requests). It detects **sequences** of actions that match known fraud patterns inside **time windows**, produces an explainable **risk score and band**, enriches risky accounts with **knowledge-graph** context (shared devices, IPs, bank accounts, known fraud), runs an **agentic investigation** that writes an evidence-backed case summary, and pauses for a **human analyst** to approve any intervention. Analyst decisions feed back into evaluation and rule tuning.

**Analogy — a bank branch's security team.**

| Security team | Vigil component |
|---|---|
| CCTV recording everything | Event ingestion + event store |
| Rulebook ("new card + address change + cash withdrawal within an hour = suspicious") | Rules engine with time windows |
| Wall of photos connected with string | Neo4j knowledge graph |
| Detective writing up a case | Investigator agent (MAF) |
| Second detective checking the write-up | Reviewer agent (MAF) |
| Supervisor who signs off before freezing an account | Human-in-the-loop approval |
| Weekly review of which alarms were real | Evaluation harness + feedback loop |

**Core design principle:** *cheap, predictable rules filter everything; expensive AI only looks at the risky few; a human makes every consequential decision.*

---

## 3. End-to-end flow (worked example)

### 3.1 Fraud case — Margaret

Margaret holds 2,000 shares (~£18,000) and logs in a few times a year from a laptop in Reading. A fraudster has her email password.

| Time | Event | Rule fired | Score | Band |
|---|---|---|---|---|
| Mon 09:00 | `PasswordReset` from new device `D-77`, IP in Romania | R0 new device + new country (+10) | 10 | Low |
| Mon 09:00 | Graph: `D-77` already used on 3 other Accounts, one Confirmed Fraud | G2 device linked to known fraud (+25), G3 ring indicator (+20) | 55 | Medium |
| Mon 09:04 | `MfaEnrolled` (new phone) | R1 reset → MFA within 1h (+30) | 85 | **High → case opened** |
| Mon 09:10 | `EmailChanged` | R2 contact change within 24h of reset (+20) | **105** | **Critical** |
| Mon 09:11 | MAF workflow: Investigator → Reviewer → **pause for analyst** | — | — | — |
| Mon 09:30 | Analyst approves: block bank changes 7 days, contact holder by post | — | — | — |
| Tue 14:00 | Fraudster attempts `BankDetailsChanged` → **blocked** | (R3 would have fired) | — | — |

**Detected before the bank-detail change** — the explicit MVP goal.

Vigil records the analyst's block as an Intervention for the portal to enforce; it does not reject later events itself, so the simulator still posts Tuesday's `BankDetailsChanged`, and the live Alert goes on to add R3 (+30) and ends at 135.

Explainable alert:

```
Alert A-501  ACC-MARGARET  Score 105  CRITICAL   rules v2
 ├─ R0 +10  E-1001 PasswordReset            (new device D-77, new country RO)
 ├─ G2 +25  D-77 ─USED_BY→ ACC-PRIYA        (confirmed fraud, 2026-09-14)
 ├─ G3 +20  D-77 shared by 4 Accounts       (ACC-DEREK, ACC-PRIYA, ACC-LENA; Ring threshold 3)
 ├─ R1 +30  E-1001 → E-1002                 (reset → MFA enrol, 4 min; window 1h)
 └─ R2 +20  E-1001 → E-1003                 (reset → email change, 10 min; window 24h)
```

Agent case summary (cites evidence IDs; PII masked):

> Likely account takeover. 3 years of consistent UK laptop activity, then password reset, new MFA device and email change within 10 minutes from a Romanian IP. Device D-77 is linked to confirmed fraud case ACC-PRIYA. **Recommend:** block bank-detail changes for 7 days; contact holder by post at address on file. *Evidence: E-1001, E-1002, E-1003, graph path G-12.*

### 3.2 Genuine case — Mr Patel moves house

Usual laptop, usual IP. `AddressChanged`, then two days later `PhoneChanged`. No reset, no new device → no rule fires → score 0–10, **Low**. No case, **no LLM call, zero cost**.

### 3.3 The flow in one picture

```
Simulator ──► Ingestion API ──► Event Store (Postgres)
                    │
                    ├──► Rules Engine ── score + band + reason codes ──► Alert
                    │
                    └──► Graph Projector ──► Neo4j
                                               │
          Alert ≥ High ──► MAF Workflow ───────┘ (graph features + GraphRAG)
                           Enrich → Investigator → Reviewer → [Human approval] → Action (stub)
                                                                      │
                                         Analyst decision ──► Feedback store ──► Eval + rule-tuning report
```

---

## 4. Architecture

### 4.1 Components

| # | Component | Responsibility | Type |
|---|---|---|---|
| 1 | **Simulator** | Generates synthetic holders and labelled fraud personas; posts events to the API on a simulated clock | Console app |
| 2 | **Ingestion API** | Validates and normalises events into a canonical schema; persists; triggers detection + graph projection | ASP.NET Core Minimal API |
| 3 | **Event Store** | Append-only event history per account | PostgreSQL via EF Core |
| 4 | **Rules Engine** | Sliding-window sequence matching, weighted scoring, banding, reason codes | Class library (pure C#) |
| 5 | **Graph Projector** | Upserts nodes/relationships into Neo4j for every event | Class library + Neo4j .NET driver |
| 6 | **Graph Queries** | Cypher queries for shared identifiers, known-fraud proximity, ring detection | Class library |
| 7 | **Case Service** | Opens cases for High/Critical alerts, tracks status, stores analyst decisions | Part of API |
| 8 | **Investigation Workflow** | MAF workflow: enrich → investigate → review → human approval → action | Class library (MAF) |
| 9 | **Evaluation** | Replays labelled datasets; computes metrics; Scenario Generator agent; rule-tuning report | Console app + library |
| 10 | **AppHost** | Starts Postgres, Neo4j, API; wires config; hosts the tracing dashboard | Aspire |

### 4.2 Processing model

- **MVP:** synchronous per event inside the API request → rules → graph projection. When an alert crosses the case threshold, the investigation workflow is started **in the background** (hosted service + in-process `Channel<T>`), so ingestion stays fast.
- **Why in-process queue, not Event Hubs/Service Bus:** one process, zero cost, easy to debug. The `Channel<T>` sits behind an interface (`ICaseQueue`) so it can be swapped for Azure Service Bus during industrialisation. *(ADR-004)*

### 4.3 Key architectural decisions (summary — full ADRs in `docs/adr/`)

| ADR | Decision | Why (short) |
|---|---|---|
| 001 | Rules first, AI second | Explainable, auditable, fast, free; AI only on risky cases |
| 002 | Rules defined in config (JSON), not code | BA/analysts can tune without a deploy; versioned for audit |
| 003 | Neo4j (not Stardog) for MVP | Free local Docker, mature .NET driver, Cypher is readable; Stardog has no free self-hosted licence |
| 004 | In-process queue behind an interface | Zero cost now; swap to Service Bus later |
| 005 | MAF **workflow** with agents inside steps, not one free-roaming agent | Fixed steps are testable; AI is boxed in; human step cannot be skipped |
| 006 | Investigator + Reviewer (two agents) | Reviewer checks claims against evidence → reduces hallucination |
| 007 | GraphRAG = structured Cypher retrieval via tools, no vector DB | Fraud evidence is relational, not semantic text; no embeddings needed |
| 008 | Mask PII before any LLM call | GDPR / data minimisation; the LLM reasons on patterns, not identities |
| 009 | LLM provider behind `IChatClient` (config switch) | Azure OpenAI now; Ollama/OpenAI swappable for tests or cost |
| 010 | PostgreSQL for event store | Native ARM on MacBook, light, free; EF Core makes SQL Server/Azure SQL a provider swap |
| 011 | OpenTelemetry + Aspire dashboard for observability | Free, local, standard; same traces go to Application Insights in production |
| 012 | Human approves rule-weight changes; system never self-tunes | Model-risk governance in regulated financial services |

---

## 5. Tech stack

| Layer | Choice | Notes |
|---|---|---|
| Language / runtime | **C# / .NET 10** (current LTS) | Async, DI, Minimal APIs |
| Agents | **Microsoft Agent Framework 1.x** (`Microsoft.Agents.AI`, `Microsoft.Agents.AI.Workflows`) | Agents, function tools, workflows, human-in-the-loop, checkpointing. Verify exact API names against current docs at build time — the framework moves quickly. |
| LLM | **Azure OpenAI** — one small, cheap chat model (e.g. gpt-4.1-mini or the current equivalent), deployed via the **Microsoft Foundry** portal | Requires a Pay-As-You-Go subscription (free-trial subscriptions have zero model quota). Budget £5–10. |
| AI abstraction | `Microsoft.Extensions.AI` (`IChatClient`) | Provider-agnostic; enables fake clients in tests |
| Knowledge graph | **Neo4j 5 Community** (Docker) + `Neo4j.Driver` | Cypher. AuraDB Free (50k nodes / 175k rels) is a fallback. |
| Event store | **PostgreSQL** (Docker) + EF Core | Append-only events, alerts, cases, decisions, audit |
| Orchestration (local) | **Aspire** (AppHost) | One command starts everything; built-in OpenTelemetry dashboard |
| Observability | **OpenTelemetry** → Aspire dashboard (MVP); Application Insights (production path) | Traces, metrics, logs, token usage, latency |
| Testing | xUnit, **Testcontainers** (Postgres, Neo4j), fake `IChatClient` | No real LLM calls in unit/CI tests |
| API docs | OpenAPI + Scalar UI | Demo surface instead of a custom UI |
| CI | **GitHub Actions** | Build + unit + integration tests on every push |
| Secrets (local) | .NET user-secrets | Key Vault is the production path |

**Not used (and why):** Stardog (no free self-hosted licence; parked), vector database (not needed — see ADR-007), Python (role is C#-first), custom UI (Swagger/Scalar + Aspire dashboard are enough for a walkthrough).

---

## 6. Domain model

### 6.1 Canonical event

```csharp
public sealed record PortalEvent(
    Guid EventId,
    string AccountId,
    string HolderId,
    EventType Type,
    DateTimeOffset OccurredAt,
    string? DeviceId,
    string? IpAddress,
    string? Country,
    string Channel,                       // "web", "mobile", "call-centre"
    IReadOnlyDictionary<string, string> Attributes  // e.g. NewEmail, NewBankAccountHash, Amount
);
```

### 6.2 Event types (source system → type)

| Source system | Event types |
|---|---|
| Authentication | `LoginSucceeded`, `LoginFailed` |
| Account recovery | `PasswordResetRequested`, `PasswordReset` |
| MFA | `MfaEnrolled`, `MfaRemoved` |
| Account maintenance | `EmailChanged`, `PhoneChanged`, `AddressChanged` |
| Registration | `AccountRegistered` |
| Payment instructions | `BankDetailsChanged` |
| Transactions | `SaleRequested`, `TransferRequested` |

### 6.3 Core entities

| Entity | Key fields |
|---|---|
| `Holder` | HolderId, masked name, registered date |
| `Account` | AccountId, HolderId, holding value |
| `Alert` | AlertId, AccountId, Score, Band, RuleHits[], RulesVersion, CreatedAt |
| `RuleHit` | RuleId, Points, EvidenceEventIds[], Explanation |
| `Case` | CaseId, AlertId, Status (`Open` → `Investigating` → `AwaitingApproval` → `Closed`), AgentSummary, Recommendation |
| `AnalystDecision` | CaseId, Decision (`ConfirmedFraud` / `FalsePositive`), ActionApproved, AnalystId, Notes, DecidedAt |
| `AuditEntry` | Who/what, action, model + prompt version, evidence IDs, timestamp |

---

## 7. Detection: rules, windows, scoring, bands

### 7.1 How a time window works

Each time an event arrives for an account, the engine looks **back** over that account's recent events and asks: *"Does this new event complete any known pattern within the pattern's time window?"* Only events inside the window count — like a sliding ruler laid along the account's timeline.

### 7.2 Rule catalogue (MVP, `rules.v1.json`)

| ID | Name | Pattern | Window | Points |
|---|---|---|---|---|
| R0 | New device / new country | First sighting of device or country for this account | — | +10 |
| R1 | Recovery takeover | `PasswordReset` → `MfaEnrolled` | 1h | +30 |
| R2 | Contact hijack | `PasswordReset` → `EmailChanged` or `PhoneChanged` | 24h | +20 |
| R3 | Payout redirect | `EmailChanged`/`PhoneChanged`/`AddressChanged` → `BankDetailsChanged` | 72h | +30 |
| R4 | Cash-out | `BankDetailsChanged` → `SaleRequested`/`TransferRequested` | 7d | +40 |
| R5 | Fast-track registration | `AccountRegistered` → `BankDetailsChanged` | 48h | +30 |
| R6 | Register-and-sell | `AccountRegistered` → `SaleRequested`/`TransferRequested` | 14d | +30 |
| G1 | Shared bank account | Bank account linked to another, unrelated holder | graph | +30 |
| G2 | Known-fraud proximity | Any identifier ≤ 2 hops from a confirmed-fraud account | graph | +25 |
| G3 | Ring indicator | ≥ 3 accounts connected via shared identifiers | graph | +20 |

Example rule definition:

```json
{
  "id": "R1",
  "name": "Recovery takeover",
  "sequence": ["PasswordReset", "MfaEnrolled"],
  "windowMinutes": 60,
  "points": 30,
  "explanation": "Password reset followed by new MFA enrolment within {window}"
}
```

### 7.3 Scoring and bands

- **Score** = sum of points of distinct rule hits within the account's active risk period (default 7 days). *Stage 1 refines this: an Alert is one Risk Episode, which stays open while each hit is within 7 days of the previous one, and its Score never decreases (ADR-0013).*
- **Bands:** `0–29 Low` · `30–59 Medium` · `60–89 High` · `90+ Critical`.
- **Routing:** High/Critical → open a case and start the investigation workflow. Cases are prioritised by band, then score, then holding value.
- **Explainability:** every alert stores rule hits with evidence event IDs and the rules version that produced it.

### 7.4 Edge cases the engine must handle (tested)

- Events arriving out of order (sort by `OccurredAt`, not arrival).
- Events exactly on a window boundary (inclusive/exclusive is defined and tested).
- The same rule firing repeatedly (count once per risk period).
- Duplicate events (idempotent on `EventId`).

---

## 8. Knowledge graph (Neo4j)

### 8.1 Graph model

**Nodes:** `Holder`, `Account`, `Registration`, `Device`, `IpAddress`, `Email`, `Phone`, `BankAccount`, `Address`, `Transaction`, `Case`.

**Relationships:**

```
(Holder)-[:OWNS]->(Account)
(Account)-[:REGISTERED_VIA]->(Registration)
(Account)-[:USED_DEVICE {firstSeen, lastSeen}]->(Device)
(Account)-[:SEEN_FROM {firstSeen, lastSeen}]->(IpAddress)
(Account)-[:HAS_EMAIL {from, to}]->(Email)
(Account)-[:HAS_PHONE {from, to}]->(Phone)
(Account)-[:PAYS_TO {from, to}]->(BankAccount)
(Account)-[:LIVES_AT {from, to}]->(Address)
(Account)-[:MADE]->(Transaction)
(Case)-[:CONCERNS]->(Account)          // Case.outcome = 'ConfirmedFraud' marks known fraud
```

Time-bounded relationships (`from`/`to`) keep history: an old email stays linked, so "email previously used by a fraudster" is still findable.

### 8.2 Key queries

| Query | Purpose |
|---|---|
| Shared identifiers | Which other accounts share this device/IP/email/phone/bank account? |
| Known-fraud proximity | Is any identifier within 2 hops of a confirmed-fraud account? |
| Ring detection | Find connected groups of ≥ 3 accounts linked by shared identifiers |
| Account neighbourhood | Compact sub-graph summary for the Investigator agent (GraphRAG) |

Example — known-fraud proximity:

```cypher
MATCH (a:Account {accountId: $accountId})-[r1]->(id)<-[r2]-(other:Account)
MATCH (c:Case {outcome: 'ConfirmedFraud'})-[:CONCERNS]->(other)
RETURN labels(id)[0] AS sharedVia, id.value AS identifier, other.accountId AS fraudAccount
LIMIT 25
```

### 8.3 How the graph feeds detection and agents

1. **Graph features → score:** G1–G3 rules add points after projection.
2. **GraphRAG → agent:** the Investigator calls `QueryGraphNeighbourhood`, which runs Cypher and returns a compact, PII-masked text/JSON summary used as evidence.

---

## 9. Agents and workflow (Microsoft Agent Framework)

### 9.1 Workflow

```
[Enrich]  ──►  [Investigate]  ──►  [Review]  ──►  [Await Approval]  ──►  [Act]
 code           Investigator        Reviewer        human-in-the-loop      code (stub)
                agent + tools       agent           (pause / resume)
                    ▲                  │
                    └── rejected ──────┘  (max 1 retry, then escalate to human with flag)
```

| Step | Kind | Input | Output |
|---|---|---|---|
| Enrich | Deterministic executor | Alert | Timeline, rule hits, graph summary (masked) |
| Investigate | **Investigator agent** (LLM + tools) | Enriched context | Draft finding: verdict, reasoning, recommendation, evidence IDs |
| Review | **Reviewer agent** (LLM, no tools) | Draft + evidence | `Approved` or `Rejected` with reasons (e.g. "E-1009 cited but not in evidence") |
| Await Approval | Human-in-the-loop request | Final summary | Analyst decision |
| Act | Deterministic executor | Decision | Stubbed action (block bank changes / contact holder / close as false positive) + audit entry |

**Checkpointing:** workflow state is checkpointed so a case awaiting approval survives a restart.

### 9.2 Investigator tools (C# function tools)

| Tool | Returns |
|---|---|
| `GetAccountTimeline(accountId, days)` | Masked chronological events |
| `GetRuleHits(alertId)` | Rule hits with evidence IDs |
| `QueryGraphNeighbourhood(accountId)` | Shared identifiers, linked accounts, fraud proximity |
| `GetKnownFraudMatches(accountId)` | Confirmed-fraud accounts within 2 hops |
| `GetHolderBaseline(accountId)` | Usual countries, devices, login frequency |

### 9.3 Agent output contract (structured output)

```json
{
  "verdict": "LikelyAccountTakeover | LikelyRegistrationFraud | Inconclusive | LikelyGenuine",
  "confidence": "Low | Medium | High",
  "summary": "string, ≤ 120 words",
  "recommendation": "BlockBankChanges | ContactHolder | NoAction",
  "evidence": ["E-1001", "E-1002", "G-12"]
}
```

### 9.4 Guardrails

- Agents **recommend**; only the Act step changes anything, and only after human approval.
- Evidence IDs are validated in code (every cited ID must exist) before the Reviewer runs.
- PII masked before any prompt (`HOLDER-7f3a`, `EMAIL-91c2`); mapping kept server-side.
- Model name, prompt version and token usage recorded on every case (audit).
- Investigation only for High/Critical alerts (cost control).

---

## 10. Simulation, evaluation and feedback

### 10.1 Simulator personas

| Persona | Behaviour | Label | Expected outcome |
|---|---|---|---|
| Quiet holder | Occasional logins, same device | Genuine | Low |
| House mover | Address + phone change, same device | Genuine | Low |
| New phone | Password reset + MFA enrol (legit) | Genuine | Medium (realistic false-positive pressure) |
| Active trader | Frequent sales, stable details | Genuine | Low |
| Travelling holder | Abroad on a new device: password reset + MFA enrol + phone change (legit) | Genuine | High (false positive the feedback loop learns from; added in Stage 4) |
| ATO fraudster | Reset → MFA → email → bank → sale | Fraud (ATO) | High/Critical before bank change |
| Registration fraudster | Register → bank details → sale | Fraud (Registration) | High |
| Fraud ring | 5 accounts sharing device/IP/bank account | Fraud (Ring) | Graph rules lift to High |
| Slow-burn fraudster | Same steps spaced to dodge windows | Fraud (Evasion) | **Known miss** — shows rule limits and motivates the probabilistic path |

- Seeded random generator → reproducible datasets.
- Simulated clock → "7 days" replays in seconds.
- Default dataset: 1,000 holders, ~3% fraud.

### 10.2 Scenario Generator agent (AI training agent)

An LLM agent that writes **new labelled fraud scenarios** as JSON event sequences (e.g. "fraudster who uses a call-centre channel for the contact change"). Output is schema-validated, de-duplicated, saved to `datasets/generated/`, and replayed through the simulator. Purpose: find blind spots in the rules.

### 10.3 Evaluation harness

| Metric | Definition |
|---|---|
| Detection rate (recall) | Fraud accounts flagged High+ ÷ all fraud accounts |
| Early-detection rate | Fraud flagged **before** `BankDetailsChanged` ÷ all ATO fraud |
| False-positive rate | Genuine accounts flagged High+ ÷ all genuine accounts |
| Precision per rule | Hits on fraud ÷ all hits, per rule |
| Evidence grounding | Agent summaries whose cited IDs all exist ÷ all summaries |
| Reasoning quality | LLM-as-judge score (1–5) against a rubric, on a small sample |
| Cost / latency | Tokens and seconds per investigated case |

Output: a markdown report in `eval-reports/` with a run ID, rules version and prompt version.

**Cost rule:** rules-only evaluation is free and runs any time; agent evaluation runs on a small sample (e.g. 20 cases) to stay within budget.

### 10.4 Feedback loop

1. Analyst decisions stored against each case.
2. A **rule-tuning report** shows precision per rule from real decisions and *suggests* weight changes (e.g. "R0 precision 5% → suggest +10 → +5").
3. A human edits `rules.vN.json` → new version → re-run evaluation → compare. **The system never changes its own rules.**

---

## 11. Engineering: observability, security, testing, CI

### 11.1 Observability

- **OpenTelemetry** traces from ASP.NET Core, EF Core, Neo4j calls (custom spans), and **MAF agent/tool/LLM calls**.
- **Custom metrics:** alerts by band, rule hit counts, cases opened, LLM tokens and estimated £ per case, workflow step latency, approval wait time.
- **Local:** Aspire dashboard. **Production path:** Application Insights + Azure Monitor alert rules (latency, error rate, daily token spend).
- **Two kinds of alert, kept separate:** *fraud alerts* (product feature, in Postgres, for analysts) vs *operational alerts* (system health, in monitoring, for engineers).

### 11.2 Security, privacy, audit

| Control | MVP | Production path |
|---|---|---|
| PII minimisation | Masking before LLM | + field-level encryption |
| Audit trail | `AuditEntry` for every alert, agent run and decision | Immutable store / retention policy |
| Versioning | Rules version + prompt version on every alert/case | Model registry, change approval |
| Human oversight | Mandatory approval step | Four-eyes for high-value actions |
| Secrets | user-secrets | Azure Key Vault + managed identity |
| AuthN/Z | Dev stub | Entra ID, role-based (analyst / supervisor) |

### 11.3 Testing strategy

| Level | What | Tools |
|---|---|---|
| Unit | Window logic, scoring, banding, masking, evidence validation | xUnit |
| Integration | Event store, Cypher queries against a real Neo4j | Testcontainers |
| Workflow | Workflow paths (approve / reject / reviewer-reject) with a **fake `IChatClient`** | xUnit |
| Evaluation | Replay labelled datasets; metrics thresholds | Eval console |

No real LLM calls in unit, integration or CI tests.

### 11.4 CI (GitHub Actions)

On every push / PR: restore → build → unit tests → integration tests (Testcontainers on the Ubuntu runner) → publish test results. No secrets needed; no LLM calls.

---

## 12. API surface

| Method | Route | Purpose |
|---|---|---|
| `POST` | `/events` | Ingest one event (or a batch) |
| `GET` | `/accounts/{id}/timeline` | Events for an account |
| `GET` | `/alerts?band=High` | Alerts, filterable |
| `GET` | `/alerts/{id}` | Alert with rule hits and evidence |
| `GET` | `/cases?status=AwaitingApproval` | Case queue, ordered by priority |
| `GET` | `/cases/{id}` | Case with agent summary, recommendation, graph evidence |
| `POST` | `/cases/{id}/decision` | Analyst approves / rejects → resumes workflow |
| `GET` | `/graph/accounts/{id}/neighbourhood` | Graph context (for demo) |
| `GET` | `/rules` | Active rules and version |

---

## 13. Solution structure

```
Vigil/
├─ PROJECT.md                     ← this file
├─ CLAUDE.md                      ← build conventions for Claude Code (created in Stage 0)
├─ Vigil.sln
├─ src/
│  ├─ Vigil.AppHost/              ← Aspire: Postgres, Neo4j, API, dashboard
│  ├─ Vigil.ServiceDefaults/      ← OpenTelemetry, health checks, resilience
│  ├─ Vigil.Domain/               ← events, alerts, cases, rule definitions (records)
│  ├─ Vigil.Api/                  ← Minimal API endpoints, case service, background queue
│  ├─ Vigil.Detection/            ← [HUMAN] rules engine, windows, scoring, banding
│  ├─ Vigil.Graph/                ← [HUMAN] Neo4j projector + Cypher queries
│  ├─ Vigil.Agents/               ← [HUMAN] MAF workflow, agents, tools, masking
│  ├─ Vigil.Infrastructure/       ← EF Core DbContext, repositories, audit
│  ├─ Vigil.Simulator/            ← personas, simulated clock, dataset writer
│  └─ Vigil.Evaluation/           ← replay harness, metrics, Scenario Generator agent, reports
├─ tests/
│  ├─ Vigil.Detection.Tests/
│  ├─ Vigil.Graph.Tests/
│  ├─ Vigil.Agents.Tests/
│  └─ Vigil.Api.Tests/
├─ config/
│  └─ rules.v1.json
├─ datasets/                      ← seeded + generated scenarios
├─ eval-reports/
├─ docs/
│  ├─ architecture.md
│  ├─ adr/                        ← ADR-001 … ADR-013
│  ├─ runbook.md
│  ├─ industrialisation.md
│  └─ interview-notes.md
└─ .github/workflows/ci.yml
```

---

## 14. Build stages

Each stage ends with a **"Done when"** checklist and a short **"Explain it"** list — the questions the developer must be able to answer before moving on.

### Stage 0 — Foundations (Day 1, morning)

**Build**
- Solution skeleton, Aspire AppHost with Postgres + Neo4j containers, ServiceDefaults with OpenTelemetry.
- Domain records (`PortalEvent`, `EventType`, `Alert`, `Case`, …).
- EF Core DbContext + migrations for events, alerts, cases, decisions, audit.
- `CLAUDE.md` with conventions; git repo; GitHub Actions CI (build + tests).
- Simulator v0: Margaret (ATO) and Mr Patel (genuine) as fixed scripts.

> **Stage 0 decisions (grill session, 2026-10-22):** only the `events` table is created now; alerts, cases, decisions and audit tables arrive with the stages that define their shape. ADRs keep the numbering in Section 4.3 and are written when their stage makes the decision real (Stage 0: 0003, 0010, 0011). See GLOSSARY.md and docs/adr/.

**Done when**
- [ ] `dotnet run` on AppHost starts everything; Aspire dashboard shows API, Postgres, Neo4j healthy.
- [ ] Simulator posts Margaret's and Mr Patel's events; they appear in Postgres.
- [ ] CI is green on GitHub.

**Explain it:** What does Aspire do vs MAF? What is the canonical event and why normalise?

### Stage 1 — Deterministic MVP (Day 1 afternoon → Day 2 morning)

**Build**
- Rules loaded from `config/rules.v1.json`.
- `[HUMAN]` Sliding-window sequence matcher, scorer, bander, reason codes.
- Alert persistence with evidence IDs and rules version.
- Case creation for High/Critical; case queue endpoint ordered by priority.
- Full simulator personas (Section 10.1) with seeded generation and simulated clock.
- Unit tests for all edge cases in 7.4.

> **Stage 1 decisions (grill session, 2026-10-22):** detection is one pure entry point, `Detector.Detect(timeline, ruleSet)` (Stage 1.4 adds the Account's stored Alerts as a third argument, which it only extends; see ADR-0013), run synchronously inside the ingest transaction for every Account in the batch, under a per-Account transaction-scoped Postgres advisory lock. A sequence Rule is two steps (A → B), each with alternative event types; B is the event being evaluated and A is the latest earlier event matching step A; Windows are inclusive. Each Rule Hit stores evidence EventIds (native `uuid[]`), its trigger time and an explanation; stored hits are never rewritten. The holding-value tie-break in Section 7.3 is dropped for Stage 1, because there is no Account entity yet: Cases are ordered by band, then Score, then time opened. See GLOSSARY.md, ADR-0001 and ADR-0002.

**Done when**
- [ ] Margaret is flagged **High at the email change, before any bank change**; Mr Patel stays Low.
- [ ] `GET /alerts/{id}` shows every rule hit with evidence event IDs.
- [ ] Rules-only evaluation report runs on the 1,000-holder dataset (detection rate, FPR, per-rule precision).

**Explain it:** How does a sliding window work? Why config-driven rules? Why does the slow-burn fraudster get missed, and what would fix it?

### Stage 2 — Knowledge graph (Day 2)

**Build**
- `[HUMAN]` Graph projector: every event upserts nodes/relationships (idempotent `MERGE`).
- `[HUMAN]` Cypher queries: shared identifiers, known-fraud proximity, ring detection, neighbourhood summary.
- Graph rules G1–G3 feeding the score.
- Integration tests with Testcontainers Neo4j.
- Seed a few confirmed-fraud cases so proximity has something to find.

> **Stage 2 decisions (grill session, 2026-10-23):** Neo4j holds a disposable Projection of Postgres, MERGE'd inside the ingest request before the Postgres commit, so a Neo4j failure fails the request and the retry converges both stores; a `rebuild-graph` command re-projects everything from Postgres. Device, BankAccount, Email and Phone are Linking Identifiers; IP addresses and postal Addresses are projected but never link. Relationships carry only `firstSeen` and the EventId that set it, not the `from`/`to` periods drawn in Section 8.1. Graph Rules G1 Shared bank account (+30), G2 Known-fraud proximity (+25) and G3 Ring indicator (+20) are one-hop Cypher queries that read the graph as it is now and return candidate Rule Hits to the still-pure Detector, which places them in Risk Episodes like any other. G2 is one hop, not the two in Section 8.2, and G3 counts the Accounts on one shared identifier rather than a connected group. After each batch, ingestion re-checks the Neighbours, so the first member of a Ring is lifted by the third. Graph Rules live in `rules.v2.json`, which the API now loads; v1 is unchanged. A minimal Confirmed Fraud record (Source = Seed, from Scenario files) gives G2 something to find. The generated Dataset has none, so G2 is demonstrated by Scenarios only. Evaluation replays the Dataset into a throwaway Neo4j in time order and reports v1 (graph off) against v2 (graph on). The neighbourhood summary is deferred to Stage 3's GraphRAG tools. See GLOSSARY.md, ADR-0014 and ADR-0015.

**Done when**
- [ ] Neo4j Browser shows Margaret's account linked via `D-77` to the ring accounts.
- [ ] Ring persona is lifted to High by graph rules even though per-account rules alone score Medium.
- [ ] Evaluation report shows the change in detection rate with graph rules on vs off.

**Explain it:** Why a graph rather than SQL joins? What does `MERGE` do? How do time-bounded relationships help?

### Stage 3 — Agentic investigation with MAF (Day 3)

**Build**
- Azure OpenAI deployment (via Foundry portal), `IChatClient` registration, provider switch in config.
- PII masking service.
- `[HUMAN]` Investigator agent + function tools; Reviewer agent; structured output contract.
- `[HUMAN]` Workflow: Enrich → Investigate → Review → Await Approval → Act, with checkpointing.
- `POST /cases/{id}/decision` resumes the workflow; Act step writes audit entry.
- Workflow tests with a fake `IChatClient` (approve, reject, reviewer-reject paths).
- OpenTelemetry on agents; token/cost metrics.

> **Stage 3 decisions (grill session, 2026-10-23):** spec in GitHub issue #28, built as sub-issues #29–#36. Each Case gets one Investigation, enqueued on an in-process `ICaseQueue` after the ingest commit and run by a background worker; a daily limit and an on/off switch guard the budget. `Llm:Provider` chooses Azure OpenAI or a free scripted Fake. Every string sent to the LLM is masked with per-Investigation Pseudonyms (value replacement, so identifiers inside Rule Hit explanations are caught too), and agents cite short Evidence Refs (`E-n`, `G-n`) that code validates before the Reviewer runs. Enrich is thin; the Investigator must call three tools bound to the Case's Account (timeline, graph neighbourhood, Holder baseline), so `GetRuleHits` and `GetKnownFraudMatches` from Section 9.2 are dropped. A rejected Finding gets one retry, then reaches the analyst with a Review Flag; an LLM failure reaches the analyst as InvestigationFailed. The approval pause is a MAF `RequestPort` with checkpoints in a Postgres table, and `POST /cases/{id}/decision` resumes it. Act records an Intervention and Audit Entries and enforces nothing; a ConfirmedFraud decision records a Confirmed Fraud with Source = Analyst, so G2 lifts its Neighbours. One trace runs from ingestion to the Reviewer; the decision run is linked to it. See GLOSSARY.md and ADR-0004 to ADR-0009.

**Done when**
- [ ] Margaret's case produces a masked, evidence-cited summary and waits in `AwaitingApproval`.
- [ ] Approving it runs the Act step; the audit trail shows model, prompt version, evidence IDs, analyst.
- [ ] Restarting the API mid-approval does not lose the case.
- [ ] Aspire dashboard shows one trace spanning rules → graph → Investigator (with tool calls) → Reviewer.

**Explain it:** What is a MAF workflow vs an agent? How does tool calling work? Why two agents? How does human-in-the-loop pause and resume? What is GraphRAG here?

### Stage 4 — Training agents, evaluation, feedback (Day 4)

**Build**
- Scenario Generator agent → schema-validated scenarios → replay.
- Evaluation harness with all metrics in 10.3, including evidence grounding and LLM-as-judge on a sample.
- Analyst decision capture → rule-tuning report.
- Docs: `architecture.md`, ADR-001…013, `runbook.md`, `industrialisation.md`.

> **Stage 4 decisions (grill session, 2026-10-24):** Evaluation runs the agents in-process through the same `InvestigationLoop`, on a seeded Agent Sample of 20 Flagged Accounts plus up to a quarter Near-misses, with Fake as the free default and `--llm AzureOpenAI` for a real run; the LLM wiring moves from the API into `Vigil.Agents` so both hosts share it. Evidence Grounding is measured on first-attempt Findings, beside Reviewer rejection and Review Flag rates, Verdict Agreement with the Label and cost/latency per Investigation; an evaluation-only Judge (`judge.v1`) scores reasoning on four 1–5 criteria. The Scenario Generator is a red team: it sees the rules, writes labelled Generated Scenarios to `datasets/generated/` from its own command (committed), and evaluation lists every fraud one that never reaches High as a Blind Spot. A genuine Travelling holder Persona (Section 10.1) reaches High, so Analyst Decisions include false positives. A Stand-in Analyst simulator command decides Cases from their Labels through the API, and a `tune` command builds the Rule-Tuning Report from those decisions in Postgres, suggesting halved weights for Rules below 50% precision, each with a What-if over the Dataset; it never writes a rules file. See GLOSSARY.md, ADR-0012 and ADR-0016.

**Done when**
- [ ] One command produces an evaluation report with all metrics, rules version and prompt version.
- [ ] Generated scenarios expose at least one rule blind spot, documented in the report.
- [ ] Rule-tuning report suggests at least one weight change based on decisions.
- [ ] Total LLM spend to date is under budget (check Azure cost view).

**Explain it:** How do you measure an agent's reasoning quality? Why does the human approve rule changes? How would you move from rules to probabilistic detection?

### Stage 5 — Rehearsal (Day 5)

- Walk the Margaret demo end to end from a clean start.
- Rehearse Section 19 talking points out loud.
- Review each `[HUMAN]` module until it can be explained without notes.

### Parked (after the interview, if useful)

Anomaly score, Stardog/SPARQL + formal ontology, Application Insights, Azure Container Apps deployment, Angular analyst UI, Event Hubs ingestion, Entra ID, Key Vault. See Section 17.

---

## 15. Documentation plan

| Document | Contents | When |
|---|---|---|
| `PROJECT.md` | This file | Now |
| `CLAUDE.md` | Coding conventions, commands, `[HUMAN]` rules, stage gates | Stage 0 |
| `README.md` | What it is, quick start, demo script | Stage 0, updated each stage |
| `docs/architecture.md` | Component diagram, data flow, sequence diagram of Margaret's case | Stage 1, extended each stage |
| `docs/adr/ADR-0NN-*.md` | One per decision in 4.3: context, decision, alternatives, consequences | As decisions are made |
| `docs/runbook.md` | How to run, reset data, run evals, common failures | Stage 4 |
| `docs/industrialisation.md` | MVP → target state: Service Bus/Event Hubs, App Insights, Entra ID, Key Vault, Container Apps, anomaly models, Stardog | Stage 4 |
| `docs/interview-notes.md` | Talking points, likely questions, honest gaps | Stage 5 |
| `eval-reports/*.md` | Generated per evaluation run | Stage 1+ |

ADR template:

```markdown
# ADR-0NN: <title>
Status: Accepted | Date: YYYY-MM-DD
## Context
## Decision
## Alternatives considered
## Consequences (positive / negative)
## How it changes at industrialisation
```

---

## 16. Job-spec coverage map

✅ covered · 🟡 partial · ❌ not covered · 💬 from experience

| Area | Requirement | Status |
|---|---|---|
| Fraud agents | MAF agents in C#: multi-agent workflows, tool calling, HITL | ✅ (deploy = local) |
| | Event-sequence detection across recovery/MFA/contact/bank/sale | ✅ |
| | Deterministic rules, time-window correlation, risk banding → prioritisation | ✅ |
| | Transition to probabilistic GenAI (anomaly reasoning, summaries, investigation) | 🟡 summaries + investigation; no statistical model |
| | Explainability on every alert | ✅ |
| Training agents | Generate/curate labelled scenarios and eval datasets | ✅ |
| | Feedback loops tuning rules, prompts, models | 🟡 rules + prompt versions; no model tuning |
| | Eval harness: detection rate, FPR, reasoning quality | ✅ |
| Knowledge graphs | Neo4j/Stardog graph of holders, accounts, devices, IPs, emails, phones, bank accounts, transactions | ✅ Neo4j |
| | Ontologies and queries (Cypher, SPARQL) for shared identifiers, fraud matches, rings | 🟡 Cypher only |
| | GraphRAG and graph features in agent reasoning | ✅ |
| Engineering | Work with SA/BA | 💬 (config-driven rules support it) |
| | Data pipelines from source systems | 🟡 simulated sources, canonical schema |
| | Version control, CI/CD, testing, observability, cost/latency | 🟡 CI yes, CD parked |
| | Security, privacy, model risk, audit | 🟡 masking, audit, HITL, versioning |
| | Documentation and handover | ✅ |
| Skills | 1+ year MAF | ❌ — stated openly |
| | Advanced C#/.NET | ✅ |
| | Azure OpenAI / Foundry / Azure services | 🟡 |
| | Event/time-series sequencing and windowing | ✅ |
| | Evaluation of LLM/agent outputs | ✅ |

---

## 17. Out of scope (parked) and the industrialisation path

| Parked item | Why parked | Industrialisation answer |
|---|---|---|
| Statistical anomaly score | Time | Per-holder baselines (country/device/frequency) → anomaly score blended with rules; later ML models |
| Stardog / SPARQL / OWL ontology | No free self-hosted licence; time | Same model expressed as RDF/OWL; Stardog for reasoning and virtual graphs over source systems |
| Event Hubs / Service Bus | Cost, complexity | Replace `ICaseQueue` and ingestion with Event Hubs; CDC from source systems |
| Application Insights / Azure Monitor alerts | Optional | Same OpenTelemetry data, exporter config change |
| Azure deployment (Container Apps, Azure SQL/Postgres, Neo4j Aura) | Time, cost | Bicep/azd, managed identity, private networking |
| Entra ID, Key Vault | Time | Role-based analyst/supervisor access; secrets in Key Vault |
| Analyst UI | Time | Angular case queue with graph visualisation |
| Model fine-tuning from feedback | Not needed for MVP | Use labelled decisions for prompt optimisation and later supervised models |

---

## 18. Working agreement for Claude Code


1. **`[HUMAN]` modules** (`Vigil.Detection`, `Vigil.Graph`, `Vigil.Agents`): after implementing, walk the developer through the code (purpose of each class, the main flow, one tricky line) before moving on. Keep these modules small and readable — clarity over cleverness.
2. **Verify MAF APIs against current official docs** before writing agent/workflow code; the framework changes quickly. Note any API differences from this document in the relevant ADR.
3. **Tests first for detection logic**; no real LLM calls in tests.
4. **Every architectural choice gets an ADR** (short is fine).
5. **Budget guard:** never loop LLM calls over the full dataset; agent evals use a sample size from config. Log tokens per call.
6. Use plain, well-named code and comments explaining *why*, not *what*.

---

## 19. Interview talking points

**Opening (honest framing):** "Microsoft Agent Framework is new to me — I have 14 years of C#/.NET and recent agentic work in Python with LangGraph. To prepare, I built Vigil end to end in MAF this week, mirroring your MVP-to-industrialisation journey. I'd like to walk you through it and the decisions I made."

**Decisions to defend**

1. **Rules first, AI second** — explainable, auditable, cheap; AI only on risky cases.
2. **Graph vs SQL** — "accounts 2–3 hops from known fraud through any shared identifier" is one Cypher query vs many self-joins.
3. **Workflow vs one big agent** — predictable, testable steps; AI boxed into specific steps; human step cannot be skipped.
4. **Two agents** — Reviewer checks claims against evidence; evidence IDs validated in code.
5. **Human in the loop** — freezing an account is a regulated action; AI recommends, a human decides.
6. **PII masking** — data minimisation; the LLM needs patterns, not identities.
7. **No vector DB** — fraud evidence is relational; GraphRAG via Cypher tools.
8. **System never self-tunes** — model-risk governance; human-approved rule versions.
9. **Known limits** — slow-burn fraud evades windows → motivates probabilistic baselines.

**Likely questions to prepare**

- How would you scale ingestion to millions of events per day?
- How do you stop the LLM hallucinating evidence?
- How do you measure false positives, and what is an acceptable rate?
- What happens if Azure OpenAI is down or slow? (Rules still work; cases queue; retries; fallback provider.)
- How would you move from Neo4j to Stardog, or use both?
- How would you onboard a new fraud journey (e.g. dividend mandate fraud)?
- How do you satisfy audit and model-risk reviewers?

---

## 20. Glossary

| Term | Meaning |
|---|---|
| **ATO** | Account takeover — a fraudster gains control of a genuine holder's account |
| **Registration fraud** | Creating an account using a stolen or synthetic identity |
| **Sliding window** | Look-back period in which a sequence of events must occur to match a rule |
| **Risk band** | Low / Medium / High / Critical category derived from the score |
| **Reason code** | Recorded explanation of which rule fired and why |
| **Knowledge graph** | Data stored as nodes and relationships, so connections are first-class |
| **Cypher** | Neo4j's query language |
| **GraphRAG** | Retrieving facts from a graph and giving them to an LLM as context |
| **MAF** | Microsoft Agent Framework — .NET/Python framework for agents and workflows (successor to Semantic Kernel and AutoGen) |
| **Workflow (MAF)** | A graph of steps (executors); some are code, some are agents |
| **Tool / function calling** | The LLM chooses to call a C# method and receives its result |
| **HITL** | Human-in-the-loop — workflow pauses for a human decision |
| **Checkpointing** | Saving workflow state so it can resume after a pause or restart |
| **Aspire** | .NET tool that runs and wires a multi-part app locally, with a telemetry dashboard (separate from MAF) |
| **OpenTelemetry** | Standard format for traces, metrics and logs |
| **LLM-as-judge** | Using an LLM with a rubric to score another LLM's output |

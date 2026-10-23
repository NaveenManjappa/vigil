# Vigil

Detects account takeover and registration fraud on investor portals from the sequence of actions taken on each account.

## Language

**Holder**:
The person who owns shares and signs in to the investor portal. In the MVP each Holder has exactly one Account.
_Avoid_: user, customer, shareholder

**Account**:
One holding of shares belonging to a Holder; the unit that is scored for fraud risk.
_Avoid_: profile, login, portfolio

**Portal Event**:
One recorded action taken on an Account at a moment in time, such as a password reset or a bank-detail change, in the canonical shape every source system is normalised into.
_Avoid_: activity, log entry, transaction

**Source System**:
The portal component that emits a family of Portal Events, such as authentication, MFA or payment instructions.
_Avoid_: feed, upstream

**Scenario**:
One named, hand-scripted Account journey with fixed timestamps, such as Margaret's takeover or Mr Patel's house move, stored as a JSON file in `scenarios/`. The simulator posts it to the API to demonstrate detection end to end, and evaluation can dry-run it against any rule set.
_Avoid_: test case, fixture, persona (a Persona is a template that generates many randomised Accounts; a Scenario is one fixed script)

**Persona**:
A template of Holder behaviour from PROJECT.md Section 10.1, such as quiet holder or ATO fraudster, from which the simulator generates many Accounts with randomised timings and identifiers.
_Avoid_: profile, archetype, scenario

**Baseline**:
An Account's history of ordinary logins from the Holder's usual device and country, before its Persona's or Scenario's journey begins, so a First Sighting has something to compare against.
_Avoid_: warm-up, history (alone), normal profile

**Label**:
The ground truth for one generated Account: its Persona, plus its fraud kind (ATO, registration, ring or evasion) or genuine. Only evaluation reads it; it never goes to the API.
_Avoid_: tag, class, verdict

**Dataset**:
A seeded, reproducible set of generated Accounts, each with its Portal Events and Label, stored as JSON lines. The same seed and holder count always give a byte-identical Dataset.
_Avoid_: corpus, test data, fixture

**Rule**:
A named pattern that suggests fraud, worth a fixed number of points when it matches. It is one of three kinds. A sequence Rule is two steps on one Account, A then B, inside a Window; each step may list alternative event types. A first-sighting Rule (R0) matches a First Sighting and has no steps or Window. A graph Rule (a Graph Rule, such as G1) matches links between Accounts in the Projection and has no steps or Window.
_Avoid_: check, detector, signal

**First Sighting**:
A Portal Event whose device or country has not appeared on any earlier event of its Account, which Rule R0 scores. An Account's first event is never one, because there is nothing yet to compare against, and a new device with a new country on the same event is one First Sighting, not two.
_Avoid_: new device (alone), anomaly, unknown device

**Window**:
The longest time a Rule allows between its two steps, measured on OccurredAt.
_Avoid_: timeout, period (the risk period is a different thing)

**Rule Hit**:
One Rule matching on an Account, recorded with its evidence EventIds, the time of the event that completed it (its trigger time) and a plain-English explanation. Never rewritten once stored.
_Avoid_: match, finding, reason code

**Score**:
The sum of the points of the distinct Rules hit in an Alert. A Rule that hits more than once in a Risk Episode still counts once. It never decreases, so a band can only rise.
_Avoid_: risk score (in code), rating

**Risk Episode**:
A run of Rule Hits on one Account where each hit's trigger time is at most the risk period (`riskPeriodDays`, 7) after the previous hit's, measured in event time and never by wall clock. A longer quiet gap ends the episode, and the next hit opens a new one. Each Risk Episode is one Alert.
_Avoid_: incident, session, risk period (the risk period is the gap that ends an episode, not the episode itself)

**Risk Band**:
The label a Score falls into: Low, Medium, High or Critical, by thresholds set in the rules file.
_Avoid_: severity, level, priority

**Alert**:
Detection's verdict on one Risk Episode of an Account: its Rule Hits, their Score, its Risk Band and the Rules Version that opened it. Once stored it only grows: hits are appended, never rewritten.
_Avoid_: flag, incident, case (a Case is the work item an analyst picks up)

**Flagged**:
Said of an Account in evaluation when any of its Alerts reached High or above at any point. Detection rate and false-positive rate count flagged Accounts.
_Avoid_: detected, caught, alerted (an Account can have an Alert without being flagged)

**Case**:
The work item an analyst picks up for one Alert, opened with status Open the first time that Alert reaches High. It moves Open → Investigating → AwaitingApproval → Closed: one Investigation runs on it, it waits for an Analyst Decision, and the decision closes it. An Alert has at most one Case and a Case has at most one Investigation: reaching Critical later raises the Case's priority but opens no second Case and starts no second Investigation. The queue orders Cases by Risk Band, then Score, then time opened. In the Projection, a Case node with outcome ConfirmedFraud marks a Confirmed Fraud; seeded ones have no Alert behind them.
_Avoid_: ticket, investigation, alert (the Alert is detection's verdict; the Case is the work done on it)

**Rules Version**:
The version string of the rules file in force when an Alert was scored, stored on the Alert so the reasoning behind any past Alert can be reproduced.
_Avoid_: config version, ruleset id

**Projection**:
The copy of Portal Events in Neo4j as nodes and relationships: Holder OWNS Account, and Account linked to each identifier it used (USED_DEVICE Device, SEEN_FROM IpAddress, HAS_EMAIL Email, HAS_PHONE Phone, PAYS_TO BankAccount, LIVES_AT Address), plus a Case CONCERNS Account for each Confirmed Fraud. It is written inside ingestion with idempotent MERGEs. Postgres stays the source of truth, so the Projection can be wiped and rebuilt from the event store and the Confirmed Fraud records (ADR-0015).
_Avoid_: graph database (as the store of record), sync, mirror

**Linking Identifier**:
An identifier whose reuse across Accounts suggests one person or gang behind them: Device, BankAccount, Email and Phone. IP addresses and postal Addresses are projected for exploration but never link, because households, offices and mobile carriers share them innocently.
_Avoid_: shared attribute, key, identifier (alone, when IP and Address are excluded)

**Neighbour**:
An Account outside an ingested batch that shares a Linking Identifier with one of the batch's events, or an Account that shares one with a newly recorded Confirmed Fraud Account. A new link or a new Confirmed Fraud can change its Graph Rule Hits, so ingestion re-checks it in the same request (ADR-0015).
_Avoid_: related account, connected account (a Ring is a different, larger thing)

**Neighbourhood**:
What the Projection knows about one Account's links, one hop out: each of its Linking Identifiers, the other Accounts on each, and whether each of those is Confirmed Fraud. The Investigator reads it through its graph tool (ADR-0007).
_Avoid_: subgraph, network, Neighbour (a Neighbour is an Account ingestion re-checks)

**Graph Rule**:
A Rule that matches links between Accounts in the Projection rather than events on one Account, written as one short, one-hop Cypher query: G1 Shared bank account, where an Account pays out to a BankAccount that another Holder's Account also pays out to; G2 Known-fraud proximity, where it shares a Linking Identifier with a Confirmed Fraud Account; and G3 Ring indicator, where the Account is in a Ring. It reads the graph as it is now and returns candidate Rule Hits that the Detector places in Risk Episodes like any other. Its trigger time is the OccurredAt of the event that completed the link, and its evidence is the linking events on every Account involved, with that event last (ADR-0014).
_Avoid_: graph feature, graph check, network rule

**Confirmed Fraud**:
An Account known to be fraud, recorded with the time it was decided and its source: Seed, from a Scenario's `confirmedFraud` date, or Analyst, from an Analyst Decision with outcome ConfirmedFraud. An Account is Confirmed Fraud at most once, so recording it again keeps the first decision. It is projected as `(:Case {outcome: 'ConfirmedFraud'})-[:CONCERNS]->(:Account)`, which G2 looks for. The generated Dataset has none, so Labels never leak into detection.
_Avoid_: known fraudster, blacklisted, fraud label (a Label is evaluation's ground truth)

**Ring**:
A group of at least the rules file's `accountThreshold` (3) Accounts that share one Linking Identifier, such as one device or one bank account; G3 scores each member. Only Accounts on the same identifier count, not a group joined through a chain of different identifiers (ADR-0014). An Account's Holder does not matter, so a Holder's own Accounts can form one. A G3 hit's evidence is the earliest links that brought the group, with this Account in it, to the threshold.
_Avoid_: gang, cluster, network, connected component (a component would follow links through any number of hops)

**Investigation**:
The AI work done on one Case before a human sees it: the Investigator agent gathers evidence about the Case's Account and writes a Finding, and the Reviewer agent checks it. It only recommends; nothing changes until an Analyst Decision. When Investigations are switched off or the daily limit is reached, the Case waits Open, marked not investigated.
_Avoid_: case (the Case is the work item), analysis, agent run

**Finding**:
The Investigator's written conclusion on a Case: a verdict (LikelyAccountTakeover, LikelyRegistrationFraud, Inconclusive or LikelyGenuine), a confidence, a short summary, a recommended action and the Evidence Refs it cites. An Investigation whose agents failed has no Finding and is flagged InvestigationFailed.
_Avoid_: summary (alone), verdict (alone), report, result

**Evidence Ref**:
A short label an Investigation gives one piece of evidence so the agents can cite it: `E-n` for a Portal Event, `G-n` for a fact from the Projection, each standing for the EventIds behind it. Refs are numbered per Investigation, and a Finding citing a ref that does not exist is rejected before review.
_Avoid_: evidence ID, citation, reason code

**Pseudonym**:
The stand-in an Investigation uses for an identifier before any text reaches an LLM, such as `ACCOUNT-1` or `DEVICE-2`. Pseudonyms are numbered per Investigation, so the same device has different pseudonyms in different Cases; only the analyst-facing view turns them back into real identifiers.
_Avoid_: mask, token, alias, hash

**Review Flag**:
The mark on a Finding the Reviewer rejected even after the Investigator's one retry. The Finding still reaches the analyst, flagged, so it is read with extra scepticism.
_Avoid_: warning, low confidence (confidence is the Investigator's own rating)

**Analyst Decision**:
A named analyst's ruling on a Case: an outcome (ConfirmedFraud or FalsePositive), an action (BlockBankChanges, ContactHolder or NoAction), and notes. The action may differ from the Finding's recommendation. It closes the Case, and a ConfirmedFraud outcome makes the Account a Confirmed Fraud.
_Avoid_: approval (alone), verdict, label (a Label is evaluation's ground truth)

**Intervention**:
The instruction recorded when an Analyst Decision's action is carried out, such as blocking bank-detail changes until a date or contacting the Holder by post. Vigil records it for the portal to enforce; it does not stop later Portal Events itself.
_Avoid_: block (alone), action (the action is what the analyst chose; the Intervention is its record), freeze

**Prompt Version**:
The version of an agent's instructions in force for an Investigation, named by its prompt file, stored with the Case and every Audit Entry so the AI's reasoning can be reproduced, as the Rules Version does for detection.
_Avoid_: prompt ID, template version, model version (the model is recorded separately)

**Audit Entry**:
An append-only record of one step in a Case's life after detection: an Investigation completed, a review completed, a decision recorded or an action taken. It names who or what acted, the model and Prompt Version where AI was involved, the Rules Version, the evidence used and the tokens spent. It is never changed once written.
_Avoid_: log line, history, event (a Portal Event is something done on an Account)

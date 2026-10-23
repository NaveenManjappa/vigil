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
The work item an analyst picks up for one Alert, opened with status Open the first time that Alert reaches High. An Alert has at most one Case: reaching Critical later raises the Case's priority but opens no second one. The queue orders Cases by Risk Band, then Score, then time opened. In the Projection, a Case node with outcome ConfirmedFraud marks a Confirmed Fraud; seeded ones have no Alert behind them, and Stage 3's decided Cases will write the same node.
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

**Graph Rule**:
A Rule that matches links between Accounts in the Projection rather than events on one Account, written as one short, one-hop Cypher query: G1 Shared bank account, where an Account pays out to a BankAccount that another Holder's Account also pays out to; G2 Known-fraud proximity, where it shares a Linking Identifier with a Confirmed Fraud Account; and G3 Ring indicator, where the Account is in a Ring. It reads the graph as it is now and returns candidate Rule Hits that the Detector places in Risk Episodes like any other. Its trigger time is the OccurredAt of the event that completed the link, and its evidence is the linking events on every Account involved, with that event last (ADR-0014).
_Avoid_: graph feature, graph check, network rule

**Confirmed Fraud**:
An Account known to be fraud, recorded with the time it was decided and its source: Seed for now, from a Scenario's `confirmedFraud` date, and analyst decisions from Stage 3. An Account is Confirmed Fraud at most once, so recording it again keeps the first decision. It is projected as `(:Case {outcome: 'ConfirmedFraud'})-[:CONCERNS]->(:Account)`, which G2 looks for. The generated Dataset has none, so Labels never leak into detection.
_Avoid_: known fraudster, blacklisted, fraud label (a Label is evaluation's ground truth)

**Ring**:
A group of at least the rules file's `accountThreshold` (3) Accounts that share one Linking Identifier, such as one device or one bank account; G3 scores each member. Only Accounts on the same identifier count, not a group joined through a chain of different identifiers (ADR-0014). An Account's Holder does not matter, so a Holder's own Accounts can form one. A G3 hit's evidence is the earliest links that brought the group, with this Account in it, to the threshold.
_Avoid_: gang, cluster, network, connected component (a component would follow links through any number of hops)

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

**Baseline**:
An Account's ordinary activity before an Alert starts: its usual devices and countries. The Investigator reads it through `GetUsualBehaviour` to contrast a takeover with it.
_Avoid_: warm-up, normal profile

**Rule**:
A named pattern that suggests fraud, worth a fixed number of points when the new event matches it. It is one of three kinds. A sequence Rule is two steps on one Account, A then B, inside a Window, and the new event must be step B; each step may list alternative event types. A first-sighting Rule (R0) matches a First Sighting. A Graph Rule (G1, G2) matches links between Accounts in the graph.
_Avoid_: check, detector, signal

**First Sighting**:
A Portal Event whose device or country has not appeared on any earlier event of its Account, which Rule R0 scores. An Account's first event is never one, because there is nothing yet to compare against, and a new device with a new country on the same event is one First Sighting, not two.
_Avoid_: new device (alone), anomaly, unknown device

**Window**:
The longest time a Rule allows between its two steps, measured on OccurredAt.
_Avoid_: timeout, period (the risk period is a different thing)

**Rule Hit**:
One Rule matching a new event, recorded with its evidence EventIds, the time of the event that completed it (its trigger time) and a plain-English explanation.
_Avoid_: match, finding, reason code

**Score**:
The sum of the points of the distinct Rules hit in an Alert. A Rule that matches again still counts once, so the Score never decreases.
_Avoid_: risk score (in code), rating

**Risk Band**:
The label a Score falls into: Low, Medium, High or Critical, by thresholds set in the rules file.
_Avoid_: severity, level, priority

**Alert**:
Detection's running verdict on one Account: its Rule Hits, their Score, its Risk Band and the Rules Version that opened it. An Account has at most one open Alert. It closes when its Case closes, or when its last hit is more than the risk period (`riskPeriodDays`, 7) older than a new hit in event time; the next hit then starts a new Alert.
_Avoid_: flag, incident, case (a Case is the work item an analyst picks up)

**Case**:
The work item for one Alert, opened with status Open the first time that Alert reaches the rules file's `openCaseAt` band (High). It moves Open → AwaitingDecision → Closed: `POST /cases/{id}/investigate` runs the Investigation, and `POST /cases/{id}/decision` records the Analyst Decision. An Alert has at most one Case. The queue orders Cases by Risk Band, then Score, then time opened.
_Avoid_: ticket, investigation, alert (the Alert is detection's verdict; the Case is the work done on it)

**Rules Version**:
The version string of the rules file in force when an Alert was scored, stored on the Alert so the reasoning behind any past Alert can be reproduced.
_Avoid_: config version, ruleset id

**Projection**:
The copy of Portal Events in Neo4j as nodes and relationships: Holder OWNS Account, and Account linked to each identifier it used (USED_DEVICE Device, SEEN_FROM IpAddress, HAS_EMAIL Email, HAS_PHONE Phone, PAYS_TO BankAccount, LIVES_AT Address). Each event is MERGEd as it arrives. A Confirmed Fraud Account carries `confirmedFraud: true`.
_Avoid_: graph database (as the store of record), sync, mirror

**Linking Identifier**:
An identifier whose reuse across Accounts suggests one person or gang behind them: Device, BankAccount, Email and Phone. IP addresses and postal Addresses are projected for exploration but never link, because households, offices and mobile carriers share them innocently.
_Avoid_: shared attribute, key, identifier (alone, when IP and Address are excluded)

**Neighbourhood**:
What the graph knows about one Account's links, one hop out: each of its Linking Identifiers, the other Accounts on each, and whether each of those is Confirmed Fraud. The account view shows it and the Investigator reads it through `GetLinkedAccounts`.
_Avoid_: subgraph, network

**Graph Rule**:
A Rule that matches links between Accounts in the graph, written as one short Cypher query about the identifiers on the new event: G1 Shared bank account, where the new bank account is also paid out to by another Holder's Account; and G2 Known-fraud proximity, where a Linking Identifier on the event is also used by a Confirmed Fraud Account. Its evidence is the new event (ADR-0014, ADR-0017).
_Avoid_: graph feature, graph check, network rule

**Confirmed Fraud**:
An Account known to be fraud: marked by `POST /accounts/{id}/confirm-fraud`, or by an Analyst Decision with outcome ConfirmedFraud. It is the `confirmedFraud: true` property on the Account node, which G2 looks for.
_Avoid_: known fraudster, blacklisted

**Investigation**:
The AI work done on one Case when you call `POST /cases/{id}/investigate`: the Investigator agent reads the Alert, calls its tools and writes a Finding. It only recommends; nothing changes until an Analyst Decision. If it fails, the Case stays Open and can be investigated again.
_Avoid_: case (the Case is the work item), analysis, agent run

**Finding**:
The Investigator's written conclusion on a Case: a verdict (LikelyAccountTakeover, LikelyRegistrationFraud, Inconclusive or LikelyGenuine), a confidence, a short summary, a recommended action and the Event Numbers it relies on.
_Avoid_: summary (alone), verdict (alone), report, result

**Event Number**:
An event's position in its Account's timeline, ordered by OccurredAt and starting at 1, written `#3`. The account view, the event response and the Investigator's brief and tools all use it, and a Finding cites events by it.
_Avoid_: event index, evidence ref, sequence number

**Analyst Decision**:
A named analyst's ruling on a Case awaiting decision: an outcome (ConfirmedFraud or FalsePositive), an action (BlockBankChanges, ContactHolder or NoAction), and notes. The action may differ from the Finding's recommendation. It closes the Case and its Alert, and a ConfirmedFraud outcome makes the Account a Confirmed Fraud.
_Avoid_: approval (alone), verdict

**Intervention**:
The instruction recorded when an Analyst Decision's action is carried out, such as blocking bank-detail changes until a date or contacting the Holder by post. Vigil records it for the portal to enforce; it does not stop later Portal Events itself.
_Avoid_: block (alone), action (the action is what the analyst chose; the Intervention is its record), freeze

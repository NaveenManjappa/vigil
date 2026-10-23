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
A named pattern of Portal Events on one Account that suggests fraud, worth a fixed number of points when it matches. A sequence Rule is two steps, A then B, inside a Window; each step may list alternative event types. A first-sighting Rule (R0) matches a First Sighting and has no steps or Window.
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
The work item an analyst picks up for one Alert, opened with status Open the first time that Alert reaches High. An Alert has at most one Case: reaching Critical later raises the Case's priority but opens no second one. The queue orders Cases by Risk Band, then Score, then time opened.
_Avoid_: ticket, investigation, alert (the Alert is detection's verdict; the Case is the work done on it)

**Rules Version**:
The version string of the rules file in force when an Alert was scored, stored on the Alert so the reasoning behind any past Alert can be reproduced.
_Avoid_: config version, ruleset id

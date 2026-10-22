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
A scripted Account journey with a known label (fraud or genuine), replayed to exercise detection.
_Avoid_: test case, fixture, persona (a persona is a template that generates many scenarios)

**Rule**:
A named pattern of Portal Events on one Account that suggests fraud, worth a fixed number of points when it matches. A sequence Rule is two steps, A then B, inside a Window; each step may list alternative event types.
_Avoid_: check, detector, signal

**Window**:
The longest time a Rule allows between its two steps, measured on OccurredAt.
_Avoid_: timeout, period (the risk period is a different thing)

**Rule Hit**:
One Rule matching on an Account, recorded with its evidence EventIds, the time of the event that completed it (its trigger time) and a plain-English explanation. Never rewritten once stored.
_Avoid_: match, finding, reason code

**Score**:
The sum of the points of the distinct Rules hit in an Alert. A Rule that hits more than once still counts once.
_Avoid_: risk score (in code), rating

**Risk Band**:
The label a Score falls into: Low, Medium, High or Critical, by thresholds set in the rules file.
_Avoid_: severity, level, priority

**Alert**:
Detection's verdict on an Account: its Rule Hits, their Score, its Risk Band and the Rules Version that produced them.
_Avoid_: flag, incident, case (a Case is the work item an analyst picks up)

**Rules Version**:
The version string of the rules file in force when an Alert was scored, stored on the Alert so the reasoning behind any past Alert can be reproduced.
_Avoid_: config version, ruleset id

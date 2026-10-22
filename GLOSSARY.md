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

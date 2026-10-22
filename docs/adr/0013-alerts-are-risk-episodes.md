# Alerts are Risk Episodes with a never-decreasing Score

An Account gets one Alert per Risk Episode. A Rule Hit joins the episode of the previous hit if its trigger time is at most `riskPeriodDays` (7) after that hit's, measured on OccurredAt, never wall clock, so replaying 90 simulated days in seconds gives the same Alerts as live ingestion. A longer gap opens a new Alert. Only the first hit per Rule per episode counts, and the Score is the sum of the distinct Rules hit, so it never decreases and an Account cannot silently drop out of High.

Stored Alerts are only ever extended. `Detector.Detect` takes the Account's stored Alerts as a third argument and returns them first, in order and with their hits unchanged, followed by any new Alerts; the alert store appends whatever lies past the stored hits. A late event that would have been an earlier hit for a Rule already hit is ignored, so the evidence behind a past Alert never changes. A late hit that lands just before a stored episode (within the risk period, with no stored hit before it) joins that episode rather than opening a one-day-earlier Alert. A repeat hit for a Rule already in the episode is dropped, not just left unscored, so it never counts as the previous hit: a Rule that keeps firing cannot hold an episode open on its own. Each stored Alert keeps the Rules Version that opened it.

The merge lives in Detection rather than the alert store, as the Stage 1 plan had it, because deciding which episode a hit belongs to is detection logic. Keeping it pure means it is tested at the Detection seam without a database. Evaluation passes no stored Alerts, so for it Detect stays a pure function of timeline and rule set, and any arrival order gives identical Alerts. Through ingestion, arrival order can matter, but only in one direction: late evidence is dropped, never substituted.

## Considered Options

- **Immutable per-change Alerts** (a new Alert each time the Score changes): a full history, but the queue fills with near-duplicates of one Account, and a Case must chase whichever Alert is newest.
- **Decaying 7-day score** (a sum of the hits in the last 7 days): simple, but a slow takeover can fall from High back to Low between steps, and the band an analyst saw yesterday cannot be reproduced today.

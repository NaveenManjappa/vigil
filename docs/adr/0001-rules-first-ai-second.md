# Rules first, AI second

Every Portal Event is scored by deterministic Rules; AI agents only investigate Accounts the Rules have already made risky. Rules are explainable (each Rule Hit names the events behind it), auditable, fast enough to run inside the ingest request, and free, so they can watch every Account. An LLM on every event would be slow, costly and impossible to explain to an auditor.

The Detection module is a pure function of an Account's timeline and a rule set, with no database and no clock, so the API and the offline evaluation run exactly the same code.

## Considered Options

- **LLM scores every event**: flexible, but non-deterministic, expensive at volume and hard to defend in a regulated setting.
- **Anomaly model first**: needs labelled history we do not have; parked for industrialisation (PROJECT.md Section 17).

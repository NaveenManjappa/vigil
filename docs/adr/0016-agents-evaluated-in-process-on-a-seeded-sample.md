# Agents are evaluated in-process, on a seeded sample, by code first and a Judge second

Evaluation runs the same `InvestigationLoop` the API runs, inside the evaluation command, with an `IInvestigationSource` built from the in-memory Dataset and the throwaway Neo4j the graph replay already starts. No API, Postgres or Aspire is needed, so one command produces the whole report. The LLM wiring (provider switch, Fake, token logging) moves from the API into `Vigil.Agents` so both hosts register it the same way, and `--llm` chooses the provider for a run. Fake is the default, so the command is free; its agent numbers are marked as not meaningful.

Only an Agent Sample is investigated: `AgentSampleSize` (20) Flagged Accounts drawn with a fixed seed across Personas, plus up to a quarter as many Near-misses, never more than a quarter of the final sample, so a run with few Flagged Accounts is not mostly Near-misses. Production never investigates a genuine Account below High, but without Near-misses the sample would hold almost no genuine Accounts, and nobody would learn whether the agents can say LikelyGenuine.

Each Finding is measured by code before any LLM judges it. Evidence Grounding is taken on the first attempt, before the check that rejects unknown Evidence Refs (ADR-0006), because after that check it is always 100%. The report also gives the Reviewer's rejection rate, the Review Flag rate, Verdict Agreement with the Label, and tokens, pounds and seconds per Investigation. Only reasoning quality needs an LLM: an evaluation-only Judge (`judge.v1`) scores each Finding 1 to 5 on four criteria against the same masked evidence, with a reason per score. It lives in `Vigil.Evaluation`, not `Vigil.Agents`, because it never runs on a live Case. It uses the same deployment as the Investigator, so it may favour its own model's style; the report says so.

The report header names the Rules Versions, the Prompt Versions (`investigator.v1+reviewer.v1`, `judge.v1`), the provider and the model, so two runs can be compared knowing what changed.

## Considered Options

- **Evaluate through the running API** (post the sample, poll Cases): it tests the real pipeline, but needs the whole stack up and turns one command into a choreography.
- **Grounding on final Findings**: matches PROJECT.md's wording, but the code check makes it trivially 100% and hides how often the Investigator invents refs.
- **A different, stronger model as Judge**: less self-preference, but a second deployment and higher cost on a £5–10 budget.
- **Human rating of every Finding**: the gold standard, kept for a handful of spot checks; it does not scale to every run.

## At industrialisation

The Agent Sample becomes a fixed, versioned golden set with human-rated Findings, the Judge is calibrated against those ratings, and a run whose grounding or agreement drops below a threshold fails the prompt change in CI.

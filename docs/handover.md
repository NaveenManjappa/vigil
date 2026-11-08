# Handover

For the engineer taking over Vigil. It says what state the code is in, what it cannot do, and how to read the three core modules without the original author.

| Document | Read it for |
|---|---|
| [user-guide.md](user-guide.md) | How to run Vigil and walk one fraud case through it |
| [architecture.md](architecture.md) | How it works, on one page |
| [runbook.md](runbook.md) | How to reset it and fix failures |
| [industrialisation.md](industrialisation.md) | What to add back for real data |
| [adr/](adr/) | Why each decision was made |

## Status

Stages 0 to 5 built a full system, tagged `v1-full`. Stage 6 cut it down to a demo you can follow one event at a time (ADR-0017): the same domain, one agent, no evaluation. Every walkthrough step in the user guide is pinned by `WalkthroughTests`.

## Known limits

- **Single user.** No locking between requests, and the Neo4j write is outside the Postgres save. Two concurrent events for one Account can race.
- **Graph Rules look at the new event only.** An Account linked to a fraud Account *after* its events arrived is not re-scored.
- **Late events never complete a pattern after the fact.** An event is scored against what happened before it.
- **No masking.** Real identifiers go to the model. Do not use real data until masking is back ([industrialisation.md](industrialisation.md)).
- **The agent's Finding is not checked.** It may cite an event number that does not exist; the analyst reads it with the timeline beside it.
- **Slow-burn fraud is missed.** Steps spaced just beyond each Window evade every sequence Rule.

## Reading the core modules

Read them in this order. Each is small enough to read in one sitting.

1. **Detection.** Start at [Detector.cs](../src/Vigil.Detection/Detector.cs): one function, "which Rules did this event trigger?". Then [SequenceMatcher.cs](../src/Vigil.Detection/SequenceMatcher.cs) and [FirstSighting.cs](../src/Vigil.Detection/FirstSighting.cs), one Rule kind each. Then [Alert.cs](../src/Vigil.Domain/Detection/Alert.cs), where a Rule counts once. [DetectorTests](../tests/Vigil.Detection.Tests/DetectorTests.cs) shows every Rule hitting.
2. **Graph.** [GraphProjector.cs](../src/Vigil.Graph/GraphProjector.cs) writes each event as MERGEs; [GraphRules.cs](../src/Vigil.Graph/GraphRules.cs) is two queries, G1 and G2; [GraphNeighbourhood.cs](../src/Vigil.Graph/GraphNeighbourhood.cs) reads who shares what. [graph-primer.md](graph-primer.md) explains the Cypher.
3. **Agents.** [Investigator.cs](../src/Vigil.Agents/Investigation/Investigator.cs) is three numbered steps; [InvestigatorTools.cs](../src/Vigil.Agents/Investigation/InvestigatorTools.cs) is three methods the model may call. [InvestigatorTests](../tests/Vigil.Agents.Tests/InvestigatorTests.cs) scripts the model, so you can see exactly what it is sent and what it gets back.

Then read [Ingestion.cs](../src/Vigil.Api/Events/Ingestion.cs), which ties detection and the graph together in four numbered steps.

# Graph Rules are one-hop Cypher queries fed into the pure Detector

A Graph Rule is one short Cypher query in `Vigil.Graph` that asks the Projection about one Account and returns candidate Rule Hits. G1 Shared bank account (+30) is the first: the Account PAYS_TO a BankAccount that an Account owned by a different Holder also PAYS_TO. The rules file still owns the Rule's points and explanation (ADR-0002); `kind: graph` tells the Detector the hit comes from outside, and `GraphRules` maps the Rule's Id to its query. Parsing rejects a graph rule with steps or a Window, and the API refuses to start if a graph rule has no query behind it. Graph Rules live in `rules.v2.json`, which is v1 unchanged plus the graph rules, so Alerts stamped v1 remain reproducible.

`Detector.Detect` takes the candidate graph hits as an optional fourth argument and merges them with its sequence and first-sighting candidates, in trigger-time order, before Risk Episode assembly. The Detector stays pure: Vigil.Graph does the I/O, and everything ADR-0013 says (append-only Alerts, one hit per Rule per episode, a never-decreasing Score, late hits) applies to graph hits unchanged. The query returns one candidate per linked Account; the Detector keeps the first per episode.

A graph hit's trigger time is the OccurredAt of the event that created the completing link, which is the later of the two links' `firstSeen`. Its evidence is the event behind each link, with the triggering one last, the convention every Rule Hit follows. So the projector keeps, beside each relationship's `firstSeen`, the EventId that set it (`firstEventId`).

Queries read the graph **as it is now**, with no filtering on `firstSeen`. Live ingestion projects each batch before scoring it, so "now" is the moment being scored; an Account scored before its link formed is lifted in the same request that forms it, by the neighbour re-check (ADR-0015). Evaluation, once it scores Graph Rules later in Stage 2, is to get the same effect by replaying events into the graph in time order, so it never sees a link from the future. The trigger time still comes from the links, never the wall clock, so the hit lands in the right Risk Episode.

## Considered Options

- **Time-travel Cypher** (only count links whose `firstSeen` is before the event being scored): exact for any replay order, but every query grows a time parameter and filters, which makes the queries harder to read aloud, and time-ordered replay already gives the same answer.
- **Graph Rules inside the Detector**, behind an interface it calls: one call site, but the Detector would then do I/O and its tests would need a database or a fake graph.
- **Multi-hop or graph-algorithm rules** (connected components, GDS): they catch looser rings, but the queries stop being one readable pattern. One hop covers the Ring Persona, which reuses one device and one bank account.

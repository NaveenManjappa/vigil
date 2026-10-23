# The graph is a disposable Projection, updated inside ingestion

Postgres is the source of truth; Neo4j holds a Projection of it. Inside the ingest request, after the batch is appended and before the Postgres commit, every event in the batch is MERGE'd into Neo4j: Holder OWNS Account, and Account linked to its Device, IpAddress, Email, Phone, BankAccount and Address. Email, Phone, BankAccount and Address come from the NewEmail, NewPhone, NewBankAccountHash and NewPostcode attributes. There are no Registration or Transaction nodes, because they link nothing.

Projecting in the request means that by the time the request returns, the graph already reflects the batch, so the graph Rules planned for Stage 2 can score in the same request. A Neo4j failure fails the request and rolls back Postgres; the client's retry is safe because ingestion is idempotent on EventId and MERGE is idempotent on node keys. The two stores are not one transaction: if Neo4j commits and the Postgres commit then fails, the graph briefly holds links for events that are not stored. We accept that, because the client retries a failed request and the graph is disposable, so a rebuild from Postgres removes anything stray. The whole batch is projected, including events already ingested, so a retry after a Postgres commit failure still reaches the graph, and so do events from before Stage 2 when they are posted again. Each batch is one Neo4j write transaction, and uniqueness constraints on every node key are created at API start so concurrent MERGEs cannot create duplicate nodes.

Relationships carry `firstSeen` only, kept as the earliest OccurredAt even when events arrive late. PROJECT.md Section 8.1 drew `from`/`to` and `lastSeen` periods; they are dropped because no Rule reads them, and an old email simply stays linked, which is what "email previously used by a fraudster" needs.

## Considered Options

- **Project after commit, or from a background worker**: ingestion stays fast and Neo4j outages don't block it, but the graph lags the event store, and an Alert returned by the request could not include graph evidence.
- **Neo4j as the store of record**: one store, but it gives up the event store's append-only, idempotent guarantees (ADR-0010), and the graph could no longer be wiped and rebuilt.

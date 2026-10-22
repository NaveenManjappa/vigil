# PostgreSQL event store: one table, jsonb attributes, idempotent on EventId

Portal Events are stored append-only in PostgreSQL via EF Core: native on Apple silicon, free, and a provider swap away from Azure SQL or Azure Database for PostgreSQL.

All event types share a single `events` table. The fields every rule reads (account, type, time, device, IP, country, channel) are typed columns; type-specific details live in an `Attributes` jsonb column, so onboarding a new event type needs no migration. The composite index `(AccountId, OccurredAt)` serves the rules engine's per-account look-back.

`EventId` is the primary key and ingestion is idempotent on it: a re-sent event is reported as already ingested, not stored twice, so sources and the simulator can retry safely. Detection orders by the source's `OccurredAt`; our own `ReceivedAt` is stored alongside to measure lateness without trusting arrival order.

## Considered Options

- **Table per event type**: stronger typing, but every new type is a migration and timeline queries become unions.

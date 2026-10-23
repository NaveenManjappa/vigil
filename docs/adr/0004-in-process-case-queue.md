# Investigations run from an in-process queue behind ICaseQueue

When ingestion opens a Case, it puts the CaseId and the request's trace context on `ICaseQueue`, and an `InvestigationWorker` background service runs that Case's one Investigation. The queue is a bounded `System.Threading.Channels` channel in the API process (capacity 1000; a full queue makes ingestion wait rather than grow memory). The interface is the seam for a durable broker later, such as Azure Service Bus, without touching ingestion or the worker.

Cases are queued only after the ingest transaction commits, and only Cases that the transaction opened. A rolled-back batch therefore never starts an Investigation, and an Alert that later rises to Critical raises its Case's priority without queuing it again. Under the retrying execution strategy each attempt collects its own list of opened Cases, and only the committed attempt's list is queued.

Postgres, not the channel, is the durable record. The queue is lost on a crash, so on startup the worker reads every Case left Open or Investigating and runs those before it reads the queue. It reads them in `StartAsync`, which runs before the server accepts requests, so a Case opened after startup is never picked up twice. A Case the budget guard skipped (`NotInvestigated`) is not re-queued: it was skipped on purpose, and re-queuing it would spend the budget on a Dataset replay after a restart.

The budget guard sits in the worker. `Investigation:Enabled` switches Investigations off, and `Investigation:DailyLimit` (20) caps those started per UTC day. The worker counts the Cases with an `InvestigationStartedAt` in the current UTC day, so the count survives a restart. A Case over the limit, or arriving while Investigations are off, stays Open, marked not investigated, and the runner is never called. The worker takes one Case at a time, which keeps that count free of races. The design assumes a single API instance.

The worker calls `IInvestigationRunner`, which runs the Investigation workflow up to Await Approval (ADR-0005).

## Considered Options

- **Durable broker now (Service Bus, or a Postgres outbox table polled by the worker):** survives crashes without a startup scan and scales to several instances, but adds infrastructure or polling code that a single-instance demo does not need. The startup re-queue gives the same "nothing is lost" result here.
- **Run the Investigation inside the ingest request:** no queue at all, but an LLM call would hold the ingest transaction and the request open for seconds, and a slow or failed model would fail ingestion.
- **Enqueue inside the transaction:** simpler code, but a batch that later rolls back would already have queued a Case that does not exist.

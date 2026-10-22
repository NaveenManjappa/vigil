# OpenTelemetry with the Aspire dashboard for local observability

All services call `AddServiceDefaults()`, which wires OpenTelemetry traces, metrics and logs plus health endpoints. Locally these go to the Aspire dashboard, which needs no account and no cost. In production the same telemetry goes to Application Insights by changing the exporter, not the instrumentation.

Aspire is local orchestration only (it starts Postgres, Neo4j, the API and the simulator); it is unrelated to Microsoft Agent Framework.

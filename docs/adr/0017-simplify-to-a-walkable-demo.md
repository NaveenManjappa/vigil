# Simplify Vigil to a demo you can walk one event at a time

By Stage 5, Vigil had grown to about 6,500 lines in ten projects. One event passed through batch ingestion with advisory locks, a Neo4j projection with Neighbour re-scoring, Risk Episodes, an in-process queue and budget worker, a MAF workflow checkpointed in Postgres, an Investigator and a Reviewer with a validate, review and retry loop, and per-Investigation masking with Evidence Refs. Beside that sat an evaluation harness, a simulator and a 2,000-line user guide. No one could send an event and follow what happened to it. The goal of this stage is that you can send one event in Scalar, look at what changed, and then send the next one. The agent code must also be explainable to a beginner.

The decisions:

- **One event per call.** `POST /events` takes one event, not a batch. Its response says what that event did: the Rule Hits it added, the Rules that matched again but already counted, the Alert's Score and band, and the Case it opened. `GET /accounts/{id}` shows everything about one Account in one place. `POST /reset` empties both stores, in Development only.
- **Detection asks one question.** `Detector.Detect(newEvent, history, rules)` returns the Rules this event triggers, where the event is the second step of a sequence or a First Sighting. An Account has at most one open Alert. A Rule counts once in it. An Alert whose last hit is more than `riskPeriodDays` old closes, and a new one starts. Closing a Case closes its Alert. The Case band is `openCaseAt` in the rules file. This replaces Risk Episodes and the stored-Alert merge (ADR-0013).
- **The graph is kept and cut down.** Each event is MERGEd into Neo4j. G1 (shared bank account) and G2 (identifier shared with a Confirmed Fraud Account) are each one query about the identifiers on the new event, with that event as evidence. G3 (Ring), Neighbour re-scoring, link timestamps and `rebuild-graph` are gone. Confirmed Fraud is a `confirmedFraud` property on the Account node, set by `POST /accounts/{id}/confirm-fraud` or by a ConfirmedFraud decision. It is no longer a Case node.
- **One agent, called explicitly.** `POST /cases/{id}/investigate` runs the Investigator synchronously and stores its Finding on the Case. The Investigator is `chat.AsAIAgent(name, instructions, tools)` followed by `RunAsync<Finding>(brief)`, with three tools: `GetTimeline`, `GetLinkedAccounts` and `GetUsualBehaviour`. A failure returns 502 and leaves the Case Open to try again. There is no Reviewer, no retry loop, no code validation, no workflow, no checkpoints, no queue, no worker and no daily budget. Calling the endpoint is the budget, and tokens are still logged per call.
- **No masking, no Evidence Refs.** The data is synthetic. The agent sees real identifiers and cites events by number (`#3`), which is the event's position in the Account's timeline (OccurredAt order).
- **A Case is one row.** It moves Open → AwaitingDecision → Closed. The Finding, model, tokens, Analyst Decision and Intervention are columns on it. The separate decision, Intervention, Audit Entry and checkpoint tables are gone, and all the old migrations are squashed into one.
- **No evaluation.** The evaluation console, the simulator, Datasets, Personas, Scenarios, the Judge, the Scenario Generator, the Stand-in Analyst and the Rule-Tuning Report are deleted. The walkthrough is one API test, `WalkthroughTests`, and the user guide gives the same request bodies to paste into Scalar.
- **No locking.** Ingestion is one `SaveChanges` with no transaction around the Neo4j write and no advisory locks. This is a single-user demo.

The full version is tagged `v1-full`. [industrialisation.md](../industrialisation.md) lists what to add back, and in which order, for real data.

This supersedes ADR-0004, ADR-0005, ADR-0006, ADR-0008, ADR-0013, ADR-0015 and ADR-0016. It narrows ADR-0007 (the tools changed) and ADR-0014 (only G1 and G2 remain, each about the new event).

## Agent Framework API check

Checked against Microsoft.Agents.AI 1.26.0, the version in `Directory.Packages.props`, and the Microsoft Learn agent quick start. `IChatClient.AsAIAgent(instructions, name, description, tools, ...)` builds a `ChatClientAgent` with its tools. `AIAgent.RunAsync<T>(message, ...)` asks for structured output and parses it into `T`, running the tool loop on the way. Enums are written by name through `JsonStringEnumConverter<T>` attributes on the Finding's enums, so the JSON schema the model is given lists the names. The `Microsoft.Agents.AI.Workflows` package is no longer referenced.

## Considered Options

- **Keep everything and write a better guide:** the guide was already 2,000 lines. The difficulty was in the moving parts, not in how they were described.
- **Remove Neo4j and run the link rules as Postgres queries:** one store fewer, but the developer wanted to keep the graph, and Neo4j Browser is a good way to see it.
- **Run the agent automatically inside `POST /events`:** one call fewer, but the event that opens a Case would take 10 to 30 seconds with a real model, and the pause between "a Case opened" and "the agent investigated" would disappear.
- **Keep the Reviewer without the retry:** it shows two agents working together, at twice the code and the cost, for a step a beginner does not need in order to see how an agent works.

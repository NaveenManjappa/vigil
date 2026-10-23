# Two agents, Investigator and Reviewer, with code validation before review

An Investigation uses two MAF `ChatClientAgent`s. The Investigator (`vigil-investigator`, `investigator.v1`) calls its tools and writes a Finding (ADR-0007). The Reviewer (`vigil-reviewer`, `Reviewer`, `reviewer.v1`) has no tools and answers `{ approved, reasons[] }`. Both Ids and names are constants, because MAF checkpoints and telemetry identify agents by them. The Reviewer is shown the Finding and, for each ref it cites, the exact masked line the Investigator was shown for that ref, which `MaskedEvidence` keeps as it writes them. It judges only what was cited, so it can neither look for support the Finding never gave nor be swamped by the whole Case.

Before the Reviewer runs, code validates the Finding: every field inside the contract (the JSON converter accepts numbers for enums, and a missing property binds to null rather than failing to parse), a summary of at most 120 words, at least one cited ref, and every cited ref issued by this Investigation's Evidence Ref table. A failure counts as a rejection with reasons naming the problem, such as the unknown refs, and costs no Reviewer call. These checks are cheap and certain in code; a model asked to make them is neither.

`InvestigationLoop.RunAsync` runs Investigate, Validate and Review with one retry, and returns an `InvestigationOutcome` on every path, so a Case always reaches Await Approval:

- **Approved:** the Finding, unflagged.
- **Rejected, by validation or the Reviewer:** the Investigator runs once more with the reasons, masked, appended to its brief. A second rejection keeps the second Finding with the Review Flag and the last reasons, unless that Finding is incomplete (a missing summary or evidence list, or an enum outside the contract): there is nothing sound to show the analyst or cite in an Audit Entry, so it fails the Investigation instead.
- **Unparseable Investigator reply:** treated as a rejection on the first attempt ("reply with one JSON object"), because a formatting slip is often a one-off; on the retry it fails the Investigation.
- **LLM error or timeout, from either agent, or an unparseable Review:** no Finding and InvestigationFailed. An outage will not heal within one retry, so it is not retried; a Reviewer that cannot answer leaves the Finding unchecked, and an unchecked Finding is not shown as if it were reviewed. A timeout surfaces as `OperationCanceledException` with the caller's token untouched, so it fails like any error; only a cancelled caller token, the host stopping, escapes, and the startup re-queue (ADR-0004) runs the Case again.

The outcome carries each returned agent call's agent Id, Prompt Version and input/output tokens, failed runs included, for the Audit Entry. A call whose reply fails to parse is not counted there; the per-call token log (ADR-0009) still has it. The loop is plain C#; Stage 3.6 hosts it in the MAF workflow's executors (ADR-0005). The Fake provider recognises a Reviewer call by the `approved` property in the requested response schema, not by prompt wording, and approves.

## Considered Options

- **One agent that checks itself:** half the calls, but a model reviewing its own Finding in the same context tends to agree with it; a second agent with a separate prompt and only the cited evidence is a real check.
- **Reviewer validates refs and length too:** no code step, but it pays a model call to catch hallucinated refs that a dictionary lookup catches for free, and it can miss them.
- **More retries, or retrying LLM errors:** more Findings rescued, but each retry is another full tool loop of tokens on a £5–10 budget, and a provider outage fails every retry anyway.
- **Keep the Finding when the Reviewer fails:** saves the Investigator's work, but an unreviewed Finding would sit beside reviewed ones with nothing to tell them apart except a new flag; InvestigationFailed is the one failure state the analyst already knows.

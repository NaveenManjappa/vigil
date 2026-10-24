# Vigil

Account-takeover and registration-fraud detection for investor portals: rules first, AI agents second, a human decides. PROJECT.md is the spec and stage plan; GLOSSARY.md is the domain language (use its terms in code and docs); docs/adr/ records decisions.

## Working agreement

- **Review before commit.** Build each stage on its own branch (`stage-N-<name>`) and leave changes uncommitted. When the stage is done, summarise the diff and propose a commit message, then wait. Commit, push and open a PR only after the developer approves. The developer merges.
- **One stage at a time.** Start a stage only after the previous stage's "Done when" list is ticked and the developer confirms. Grill each stage first (grill-with-docs), recording terms in GLOSSARY.md and decisions in docs/adr/.
- **[HUMAN] modules** (`Vigil.Detection`, `Vigil.Graph`, `Vigil.Agents`) must be explainable without notes: keep them small and readable, and after implementing, walk the developer through each class, the main flow and one tricky line.
- **Tests first** for detection logic. Tests never call a real LLM; use a fake `IChatClient`.
- **Verify Microsoft Agent Framework APIs** against Microsoft Learn before writing agent code; note any difference from PROJECT.md in the relevant ADR.
- **Budget guard:** agent evaluation runs on a configured sample size, never the full dataset; log tokens per call.
- **ADRs** keep PROJECT.md's numbering (`0010-...` for ADR-010) and are written when their stage makes the decision real, in the short format already in docs/adr/.
- Write "Section 14", never the section symbol.
- Comments explain *why*, not *what*.

## Gotchas

- `global.json` opts `dotnet test` into Microsoft.Testing.Platform (required for xunit.v3 on the .NET 10 SDK).
- `Directory.Packages.props` pins transitive packages; EF Core versions must stay aligned there or the API fails with CS1705.
- Add migrations with `dotnet ef migrations add <Name> --project src/Vigil.Infrastructure --output-dir Migrations` (a design-time factory means no running database is needed).
- API tests use Testcontainers, so Docker must be running.
- Run the evaluation and simulator commands from the repo root: they read `config/`, `scenarios/`, `datasets/` and `src/Vigil.Api/appsettings.json` relative to it.
- A Dataset file (`datasets/seed-*-holders-*.jsonl`) is reused if it exists; delete it after changing a Persona, or the evaluation scores the old one.
- The Neo4j password is generated into the AppHost's user-secrets and baked into the `vigil-neo4j-data` volume on first start; if user-secrets are reset, delete that volume too.

## Agent skills

### Issue tracker

Issues live in GitHub Issues on NaveenManjappa/vigil, via the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Triage labels

Default vocabulary: `needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context: `GLOSSARY.md` and `docs/adr/` at the repo root. See `docs/agents/domain.md`.

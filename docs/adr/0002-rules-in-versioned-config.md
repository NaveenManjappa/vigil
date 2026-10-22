# Rules in versioned config, not code

Rules, their Windows and points, the Risk Band thresholds and the risk period live in `config/rules.v1.json`, not in C#. An analyst can tune a Window or a weight without a code change, and the file's `version` is stamped on every Alert so an auditor can see which rules produced it.

The API loads the file once at startup and fails fast if it is malformed (for example a sequence Rule without exactly two steps). `GET /rules` shows the rules and version that are live. The detection tests load the same shipped file, so a config change that breaks detection fails CI.

Changing a rule means a new file version; stored Rule Hits are never rescored under a newer version.

## Considered Options

- **Rules as C# classes**: type-safe, but every tweak is a deploy and analysts cannot read them.
- **Rules in the database**: editable at runtime, but needs an admin UI and its own audit trail; too much for the MVP.

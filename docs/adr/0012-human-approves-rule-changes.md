# A human approves every rule change; Vigil never tunes itself

Analyst Decisions feed back into detection only through a Rule-Tuning Report. The report gives each Rule's precision from decided Cases, suggests a lower weight for a Rule wrong more often than not (precision below 50%, at least 5 decisions, points halved to the nearest 5, a tie rounding up so 25 becomes 15 rather than 10, because a suggestion should be the gentler cut and a person can always cut further), and shows each suggestion's What-if: detection, false-positive and early-detection rates over the Dataset before and after. It writes nothing. A person who accepts a suggestion copies the rules file to a new version, edits it, re-runs evaluation and deploys it, and the new Rules Version then appears on every Alert it scores (ADR-0002).

The What-if is there because a weight that looks noisy may still carry fraud over the line. Lowering R2 (contact hijack) quiets the travelling holder, but it also drops Margaret's takeover to Medium at the email change, which is exactly the early detection the MVP exists for. That trade-off is a business judgement about acceptable false positives, and in a regulated firm a model-risk owner has to be able to say who made it and on what evidence.

The decisions behind the report come from real Cases, so the report can only see Rules that reached a Case. A Rule that never helps an Account reach High never gets a decision, and fraud the rules miss entirely never reaches an analyst; those gaps are what Blind Spots and evaluation's detection rate are for.

## Considered Options

- **Automatic re-weighting from decisions** (for example, points proportional to precision): no human in the loop, but a run of mistaken analyst decisions or a quiet week would silently change who gets investigated, and nobody could say who approved the change.
- **Learned weights (logistic regression on decisions)**: the industrialisation path, but it needs far more labelled decisions than the MVP has, and it trades readable points for coefficients a model-risk reviewer must validate.
- **Suggestions without a What-if**: simpler, but it hides the cost of each change, which is the part a human actually needs to judge.

## At industrialisation

The same report becomes a change request reviewed by a model-risk owner, with four-eyes approval before a rules version is promoted, and a shadow run of the candidate version beside the live one before it scores real Alerts.

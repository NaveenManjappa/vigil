# The LLM is one `IChatClient`, chosen by `Llm:Provider`

Everything that talks to a model takes a `Microsoft.Extensions.AI` `IChatClient`, and the API registers exactly one, chosen by `Llm:Provider`: `Fake` or `AzureOpenAI`. Fake is the default when the setting is missing, and appsettings set it for Development, so tests, CI and the demo cost nothing and need no secret. The Fake plays a scripted Investigator (ADR-0007) and a Reviewer that approves (ADR-0006), and gives anything else a fixed reply. The agents never know which provider is behind the interface.

Azure OpenAI is reached through its v1 endpoint (`https://<resource>.openai.azure.com/openai/v1/`) with the plain `OpenAI` SDK and `Microsoft.Extensions.AI.OpenAI`'s `AsIChatClient()`, with the deployment name as the model. `Azure.AI.OpenAI` is not used: its only stable release (2.1.0) is built against an old OpenAI SDK, and newer ones are betas, while Microsoft Learn now recommends the v1 endpoint for new code. The endpoint and deployment are ordinary config; the key comes only from the AppHost's `azure-openai-key` secret parameter, persisted to user-secrets and passed to the API as `Llm__ApiKey`, like `neo4j-password`. The parameter is generated so the AppHost starts while the provider is Fake; to use Azure, replace it with the real key.

Settings are checked when the API starts, not on the first call: an unknown provider names the valid ones, and AzureOpenAI with a missing endpoint, deployment or key names the missing setting. A bad switch would otherwise surface hours later as a failed Investigation.

A `TokenLoggingChatClient` decorator wraps the registered client and logs the model, input and output tokens, and the estimated cost from `Llm:InputPricePer1kTokens` and `Llm:OutputPricePer1kTokens`, on every call. It sits below the agents, so a tool loop logs one line per model round trip, which is what Azure bills and what the cost view can be reconciled against.

Versions: Microsoft Agent Framework (`Microsoft.Agents.AI`, `Microsoft.Agents.AI.Workflows`) 1.26.0, the latest stable at build time, not the 1.13.0 that PROJECT.md and issue #28 name; `Microsoft.Extensions.AI` 10.11.0 and `OpenAI` 2.14.0. Workflows needs `OpenTelemetry.Api` 1.18.0, pinned beside the other OpenTelemetry 1.17.0 packages.

## Considered Options

- **`Azure.AI.OpenAI`'s `AzureOpenAIClient`**: the long-standing Azure route, but stable only at 2.1.0, behind the OpenAI SDK MAF pulls in, or beta.
- **Ollama or plain OpenAI providers**: one more `case` in the switch and a package each; left out because nothing in the demo needs them, and the Fake already makes tests free.
- **Fake only in tests, swapped in through DI**: keeps production code free of a fake, but the free demo and Development runs need it too, so it is a provider like any other.

# Vigil user guide

This guide tells you how to set up, configure and operate Vigil from the start to the end.
It is for engineers and operators who are new to Vigil.

Part A tells you how to prepare the system.
Part B takes one fraud case through the full system.
Each step gives the command, the result that you will see, and the configuration or code that controls the step.

The guide uses the terms in [GLOSSARY.md](../GLOSSARY.md).
A term with a capital letter, for example Alert, Case or Finding, has the meaning that the glossary gives.
For the design of each part, read [architecture.md](architecture.md).
If a step fails, read [runbook.md](runbook.md).

The example outputs in this guide come from a real run on 24 October 2026.
Identifiers such as Case IDs and ports are different on your computer.

## Contents

**Part A: Set up and configure**

1. [What Vigil does](#1-what-vigil-does)
2. [Before you start](#2-before-you-start)
3. [Set up Azure OpenAI (optional)](#3-set-up-azure-openai-optional)
4. [Configure Vigil](#4-configure-vigil)

**Part B: Operate Vigil from the start to the end**

1. [Start the system](#5-start-the-system)
2. [Send Portal Events](#6-send-portal-events)
3. [Read the Alert](#7-read-the-alert)
4. [Examine the knowledge graph](#8-examine-the-knowledge-graph)
5. [Follow the Case and the Investigation](#9-follow-the-case-and-the-investigation)
6. [Record the Analyst Decision](#10-record-the-analyst-decision)
7. [Monitor the system](#11-monitor-the-system)
8. [Operate with Azure OpenAI](#12-operate-with-azure-openai)
9. [Measure the rules and the agents](#13-measure-the-rules-and-the-agents)
10. [Learn from Analyst Decisions](#14-learn-from-analyst-decisions)
11. [Change a Rule safely](#15-change-a-rule-safely)
12. [Reset and recover](#16-reset-and-recover)

---

# Part A: Set up and configure

## 1. What Vigil does

Vigil finds account takeover and registration fraud on an investor portal.
It finds the fraud before the fraudster changes the bank details or sells the shares.

Vigil does these tasks in sequence:

1. It receives each Portal Event, for example a password reset or an email change.
2. It writes the event to PostgreSQL and to a Neo4j knowledge graph.
3. It scores the Account with Rules. The result is an Alert with a Score and a Risk Band.
4. It opens a Case when an Alert reaches the High band.
5. Two AI agents investigate the Case. The Investigator writes a Finding. The Reviewer checks the Finding.
6. The Case stops and waits for an analyst.
7. The analyst records a decision. Vigil records the Intervention and the audit trail.
8. Analyst Decisions go back into a Rule-Tuning Report. A person uses the report to change the Rules.

The rules are cheap and they examine every event.
The AI agents examine only the risky Accounts.
A person makes each important decision.

The system has these parts:

| Part | What it does |
|---|---|
| `Vigil.Api` | Receives events, scores them, runs Investigations, and gives the HTTP endpoints |
| PostgreSQL | Keeps all events, Alerts, Cases, decisions and Audit Entries |
| Neo4j | Keeps the knowledge graph of Accounts and their shared identifiers |
| `Vigil.Simulator` | Sends test events to the API and decides Cases from test labels |
| `Vigil.Evaluation` | Measures the Rules and the agents, and writes reports |
| `Vigil.AppHost` | Starts all the parts on your computer and shows the dashboard |

## 2. Before you start

### 2.1 Install the tools

Install these tools on your computer:

| Tool | Version | Use |
|---|---|---|
| .NET SDK | 10 | Builds and runs all the projects |
| Docker Desktop | Current | Runs PostgreSQL and Neo4j, and the test containers |
| Git | Current | Gets the code |
| curl | Any | Sends requests to the API |
| Python 3 | Any | Counts Cases in [section 14](#14-learn-from-analyst-decisions). Optional. |
| Azure CLI | 2.51 or later | Optional. Creates the Azure OpenAI resource from a terminal |

Make sure that Docker is running before you start Vigil.
Vigil does not start without Docker.

### 2.2 Get the code

```bash
git clone https://github.com/NaveenManjappa/vigil.git
cd vigil
dotnet build
```

Do all the commands in this guide from the repository root.
The commands read the `config/`, `scenarios/` and `datasets/` folders from the current folder.

### 2.3 Know the folders

| Folder | Contents |
|---|---|
| `src/` | The projects: `Vigil.Api`, `Vigil.Detection`, `Vigil.Graph`, `Vigil.Agents`, and others |
| `config/` | The Rules files (`rules.v1.json`, `rules.v2.json`) and the agent prompts (`prompts/*.md`) |
| `scenarios/` | Hand-written Scenarios, for example `margaret-ato.json` |
| `datasets/` | Generated Datasets (not in Git) and Generated Scenarios (`datasets/generated/`, in Git) |
| `eval-reports/` | Evaluation reports and Rule-Tuning Reports |
| `docs/` | This guide, the architecture, the runbook and the ADRs |

## 3. Set up Azure OpenAI (optional)

**PAID STEPS.** The steps in this section create resources that cost money when you use them.

Vigil has two LLM providers:

- **Fake.** This is the default. It gives fixed answers. It costs nothing and it needs no key.
- **AzureOpenAI.** This uses a real model in your Azure subscription. It costs money.

Do this section only if you want real AI results.
If you do not, go to [section 4](#4-configure-vigil) and keep the Fake provider.

The steps in this section agree with Microsoft Learn on 24 October 2026.
The Azure portal changes often. If a screen is different, use the Microsoft Learn page that each step gives.

### 3.1 Get a subscription that can use models

You must have an Azure subscription with Pay-As-You-Go billing.
A free trial subscription has no quota for Azure OpenAI models.

1. Sign in to the [Azure portal](https://portal.azure.com).
2. Open **Subscriptions**.
3. Make sure that your subscription shows the offer **Pay-As-You-Go**.

### 3.2 Set a budget alert before you spend money

Do this step first.
A budget alert sends you an email when your costs approach a limit.
Vigil has a budget of £5 to £10 for all LLM work.

1. In the Azure portal, open **Cost Management + Billing**.
2. Select your subscription.
3. Select **Budgets**.
4. Select **Add**.
5. Set **Name** to `vigil-llm`.
6. Set **Reset period** to **Monthly**.
7. Set **Amount** to `10`.
8. Select **Next**.
9. Add an alert of type **Actual** at `50` %.
10. Add an alert of type **Actual** at `90` %.
11. Type your email address.
12. Select **Create**.

Note: Azure sends budget emails from `azure-noreply@microsoft.com`. Add this address to your approved senders.

Note: Cost data in Azure can be up to 24 hours late. Do not use the cost view to stop a run that is in progress.
Vigil logs the estimated cost of each LLM call, so that you can see the cost immediately.

Reference: [Tutorial: Create and manage budgets](https://learn.microsoft.com/azure/cost-management-billing/costs/tutorial-acm-create-budgets).

### 3.3 Create the Azure OpenAI resource

You can use the portal or the Azure CLI. Use one of the two procedures.

**Procedure 1: the Azure portal**

1. In the Azure portal, select **Create a resource**.
2. Search for **Azure OpenAI**.
3. Select **Create**.
4. Select your subscription.
5. Create a new resource group with the name `rg-vigil`.
6. Select a region that has the model that you want. For example, select **Sweden Central** or **East US 2**.
7. Type a unique name, for example `vigil-openai-<your initials>`. This name becomes part of the endpoint.
8. Set **Pricing tier** to **Standard S0**.
9. Select **Review + create**.
10. Select **Create**.
11. Wait until the deployment is complete.
12. Select **Go to resource**.

**Procedure 2: the Azure CLI**

```bash
az login
az group create --name rg-vigil --location swedencentral

az cognitiveservices account create \
  --resource-group rg-vigil \
  --name vigil-openai-<your initials> \
  --location swedencentral \
  --kind OpenAI \
  --sku S0 \
  --custom-domain vigil-openai-<your initials>
```

The `--custom-domain` value makes the endpoint `https://vigil-openai-<your initials>.openai.azure.com/`.

Reference: [Create and deploy an Azure OpenAI resource](https://learn.microsoft.com/azure/ai-foundry/openai/how-to/create-resource).

### 3.4 Deploy a chat model

Vigil needs one chat model that supports tool calls and structured JSON output.
A small, cheap model is enough. This guide uses `gpt-4.1-mini`.

**Procedure 1: the Foundry portal**

1. Open the [Foundry portal](https://ai.azure.com).
2. Select your subscription and your Azure OpenAI resource.
3. Select **Deployments** (or **Models + endpoints**).
4. Select **Deploy model**.
5. Select **Deploy base model**.
6. Select `gpt-4.1-mini`.
7. Select **Confirm**.
8. Set **Deployment name** to `vigil-chat`.
9. Set **Deployment type** to **Global Standard**.
10. Set **Tokens per Minute Rate Limit** to `50K`. This is enough for Vigil.
11. Select **Deploy**.

**Procedure 2: the Azure CLI**

First, find the model versions that your resource can deploy:

```bash
az cognitiveservices account list-models \
  --resource-group rg-vigil --name vigil-openai-<your initials> \
  --query "[?name=='gpt-4.1-mini'].{name:name, version:version}" -o table
```

Then deploy the model. Use a version from the result. `--sku-capacity 50` gives 50,000 tokens per minute.

```bash
az cognitiveservices account deployment create \
  --resource-group rg-vigil \
  --name vigil-openai-<your initials> \
  --deployment-name vigil-chat \
  --model-name gpt-4.1-mini \
  --model-version "<version from the list>" \
  --model-format OpenAI \
  --sku-name GlobalStandard \
  --sku-capacity 50
```

CAUTION: Vigil uses the **deployment name**, not the model name. In this guide, the deployment name is `vigil-chat`.
If you use the model name, Azure returns the error 404.

References: [Deploy a model](https://learn.microsoft.com/azure/ai-foundry/openai/how-to/create-resource#deploy-a-model), [Deployment types](https://learn.microsoft.com/azure/ai-foundry/openai/how-to/deployment-types), [Manage quota](https://learn.microsoft.com/azure/ai-foundry/openai/how-to/quota).

### 3.5 Get the endpoint and the key

1. In the Azure portal, open your Azure OpenAI resource.
2. Under **Resource Management**, select **Keys and Endpoint**.
3. Copy the **Endpoint**. It has the form `https://<name>.openai.azure.com/`.
4. Copy **KEY 1**.

With the CLI, do these commands:

```bash
az cognitiveservices account show -g rg-vigil -n vigil-openai-<your initials> --query properties.endpoint -o tsv
az cognitiveservices account keys list -g rg-vigil -n vigil-openai-<your initials> --query key1 -o tsv
```

WARNING: Keep the key secret. Do not put it in a file that Git tracks. Do not paste it into a chat or a ticket.

Use the endpoint exactly as Azure shows it.
Do not add `/openai/v1/` to the endpoint. Vigil adds it. This code in `src/Vigil.Agents/Llm/LlmRegistration.cs` does it:

```csharp
// Azure's v1 endpoint speaks the plain OpenAI protocol, so the stable OpenAI SDK serves it (ADR-0009).
var v1 = new Uri($"{endpoint.TrimEnd('/')}/openai/v1/");
var openAi = new OpenAIClient(new ApiKeyCredential(apiKey), new OpenAIClientOptions { Endpoint = v1 });
return openAi.GetChatClient(deployment).AsIChatClient();
```

Reference: [Azure OpenAI v1 API](https://learn.microsoft.com/azure/foundry/openai/api-version-lifecycle).

### 3.6 Find the token prices

Vigil calculates the estimated cost of each call from two prices.
Each price is in pounds (£) for 1,000 tokens.

1. Open the [Azure OpenAI pricing page](https://azure.microsoft.com/pricing/details/cognitive-services/openai-service/).
2. Find your model and your deployment type.
3. Read the input price and the output price for 1 million tokens.
4. Divide each price by 1,000. This gives the price for 1,000 tokens.
5. Change the currency to pounds.

Example: an input price of $0.40 for 1 million tokens is $0.0004 for 1,000 tokens. This is approximately £0.0003.

The default values in `src/Vigil.Api/appsettings.json` are `0.0003` (input) and `0.0012` (output).
These values are approximately correct for `gpt-4.1-mini` with Global Standard.
Change them if your model or your prices are different. [Section 4](#4-configure-vigil) tells you where.

### 3.7 Delete the Azure resources when you finish

The deployment costs nothing when you do not use it.
But delete the resources when you do not need them any more.

```bash
az cognitiveservices account deployment delete -g rg-vigil -n vigil-openai-<your initials> --deployment-name vigil-chat
az group delete --name rg-vigil --yes
```

Azure keeps a deleted Azure OpenAI resource for some days (soft delete). During this time, you cannot use its name again.
To remove it immediately, purge it:

```bash
az cognitiveservices account purge --location swedencentral --resource-group rg-vigil --name vigil-openai-<your initials>
```

Note: In production, Vigil uses Microsoft Entra ID and a managed identity, not a key.
Read [industrialisation.md](industrialisation.md).

## 4. Configure Vigil

### 4.1 Where the settings are

Vigil uses the standard .NET configuration. A setting can come from four places:

| Place | Used for | In Git |
|---|---|---|
| `src/Vigil.Api/appsettings.json` | Default values for all environments | Yes |
| `src/Vigil.Api/appsettings.Development.json` | Values for your computer. These replace the defaults. | Yes |
| User secrets of the AppHost | Passwords and the Azure OpenAI key | No |
| Environment variables | Values for one run, and all settings of the evaluation command | No |

An environment variable uses two underscores where a setting uses a colon.
For example, the setting `Llm:Provider` is the environment variable `Llm__Provider`.

### 4.2 All the settings

| Setting | Default | Set it in | Change it when |
|---|---|---|---|
| `Rules:Path` | `config/rules.v2.json` | `appsettings.json` | You deploy a new Rules Version ([section 15](#15-change-a-rule-safely)) |
| `Llm:Provider` | `Fake` | `appsettings.Development.json` | You want real AI results: set `AzureOpenAI` |
| `Llm:Endpoint` | none | `appsettings.Development.json` | You use Azure OpenAI: the endpoint from [3.5](#35-get-the-endpoint-and-the-key) |
| `Llm:Deployment` | none | `appsettings.Development.json` | You use Azure OpenAI: the deployment name, for example `vigil-chat` |
| `Llm:ApiKey` | generated value | User secret `Parameters:azure-openai-key` | You use Azure OpenAI: the key from [3.5](#35-get-the-endpoint-and-the-key) |
| `Llm:InputPricePer1kTokens` | `0.0003` | `appsettings.json` | Your input price is different ([3.6](#36-find-the-token-prices)) |
| `Llm:OutputPricePer1kTokens` | `0.0012` | `appsettings.json` | Your output price is different |
| `Investigation:Enabled` | `true` | `appsettings.json` | You send a large Dataset and you do not want Investigations |
| `Investigation:DailyLimit` | `20` | `appsettings.json` | You want more or fewer Investigations each UTC day |
| `Intervention:BlockBankChangesDays` | `7` | `appsettings.json` | The business changes the length of a bank-change block |
| `Database:MigrateOnStartup` | `true` in Development | `appsettings.Development.json` | Do not change it on your computer |
| `ConnectionStrings:vigil`, `Neo4j:Uri`, `Neo4j:Password` | set by the AppHost | Do not set | Only when you run the API without the AppHost |

The default files contain these values:

```json
// src/Vigil.Api/appsettings.json
{
  "Rules": { "Path": "config/rules.v2.json" },
  "Llm": {
    "Provider": "Fake",
    "InputPricePer1kTokens": 0.0003,
    "OutputPricePer1kTokens": 0.0012
  },
  "Investigation": { "Enabled": true, "DailyLimit": 20 },
  "Intervention": { "BlockBankChangesDays": 7 }
}
```

```json
// src/Vigil.Api/appsettings.Development.json
{
  "Database": { "MigrateOnStartup": true },
  "Llm": { "Provider": "Fake" }
}
```

### 4.3 The secrets

The AppHost makes three secrets the first time that it starts.
It keeps them in the user secrets of `src/Vigil.AppHost`, outside the repository.

| Secret | Use |
|---|---|
| `Parameters:postgres-password` | The PostgreSQL password |
| `Parameters:neo4j-password` | The Neo4j password |
| `Parameters:azure-openai-key` | The Azure OpenAI key. The AppHost makes a placeholder value, so that it can start with the Fake provider. |

To see the names of the secrets, do this command:

```bash
dotnet user-secrets list --project src/Vigil.AppHost
```

CAUTION: The PostgreSQL and Neo4j passwords go into their Docker volumes the first time that the containers start.
If you delete the user secrets, also delete the volumes. If you do not, the databases refuse the connection.
[Section 16](#16-reset-and-recover) tells you how.

The AppHost gives the secrets to the API. This code in `src/Vigil.AppHost/AppHost.cs` does it:

```csharp
// Generated so the AppHost starts without prompting while Llm:Provider is Fake; to use Azure OpenAI, replace the
// persisted value with the real key (user-secrets `Parameters:azure-openai-key`), so it never lands in source control.
var azureOpenAiKey = builder.AddParameter(
    "azure-openai-key",
    new GenerateParameterDefault { MinLength = 32, Special = false },
    secret: true,
    persist: true);

var api = WithStores(builder.AddProject<Projects.Vigil_Api>("api"))
    .WithEnvironment("Llm__ApiKey", azureOpenAiKey);
```

### 4.4 Configure the API for Azure OpenAI

Do these steps only after [section 3](#3-set-up-azure-openai-optional).

1. Put the real key into the user secrets. This replaces the placeholder:

   ```bash
   dotnet user-secrets set "Parameters:azure-openai-key" "<KEY 1>" --project src/Vigil.AppHost
   ```

2. Open `src/Vigil.Api/appsettings.Development.json`.
3. Change the `Llm` section to this:

   ```json
   "Llm": {
     "Provider": "AzureOpenAI",
     "Endpoint": "https://vigil-openai-<your initials>.openai.azure.com/",
     "Deployment": "vigil-chat"
   }
   ```

4. Do not commit this change. Your endpoint is specific to you.

The API examines these settings when it starts.
If a setting is missing, the API stops and shows the name of the setting. For example:

```text
Llm:Endpoint is required when Llm:Provider is AzureOpenAI. Set it in configuration, or use Llm:Provider Fake.
```

### 4.5 Configure the evaluation command for Azure OpenAI

The evaluation command does not read the user secrets of the AppHost.
It reads environment variables. Set them in the terminal where you run the command:

```bash
export Llm__Endpoint=https://vigil-openai-<your initials>.openai.azure.com/
export Llm__Deployment=vigil-chat
export Llm__ApiKey=<KEY 1>
# Optional. Use them if your prices are different from the defaults.
export Llm__InputPricePer1kTokens=0.0003
export Llm__OutputPricePer1kTokens=0.0012
```

The evaluation command reads the default prices from `src/Vigil.Api/appsettings.json`.
The environment variables replace them.

---

# Part B: Operate Vigil from the start to the end

Part B follows one fraud case from the first event to the decision.
The case is Margaret. Read her story in [PROJECT.md Section 3.1](../PROJECT.md#31-fraud-case--margaret).

In summary, Margaret logs in from her laptop in the UK for many months.
A fraudster gets her email password.
The fraudster uses device `D-77` in Romania. In 10 minutes, the fraudster resets her password, enrols a new MFA phone and changes her email.
The next day, the fraudster changes her bank details.
Three other Accounts used device `D-77` before. One of them, Priya, is a Confirmed Fraud.

Use the Fake provider for Part B, except in [section 12](#12-operate-with-azure-openai).

## 5. Start the system

### 5.1 Start the AppHost

1. Make sure that Docker is running.
2. Do this command:

   ```bash
   dotnet run --project src/Vigil.AppHost
   ```

3. Wait for the output that follows. The token in the URL is different each time.

   ```text
      Dashboard:  https://localhost:17147/login?t=efe2f81451cdff63fcfda1a60730a6c5
           Logs:  /Users/<you>/.aspire/logs/cli_20261024T171713_c53bf5c2.log
                  Press CTRL+C to stop the AppHost and exit.
   ```

4. Open the dashboard URL in a browser.

The first start can take some minutes, because Docker downloads the PostgreSQL and Neo4j images.

### 5.2 Make sure that the system is healthy

In the dashboard, look at the **Resources** page.

| Resource | State | Note |
|---|---|---|
| `postgres` and `vigil` | Running, Healthy | The database |
| `neo4j` | Running, Healthy | The knowledge graph |
| `api` | Running, Healthy | The API. It updates the database schema when it starts. |
| `simulator` | Not started | It starts only when you select **Start** |
| `rebuild-graph` | Not started | It starts only when you select **Start** |

To make sure that the API answers, do this command:

```bash
curl http://localhost:5107/rules
```

The API returns the Rules that are in use, with `"version": "v2"`.

These addresses do not change:

| Address | What |
|---|---|
| http://localhost:5107 | The API |
| http://localhost:5107/scalar | The API reference, where you can try each endpoint |
| http://localhost:7474 | Neo4j Browser. The user is `neo4j`. The password is the secret `Parameters:neo4j-password`. |

### 5.3 Know what the AppHost starts

`src/Vigil.AppHost/AppHost.cs` defines the parts. This is a short version of the file:

```csharp
var postgres = builder.AddPostgres("postgres")
    .WithDataVolume("vigil-postgres-data");
var db = postgres.AddDatabase("vigil");

var neo4j = builder.AddContainer("neo4j", "neo4j", "5-community")
    .WithHttpEndpoint(port: 7474, targetPort: 7474, name: "http")
    .WithEndpoint(port: 7687, targetPort: 7687, name: "bolt", scheme: "tcp")
    .WithVolume("vigil-neo4j-data", "/data");

var api = WithStores(builder.AddProject<Projects.Vigil_Api>("api"))
    .WithEnvironment("Llm__ApiKey", azureOpenAiKey);

builder.AddProject<Projects.Vigil_Simulator>("simulator")
    .WithReference(api)
    .WaitFor(api)
    .WithExplicitStart();
```

The data stays in the Docker volumes `vigil-postgres-data` and `vigil-neo4j-data`.
When you stop and start the AppHost, the data stays.

## 6. Send Portal Events

### 6.1 Know the Scenario file

A Scenario is a JSON file that describes one Account journey.
Margaret's Scenario is `scenarios/margaret-ato.json`:

```json
{
  "name": "margaret-ato",
  "description": "PROJECT.md Section 3.1: after months of logins from her laptop in Reading, a fraudster with Margaret's email password takes over her account from a new device in Romania, then tries to redirect payouts the next day.",
  "accountId": "ACC-MARGARET",
  "holderId": "HOLDER-MARGARET",
  "baseline": { "device": "D-MARGARET-LAPTOP", "ip": "203.0.113.51", "country": "GB" },
  "actors": {
    "fraudster": { "device": "D-77", "ip": "198.51.100.77", "country": "RO" }
  },
  "steps": [
    { "after": "0m",    "type": "PasswordReset",      "as": "fraudster" },
    { "after": "4m",    "type": "MfaEnrolled",        "as": "fraudster" },
    { "after": "10m",   "type": "EmailChanged",       "as": "fraudster", "attributes": { "NewEmail": "m.attacker@example.net" } },
    { "after": "1d5h",  "type": "BankDetailsChanged", "as": "fraudster", "attributes": { "NewBankAccountHash": "BA-9f31" } }
  ]
}
```

| Field | Required | Meaning |
|---|---|---|
| `name` | Yes | A short name in lower case with hyphens. Generated Scenarios use it as the file name. |
| `description` | Yes | A sentence that tells the story of the journey |
| `accountId` | Yes | The Account that the events belong to |
| `holderId` | No | The Holder. The default is `HOLDER-` and the `accountId`. |
| `start` | No | The time of the first step. The default is `2026-09-21T09:00:00Z`. |
| `baseline` | No | The usual `device`, `ip` and `country` of the Holder. The simulator makes `weeks` (default 13) of normal logins from them. Leave it out for a new Account. |
| `actors` | No | Other people who act on the Account. Each has a `device`, an `ip`, a `country`, and a `channel` (default `web`). |
| `steps` | Yes | The events. `after` is the time from the start, for example `10m` or `1d5h`. `type` is the event type. `as` names the actor; without it, the Holder acts on the baseline device. |
| `attributes` | No | Data for one step, for example `NewEmail` or `NewBankAccountHash` |
| `confirmedFraud` | No | A date. The simulator then records the Account as a Confirmed Fraud with Source Seed. |
| `label` | No | Required only in a Generated Scenario: `kind` (`Ato`, `Registration`, `Ring`, `Evasion` or `Genuine`) and `intent` |

The parser is strict. It rejects a file with an unknown field or without a required field, and it gives the file name and the step.

Other Scenarios in `scenarios/` set up Margaret's story:

- `priya-ato.json` uses `D-77`. It has `"confirmedFraud"`, so Priya becomes a Confirmed Fraud.
- `derek-reset.json` and `lena-reset.json` also use `D-77`.
- `patel-house-move.json` is a genuine Holder who moves house.

### 6.2 Send the Scenarios

You can send the Scenarios in two ways.

**Procedure 1: the dashboard**

1. On the **Resources** page, find `simulator`.
2. Select **Start**.

**Procedure 2: a terminal**

The simulator cannot find the API by itself outside the AppHost.
Give it the API address with an environment variable:

```bash
services__api__http__0=http://localhost:5107 dotnet run --project src/Vigil.Simulator
```

The simulator sends every Scenario, in the sequence of its first event. The output includes these lines:

```text
info: Simulator[0]
      Posted margaret-ato: {"accepted":17,"alreadyIngested":0}
info: Simulator[0]
      Posted patel-house-move: {"accepted":15,"alreadyIngested":0}
```

To send one Scenario only, add `-- --scenario scenarios/margaret-ato.json`.

You can send the same Scenario again. Vigil identifies each event by its `EventId` and it does not store an event two times.
The second time, the output shows `"accepted":0` and a number in `alreadyIngested`.

### 6.3 Know what happens to each batch

`POST /events` does all of these tasks in one database transaction.
This is the main method, in `src/Vigil.Api/Events/Ingestion.cs`:

```csharp
foreach (var accountId in accounts) await db.LockAccountAsync(accountId, ct);

var result = await events.AppendAsync(batch, ct);

// Before the commit, so a Neo4j failure fails the request and the client's retry converges both stores.
await graph.ProjectAsync(batch, ct);

// A new link can give an Account outside the batch a Graph Rule Hit, such as the first Account on a shared
// bank account, so they are re-checked too.
var neighbours = await graphRules.NeighboursOfAsync(batch, ct);
await ScoreAsync(batchAccounts: accounts, neighbours, opened, ct);
```

1. Vigil locks each Account in the batch, so that two batches cannot change one Account at the same time.
2. Vigil adds the events to PostgreSQL.
3. Vigil adds the events to the Neo4j graph.
4. Vigil finds the Neighbours. A Neighbour is another Account that shares a device, a bank account, an email or a phone with the batch.
5. Vigil scores each Account in the batch and each Neighbour.
6. Vigil commits the transaction.
7. Vigil puts each new Case in the Investigation queue.

If one step fails, Vigil writes nothing. The client can send the batch again.

## 7. Read the Alert

### 7.1 Get the Alerts

```bash
curl "http://localhost:5107/alerts"
```

After you send the Scenarios, the result has four Alerts:

| Account | Score | Band |
|---|---|---|
| ACC-MARGARET | 135 | Critical |
| ACC-PRIYA | 110 | Critical |
| ACC-DEREK | 85 | High |
| ACC-LENA | 75 | High |

Mr Patel has no Alert. His events agree with no Rule.

To get only one band, add a filter, for example `curl "http://localhost:5107/alerts?band=High"`.

### 7.2 Read Margaret's Alert

```bash
curl http://localhost:5107/alerts/<alert id>
```

This is the result, with two Rule Hits removed so that it is shorter:

```json
{
  "id": "01a16eea-e953-78bb-8d94-dc83e20af79d",
  "accountId": "ACC-MARGARET",
  "score": 135,
  "band": "Critical",
  "rulesVersion": "v2",
  "hits": [
    {
      "ruleId": "G2",
      "points": 25,
      "evidenceEventIds": ["3067743d-ea92-5009-8aa9-7b5be6470f6a", "20b00859-9b5e-52c2-a911-57c037415019"],
      "triggeredAt": "2026-09-21T09:00:00+00:00",
      "explanation": "Shares Device D-77 with Confirmed Fraud Account ACC-PRIYA"
    },
    {
      "ruleId": "R0",
      "points": 10,
      "evidenceEventIds": ["20b00859-9b5e-52c2-a911-57c037415019"],
      "triggeredAt": "2026-09-21T09:00:00+00:00",
      "explanation": "First sighting on this Account: new device D-77, new country RO"
    },
    {
      "ruleId": "R1",
      "points": 30,
      "evidenceEventIds": ["20b00859-9b5e-52c2-a911-57c037415019", "96d126f5-e50f-5692-951f-49e0ff7a8e41"],
      "triggeredAt": "2026-09-21T09:04:00+00:00",
      "explanation": "Password reset then MFA enrolment, 4 min apart; window 1h"
    },
    {
      "ruleId": "R3",
      "points": 30,
      "evidenceEventIds": ["8c05be13-887b-589b-a83f-4ffed06551d5", "c2de8b21-1cae-502f-8c04-408c61319664"],
      "triggeredAt": "2026-09-22T14:00:00+00:00",
      "explanation": "Contact change then bank-detail change, 28h 50 min apart; window 72h"
    }
  ]
}
```

Each Rule Hit has these fields:

| Field | Meaning |
|---|---|
| `ruleId` and `points` | The Rule and the points that it adds to the Score |
| `evidenceEventIds` | The Portal Events that caused the hit. An analyst can find each one in the timeline. |
| `triggeredAt` | The event time of the hit, not the time when Vigil calculated it |
| `explanation` | A sentence that tells why the Rule agrees with the events |

The `rulesVersion` tells you which Rules file made the Alert.
Vigil never calculates an old Alert again with a newer Rules file.

### 7.3 Know how the Score increases

The Alert collects the Rule Hits in sequence of event time:

| Event | New Rule Hits | Score | Band |
|---|---|---|---|
| `PasswordReset` from D-77 in Romania | R0 +10, G2 +25, G3 +20 | 55 | Medium |
| `MfaEnrolled`, 4 minutes later | R1 +30 | 85 | **High. Vigil opens a Case.** |
| `EmailChanged`, 10 minutes after the reset | R2 +20 | 105 | Critical |
| `BankDetailsChanged`, the next day | R3 +30 | 135 | Critical |

The Alert reached High before the bank change. This is the purpose of Vigil.

These rules apply to the Score:

- Each Rule adds its points only one time in an Alert.
- The Score never decreases. A band can only go up.
- A Rule Hit stays in the same Alert if it occurs at most 7 days after the previous hit. A longer gap starts a new Alert.

### 7.4 Know the Rules file

The Rules are in `config/rules.v2.json`. This is the start of the file, and three of its Rules:

```json
{
  "version": "v2",
  "riskPeriodDays": 7,
  "bands": { "medium": 30, "high": 60, "critical": 90 },
  "rules": [
    {
      "id": "R1",
      "name": "Recovery takeover",
      "kind": "sequence",
      "steps": [["PasswordReset"], ["MfaEnrolled"]],
      "windowMinutes": 60,
      "points": 30,
      "explanation": "Password reset then MFA enrolment, {elapsed} apart; window {window}"
    },
    {
      "id": "G2",
      "name": "Known-fraud proximity",
      "kind": "graph",
      "points": 25,
      "explanation": "Shares {identifier} {value} with Confirmed Fraud Account {account}"
    },
    {
      "id": "G3",
      "name": "Ring indicator",
      "kind": "graph",
      "accountThreshold": 3,
      "points": 20,
      "explanation": "{identifier} {value} is shared by {accounts} Accounts; Ring threshold {threshold}"
    }
  ]
}
```

| Field | Meaning |
|---|---|
| `version` | The Rules Version. Every Alert records it. |
| `riskPeriodDays` | The longest gap between two hits in one Alert |
| `bands` | The lowest Score for each band. A Score below 30 is Low. |
| `kind` | `sequence` (two events in a Window), `firstSighting` (a new device or country), or `graph` |
| `steps` | For a sequence Rule: the first event type, then the second event type. Each step can list alternatives. |
| `windowMinutes` | The longest time between the two steps |

### 7.5 Try a Rule on a Scenario without the API

You can score a Scenario in memory. The API does not have to run.

```bash
dotnet run --project src/Vigil.Evaluation -- scenario scenarios/margaret-ato.json
```

```text
| Step | After | Event | As | New Rule Hits | Score | Band |
|---|---|---|---|---|---|---|
| 1 | +0m | PasswordReset | fraudster | R0 +10: First sighting on this Account: new device D-77, new country RO | 10 | Low |
| 2 | +4m | MfaEnrolled | fraudster | R1 +30: Password reset then MFA enrolment, 4 min apart; window 1h | 40 | Medium |
| 3 | +10m | EmailChanged | fraudster | R2 +20: Password reset then contact change, 10 min apart; window 24h | 60 | High |
| 4 | +1d5h | BankDetailsChanged | fraudster | R3 +30: Contact change then bank-detail change, 28h 50 min apart; window 72h | 90 | Critical |

First reached High at step 3, EmailChanged (+10m); peak Critical.

Graph Rules G1, G2, G3 skipped: they need the graph, which a dry run does not have, so the live Score can be higher.
```

The dry run has no graph. Thus Margaret gets 90, not 135.
To try a changed Rule, copy the Rules file, change it, and add `--rules my-rules.json`.

## 8. Examine the knowledge graph

### 8.1 Open Neo4j Browser

1. Open http://localhost:7474.
2. Type the user `neo4j` and the password from the user secrets.
3. Select **Connect**.

### 8.2 See the Accounts that share D-77

Type this query and select **Run**:

```cypher
MATCH (a:Account)-[:USED_DEVICE]->(d:Device {value: 'D-77'})
OPTIONAL MATCH (c:Case {outcome: 'ConfirmedFraud'})-[:CONCERNS]->(a)
RETURN a.accountId AS account, c IS NOT NULL AS confirmedFraud
```

The result shows four Accounts: ACC-MARGARET, ACC-PRIYA, ACC-DEREK and ACC-LENA.
ACC-PRIYA shows `confirmedFraud = true`.

To see the graph as a picture, type this query:

```cypher
MATCH p = (:Account {accountId: 'ACC-MARGARET'})-[]->(id)<-[]-(:Account)
RETURN p
```

### 8.3 Know the graph Rules

| Rule | Points | Agrees when |
|---|---|---|
| G1 Shared bank account | 30 | The Account pays to a bank account that the Account of a different Holder also pays to |
| G2 Known-fraud proximity | 25 | The Account shares a device, a bank account, an email or a phone with a Confirmed Fraud Account |
| G3 Ring indicator | 20 | At least 3 Accounts, this one included, share one device, bank account, email or phone |

IP addresses and postal addresses are in the graph, but they never link Accounts.
Many families, offices and mobile networks share them.

For more Cypher, read [graph-primer.md](graph-primer.md).

## 9. Follow the Case and the Investigation

### 9.1 Find the Case

When Margaret's Alert reached High, Vigil opened a Case.
A background worker started the Investigation immediately.

```bash
curl "http://localhost:5107/cases?status=AwaitingApproval"
```

```json
[
  {
    "id": "01a16eea-e953-74cd-8403-0cc6a400f4a1",
    "alertId": "01a16eea-e953-78bb-8d94-dc83e20af79d",
    "accountId": "ACC-MARGARET",
    "status": "AwaitingApproval",
    "band": "Critical",
    "score": 135,
    "openedAt": "2026-10-24T17:17:31.347874+00:00",
    "notInvestigated": false
  }
]
```

The list shows the worst Cases first: by band, then by Score, then by the time that the Case opened.

A Case has these statuses:

| Status | Meaning |
|---|---|
| `Open` | The Case waits for the worker. If `notInvestigated` is `true`, the budget stopped the Investigation. |
| `Investigating` | The agents work on the Case |
| `AwaitingApproval` | The Finding is ready. The Case waits for an analyst. |
| `Closed` | An analyst recorded a decision |

### 9.2 Know the budget settings

The worker examines the budget before each Investigation:

```csharp
// src/Vigil.Api/Cases/InvestigationOptions.cs
public sealed class InvestigationOptions
{
    /// <summary>Off for bulk Dataset replays, so they never call the LLM.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Investigations started per UTC day; later Cases wait Open, marked not investigated.</summary>
    public int DailyLimit { get; init; } = 20;
}
```

- If `Enabled` is `false`, the worker does not investigate. The Case stays `Open` with `notInvestigated: true`.
- If 20 Investigations started on this UTC day, a new Case stays `Open` with `notInvestigated: true`.
- The worker does not try that Case again later. An analyst can examine it without a Finding.

### 9.3 Know what the agents do

The Investigation is a Microsoft Agent Framework workflow with four steps:

| Step | Who | What |
|---|---|---|
| Enrich | Code | Makes a short report about the Alert. It replaces every identifier with a Pseudonym. |
| Investigate | Investigator agent, code, Reviewer agent | Writes the Finding, examines it, and reviews it |
| Await Approval | The workflow | Stops. Keeps its state in PostgreSQL. Waits for the analyst. |
| Act | Code | Records the decision and the Intervention |

The Investigator has three tools. Each tool can only read the Account of this Case:

| Tool | Gives |
|---|---|
| `GetAccountTimeline(days)` | The events of the Account in the last `days` days, at most 90 |
| `QueryGraphNeighbourhood()` | Each shared identifier, the other Accounts that use it, and if they are Confirmed Fraud |
| `GetHolderBaseline()` | The usual countries, devices and logins each week, from before the suspicious events |

The agents never see a real identifier.
`ACC-MARGARET` becomes `ACCOUNT-1`. `D-77` becomes `DEVICE-2`.
The numbers start again in each Investigation, so one device has different Pseudonyms in different Cases.

Each event that a tool gives has an Evidence Ref, for example `E-3`. Each graph fact has a ref, for example `G-2`.
The Finding must cite these refs.
Before the Reviewer starts, code makes sure that each cited ref exists.
If a ref does not exist, the Investigator must write the Finding again.

The instructions of the Investigator are in `config/prompts/investigator.v1.md`. This is the start of the file:

```markdown
You are a fraud investigator for an investor portal. Detection rules have flagged one Account and opened a Case.
Decide whether the Account has been taken over, was registered fraudulently, or is genuine, and recommend what the
analyst should do. You only recommend: a human analyst makes the decision.
```

The name of the prompt file is the Prompt Version. Every Case and Audit Entry records it.

### 9.4 Read the Finding

```bash
curl http://localhost:5107/cases/<case id>
```

This is the result with the Fake provider, with some refs removed so that it is shorter:

```json
{
  "id": "01a16eea-e953-74cd-8403-0cc6a400f4a1",
  "accountId": "ACC-MARGARET",
  "status": "AwaitingApproval",
  "reviewFlagged": false,
  "investigationFailed": false,
  "promptVersion": "investigator.v1+reviewer.v1",
  "finding": {
    "verdict": "LikelyAccountTakeover",
    "confidence": "High",
    "summary": "Scripted Finding from the Fake provider: the activity on ACC-MARGARET came from a device and country the Holder does not use, and that device is shared with a Confirmed Fraud Account.",
    "recommendation": "BlockBankChanges",
    "evidenceRefs": [
      { "ref": "E-3", "eventIds": ["20b00859-9b5e-52c2-a911-57c037415019"] },
      { "ref": "E-4", "eventIds": ["96d126f5-e50f-5692-951f-49e0ff7a8e41"] },
      { "ref": "G-2", "eventIds": ["20b00859-9b5e-52c2-a911-57c037415019", "cbfb76d0-806a-5ba8-940e-f6040186bef6",
                                   "eb0e5cec-0980-5394-83c8-f0f879fe8ebf", "3067743d-ea92-5009-8aa9-7b5be6470f6a"] }
    ]
  },
  "decision": null,
  "intervention": null
}
```

| Field | Meaning |
|---|---|
| `verdict` | `LikelyAccountTakeover`, `LikelyRegistrationFraud`, `Inconclusive` or `LikelyGenuine` |
| `confidence` | `Low`, `Medium` or `High`. The Investigator gives this value. |
| `summary` | At most 120 words. You see real identifiers here. The LLM saw only Pseudonyms. |
| `recommendation` | `BlockBankChanges`, `ContactHolder` or `NoAction` |
| `evidenceRefs` | Each cited ref and the events behind it. Use them to examine each claim. |
| `reviewFlagged` | `true` if the Reviewer rejected the Finding two times. Read the Finding with more care. |
| `investigationFailed` | `true` if the LLM failed. There is no Finding. Examine the Case without it. |

With Azure OpenAI, the summary comes from the real model, and it is different for each Case.

## 10. Record the Analyst Decision

### 10.1 Send the decision

The analyst reads the Finding and the evidence. Then the analyst sends a decision:

```bash
curl -X POST http://localhost:5107/cases/<case id>/decision \
  -H "Content-Type: application/json" \
  -d '{"outcome":"ConfirmedFraud","action":"BlockBankChanges","analystId":"analyst-7","notes":"Holder confirmed by phone it was not her."}'
```

| Field | Values |
|---|---|
| `outcome` | `ConfirmedFraud` or `FalsePositive` |
| `action` | `BlockBankChanges`, `ContactHolder` or `NoAction`. It can be different from the recommendation. |
| `analystId` | The name or ID of the analyst |
| `notes` | Free text |

The API returns HTTP 200 and the closed Case. The new parts of the Case are these:

```json
"status": "Closed",
"decision": {
  "outcome": "ConfirmedFraud",
  "action": "BlockBankChanges",
  "analystId": "analyst-7",
  "notes": "Holder confirmed by phone it was not her.",
  "decidedAt": "2026-10-24T17:17:56.611018+00:00"
},
"intervention": {
  "kind": "BlockBankChanges",
  "until": "2026-10-31T17:17:56.611018+00:00",
  "recordedAt": "2026-10-24T17:17:56.611018+00:00"
}
```

The Intervention lasts 7 days. The setting `Intervention:BlockBankChangesDays` controls this.

Vigil records the Intervention for the portal. Vigil does not stop later events itself.
The portal must read the Intervention and stop the bank change.

If the Case is not in `AwaitingApproval`, the API returns HTTP 409 and changes nothing.

### 10.2 Know what Act does

The decision starts the workflow again from its saved state. No LLM call occurs.
The Act step does these tasks in one transaction:

1. It records the Analyst Decision.
2. It records the Intervention.
3. It writes two Audit Entries: `DecisionRecorded` and `ActionTaken`.
4. It closes the Case.
5. For `ConfirmedFraud`, it makes the Account a Confirmed Fraud. Vigil adds it to the graph and scores its Neighbours again.

Thus, after this decision, an Account that shares a device with Margaret gets the G2 Rule Hit.

### 10.3 Read the audit trail

```bash
curl http://localhost:5107/cases/<case id>/audit
```

The result has four Audit Entries:

| Kind | Actor | Records |
|---|---|---|
| `InvestigationCompleted` | `vigil-investigator` | Model, Prompt Version, Rules Version, cited refs with their events, tokens |
| `ReviewCompleted` | `vigil-reviewer` | Model, Prompt Version, the review result, tokens |
| `DecisionRecorded` | `analyst-7` | The outcome and the notes |
| `ActionTaken` | `analyst-7` | The Intervention |

This is the first entry, with the refs removed:

```json
{
  "kind": "InvestigationCompleted",
  "actor": "vigil-investigator",
  "model": "fake-model",
  "promptVersion": "investigator.v1",
  "rulesVersion": "v2",
  "inputTokens": 0,
  "outputTokens": 0,
  "detail": "LikelyAccountTakeover, High confidence, recommends BlockBankChanges",
  "at": "2026-10-24T17:17:31.436331+00:00"
}
```

Vigil never changes an Audit Entry after it writes it.

### 10.4 Make sure that a restart does not lose a Case

Do this test with a Case in `AwaitingApproval`:

1. In the dashboard, find the `api` resource.
2. Select **Stop**.
3. Wait until the API stops.
4. Select **Start**. Wait until the state is **Running**.
5. Get the Case again. The status is still `AwaitingApproval`.
6. Send the decision. The API returns HTTP 200.

The workflow keeps its state in the PostgreSQL table `workflow_checkpoints`.
The decision continues the workflow from that state.

## 11. Monitor the system

### 11.1 See one trace from the event to the agents

1. In the dashboard, select **Traces**.
2. Find the trace for `POST /events` that sent Margaret's batch.
3. Open it.

The trace shows these spans:

| Span | What |
|---|---|
| `POST /events` | The request |
| `graph.project` | The write to Neo4j |
| `detect` and `graph.rules` | The scoring of each Account |
| `investigate case` | The Investigation, in the same trace |
| `invoke_agent Investigator(vigil-investigator)` | The Investigator, with `chat` and `execute_tool QueryGraphNeighbourhood` spans |
| `invoke_agent Reviewer(vigil-reviewer)` | The Reviewer |

The decision has its own trace, under `POST /cases/{id}/decision`.
Its `decide case` span has a link to the `investigate case` span.

### 11.2 See the metrics

Select **Metrics**, and then the `api` resource.

| Metric | What |
|---|---|
| `vigil.llm.tokens` | Tokens, by agent and by direction (input or output) |
| `vigil.llm.cost` | Estimated £ for each Investigation |
| `vigil.investigation.duration` | Seconds from the start of the Investigation to Await Approval |

With the Fake provider, the tokens and the cost are 0.

### 11.3 See the logs

Select **Console** or **Structured** logs for the `api` resource.
Each LLM call writes one line with the model, the tokens and the estimated £.

## 12. Operate with Azure OpenAI

**PAID STEP.** Each Investigation in this section costs money.

Do this section only after [section 3](#3-set-up-azure-openai-optional) and [section 4.4](#44-configure-the-api-for-azure-openai).

CAUTION: Each new Case now costs money. Do not send the 1,000-holder Dataset to the API with Azure OpenAI.

1. Stop the AppHost with Ctrl+C.
2. Make sure that `appsettings.Development.json` has `"Provider": "AzureOpenAI"`, your endpoint and your deployment name.
3. Start the AppHost again.
4. Look at the `api` logs. If a setting is missing, the API stops and shows the name of the setting.
5. Send one new Scenario.
6. Use a copy of Margaret's file with a new `accountId`. Vigil ignores events that it already has, so the old file does not open a new Case.
7. Wait until the Case is in `AwaitingApproval`.
8. Read the Finding. The summary now comes from the model.
9. Read the `api` logs. Find the lines from `TokenLoggingChatClient`. Each line shows the tokens and the estimated £ of one call.
10. Look at `vigil.llm.cost` in the dashboard.
11. After 24 hours, compare the estimate with the Azure cost view.

To go back to the free provider, set `"Provider": "Fake"` and start the AppHost again.

## 13. Measure the rules and the agents

### 13.1 Run the evaluation

The evaluation does not use the API or the AppHost. It needs Docker only.

```bash
dotnet run --project src/Vigil.Evaluation
```

The command takes approximately 90 seconds. It does these tasks:

1. It makes the Dataset `datasets/seed-42-holders-1000.jsonl` if the file does not exist. The Dataset has 1,000 Holders and 20,220 events.
2. It scores the Dataset with `rules.v1.json`, without the graph.
3. It sends all events into a temporary Neo4j container. Then it scores the Dataset with `rules.v2.json`, with the graph.
4. It runs the agents on the Agent Sample. The default sample is 20 Flagged Accounts and 5 Near-misses, so 25 Investigations.
5. It scores each Generated Scenario in `datasets/generated/` and finds the Blind Spots.

The output is this:

```text
Wrote eval-reports/20261024T162209Z-v1-v2-seed42.md
v1: detection rate 17/30, false-positive rate 20/970, early-detection rate 9/9
v2: detection rate 22/30, false-positive rate 20/970, early-detection rate 9/9
agents (Fake, not meaningful): Evidence Grounding 25/25, Verdict Agreement 10/25, 0 tokens, £0.0000; Judge 25/25 judged, 0 tokens
Blind Spots: 1 of 1 fraud Generated Scenarios never reached High on v2
```

| Option | Default | Use |
|---|---|---|
| `--seed` | `42` | The seed of the Dataset and of the Agent Sample |
| `--holders` | `1000` | The size of the Dataset |
| `--rules-off` | `config/rules.v1.json` | The Rules Version without the graph |
| `--rules-on` | `config/rules.v2.json` | The Rules Version with the graph |
| `--llm` | `Fake` | `Fake` or `AzureOpenAI` |
| `--agent-sample` | `20` | The number of Flagged Accounts for the agents. Vigil adds Near-misses: at most a quarter of this number, and never more than a quarter of the full sample. |
| `--out` | `eval-reports` | The folder for the report |

CAUTION: If you change a Persona, delete the old Dataset file first. If you do not, the evaluation uses the old file.
The [runbook](runbook.md#reset-data) tells you how.

### 13.2 Read the evaluation report

Open the new file in `eval-reports/`. The header tells you what made the report:

```markdown
| Run ID | `20261024T162209Z-v1-v2-seed42` |
| Rules Versions | v1 (graph off), v2 (graph on) |
| Prompt Versions | investigator.v1+reviewer.v1, judge.v1 |
| LLM | Fake, model fake-model |
```

An Account is Flagged when one of its Alerts reached High.

| Metric | Definition | v1 | v2 |
|---|---|---|---|
| Detection rate | Flagged fraud ÷ all fraud | 17 / 30 | 22 / 30 |
| False-positive rate | Flagged genuine ÷ all genuine | 20 / 970 | 20 / 970 |
| Early-detection rate | ATO fraud Flagged before the bank change ÷ all ATO fraud | 9 / 9 | 9 / 9 |

Read these results from the report:

- **The graph finds the fraud ring.** With v1, each ring Account gets only Medium. With v2, G1 and G3 put all five Accounts in Critical.
- **The slow-burn fraudster is a known miss.** Each step occurs just after the Window of its Rule. No fixed Window can find it.
- **The 20 false positives are Travelling holders.** A genuine Holder abroad resets the password, enrols MFA and changes the phone. Rules R0, R1 and R2 give 60, which is High.

The **By Persona** table shows each Persona and the result that Section 10.1 of PROJECT.md expects.

### 13.3 Read the agent section

The agent section shows these metrics:

| Metric | Definition |
|---|---|
| Evidence Grounding | First-attempt Findings whose cited refs all exist ÷ all first-attempt Findings |
| Verdict Agreement | Findings whose verdict agrees with the Label ÷ all Findings |
| Verdict Agreement on Near-misses | Near-miss Findings that say `LikelyGenuine` or `Inconclusive` ÷ Near-miss Findings |
| Reviewer rejection rate | Reviews that rejected the Finding ÷ all Reviews |
| Review Flag rate | Findings with the Review Flag ÷ all Findings |
| Investigations failed | Investigations with no Finding ÷ all Investigations |
| Tokens, cost and seconds | The mean for each Investigation |

The Judge gives each Finding a score from 1 to 5 for four criteria:
grounded claims, sound reasoning, proportionate recommendation, and clarity.
The Judge uses the same model as the Investigator. It can prefer the style of its own model.
Use the Judge scores to compare runs, not as an absolute grade.

With the Fake provider, the report shows this warning:

```markdown
> **Not meaningful:** the Fake provider writes the same scripted Finding for every Account, so these numbers only
> show that the harness runs. Run with `--llm AzureOpenAI` for real ones.
```

### 13.4 Run the evaluation with Azure OpenAI

**PAID STEP.** This step costs money. Before you do it, set the budget alert in [section 3.2](#32-set-a-budget-alert-before-you-spend-money).

Do this step only after [section 3](#3-set-up-azure-openai-optional) and [section 4.5](#45-configure-the-evaluation-command-for-azure-openai).

First, do a small test. `--agent-sample 4` gives 4 Flagged Accounts and 1 Near-miss, so 5 Investigations:

```bash
dotnet run --project src/Vigil.Evaluation -- --llm AzureOpenAI --agent-sample 4
```

If the result is correct, do the full run:

```bash
dotnet run --project src/Vigil.Evaluation -- --llm AzureOpenAI
```

The full run makes 25 Investigations and 25 Judge calls.
Each Investigation has at most two Investigator attempts and two Reviewer calls.
The report shows the total tokens and the total estimated £.

If a setting is missing, the command stops in the first second, before the slow steps.

### 13.5 Find Blind Spots with the Scenario Generator

**PAID STEP** with `--llm AzureOpenAI`. With `--llm Fake`, the command is free, but it gives only one fixed Scenario.

The Scenario Generator is an AI agent that acts as an attacker.
It reads the Rules and the example Scenarios. Then it writes new fraud Scenarios that the Rules can miss.

```bash
dotnet run --project src/Vigil.Evaluation -- generate-scenarios --count 5 --llm AzureOpenAI
```

| Option | Default | Use |
|---|---|---|
| `--count` | `5` | The number of Scenarios to ask for. The maximum is 10. |
| `--rules` | `config/rules.v2.json` | The Rules that the generator tries to avoid |
| `--llm` | `Fake` | `Fake` or `AzureOpenAI` |
| `--out` | `datasets/generated` | The folder for the new Scenarios |

The command makes one LLM call.
It keeps a Scenario only if all these conditions are true:

- The JSON agrees with the Scenario format.
- The Scenario has a `label` with a fraud kind and an intent.
- The sequence of event types and gaps is different from each Scenario that is already in the folder.

A Generated Scenario has a `label` that a hand-written Scenario does not need:

```json
"label": {
  "kind": "Ato",
  "intent": "MFA 65 minutes after the reset misses R1's 60-minute Window, and the sale waits out R4."
}
```

The next evaluation scores each Generated Scenario.
A fraud Scenario that never reaches High is a Blind Spot. The report shows it like this:

```markdown
| Scenario | Fraud kind | Intent | Peak band | Rules hit |
|---|---|---|---|---|
| fake-late-mfa-slow-cash-out | Ato | Fake-written placeholder, to be replaced by the Azure run: ... | Low | R0 |
```

After your first run with Azure OpenAI, do these steps:

1. Read each new file in `datasets/generated/`.
2. Do a dry run of each file that you are not sure about:

   ```bash
   dotnet run --project src/Vigil.Evaluation -- scenario datasets/generated/<name>.json
   ```

3. Delete the Fake placeholder:

   ```bash
   git rm datasets/generated/fake-late-mfa-slow-cash-out.json
   ```

4. Run the evaluation again.
5. Commit the Generated Scenarios and the new report together.

A person decides if a Blind Spot needs a new Rule. The generator does not change the Rules.

## 14. Learn from Analyst Decisions

The Rule-Tuning Report shows which Rules give too many false alarms.
It uses real Analyst Decisions from PostgreSQL.
On your computer, the Stand-in Analyst makes these decisions from the Labels of the Dataset.

The full procedure uses the Fake provider. It costs nothing.

### 14.1 Prepare the API

1. Stop the AppHost.
2. Reset the data, so that the report counts only the decisions of this run. Use the procedure in the [runbook](runbook.md#reset-data).

3. Open `src/Vigil.Api/appsettings.Development.json`.
4. Make sure that `"Provider"` is `"Fake"`.
5. Add an `Investigation` section with a higher daily limit:

   ```json
   {
     "Database": { "MigrateOnStartup": true },
     "Llm": { "Provider": "Fake" },
     "Investigation": { "DailyLimit": 100 }
   }
   ```

6. Start the AppHost.

CAUTION: Do this step before you send the Dataset.
The Dataset opens approximately 42 Cases. With the limit of 20, the other Cases stay `Open` and the worker never investigates them.
An `Open` Case cannot get a decision.

CAUTION: Do not commit the change to `appsettings.Development.json`. Remove it when you finish.

### 14.2 Send the Dataset

```bash
dotnet run --project src/Vigil.Simulator -- generate --seed 42 --holders 1000
services__api__http__0=http://localhost:5107 dotnet run --project src/Vigil.Simulator -- post-dataset
```

```text
Wrote datasets/seed-42-holders-1000.jsonl
info: Simulator[0]
      Posted 20220 events from 1000 Accounts in datasets/seed-42-holders-1000.jsonl
```

The simulator sends the events without their Labels, in sequence of event time, in batches of 200.
This takes approximately 75 seconds.

The worker investigates the new Cases in the background, one at a time.
To see the progress, do this command:

```bash
curl -s http://localhost:5107/cases | python3 -c "import json,sys,collections; d=json.load(sys.stdin); print(len(d), dict(collections.Counter(c['status'] for c in d)))"
```

```text
46 {'Closed': 1, 'AwaitingApproval': 45}
```

In this example, 42 Cases came from the Dataset and 4 came from the Scenarios. Margaret's Case is already closed.

### 14.3 Let the Stand-in Analyst decide

```bash
services__api__http__0=http://localhost:5107 dotnet run --project src/Vigil.Simulator -- decide --by-label
```

```text
info: Simulator[0]
      Stand-in Analyst decided 42 Cases; 0 were decided by someone else first
warn: Simulator[0]
      Left for a human, not in datasets/seed-42-holders-1000.jsonl: ACC-PRIYA, ACC-DEREK, ACC-LENA
```

The Stand-in Analyst decides each Case in `AwaitingApproval` from the Label of its Account:

| Label | Outcome | Action |
|---|---|---|
| Fraud | `ConfirmedFraud` | `BlockBankChanges` |
| Genuine | `FalsePositive` | `NoAction` |

It records each decision with the analyst ID `stand-in`.
It does not decide a Case whose Account is not in the Dataset.

If the command tells you that Cases are still `Open` or `Investigating`, wait some seconds. Then do the command again.

### 14.4 Make the Rule-Tuning Report

The `tune` command reads the decisions from PostgreSQL. It needs the connection string.

1. Get the connection string. Use the procedure in the [runbook](runbook.md#find-the-postgres-connection-string).
2. Do the command:

   ```bash
   dotnet run --project src/Vigil.Evaluation -- tune \
     --connection "Host=localhost;Port=<port>;Database=vigil;Username=postgres;Password=<password>"
   ```

```text
R0 10 → 5 (precision 15/35): false-positive rate 20 → 0, early-detection rate 9 → 0 of 9
R1 30 → 15 (precision 10/30): false-positive rate 20 → 0, early-detection rate 9 → 0 of 9
R2 20 → 10 (precision 10/30): false-positive rate 20 → 0, early-detection rate 9 → 0 of 9
Wrote eval-reports/rule-tuning-20261024T172201Z-v2-seed42.md
```

The command also starts a temporary Neo4j container, because the What-if uses the graph Rules.

### 14.5 Read the Rule-Tuning Report

The report has two parts. The first part gives the precision of each Rule:

| Rule | Name | Points | Decisions | Precision | Suggestion |
|---|---|---|---|---|---|
| R0 | New device / new country | 10 | 15 / 35 | 42.9% | Suggest 5 |
| R1 | Recovery takeover | 30 | 10 / 30 | 33.3% | Suggest 15 |
| R2 | Contact hijack | 20 | 10 / 30 | 33.3% | Suggest 10 |
| R3 | Payout redirect | 30 | 15 / 15 | 100.0% | |
| R4 | Cash-out | 40 | 17 / 17 | 100.0% | |
| R5 | Fast-track registration | 30 | 8 / 8 | 100.0% | |
| R6 | Register-and-sell | 30 | 8 / 8 | 100.0% | |
| G1 | Shared bank account | 30 | 5 / 5 | 100.0% | |
| G2 | Known-fraud proximity | 25 | 6 / 6 | 100.0% | |
| G3 | Ring indicator | 20 | 6 / 6 | 100.0% | |

The precision is the number of `ConfirmedFraud` Cases with the Rule ÷ all decided Cases with the Rule.
The report suggests a change only if the precision is less than 50% and the Rule has at least 5 decisions.
The suggestion is half of the points, to the nearest 5.

The second part is the What-if for each suggestion.
The What-if scores the full Dataset again, with only that one change:

| Metric | v2 | v2 with R2 at 10 |
|---|---|---|
| Detection rate | 22 / 30 (73.3%) | 22 / 30 (73.3%) |
| False-positive rate | 20 / 970 (2.1%) | 0 / 970 (0.0%) |
| Early-detection rate | 9 / 9 (100.0%) | 0 / 9 (0.0%) |

Read the What-if with care.
The change removes all 20 false positives.
But the change also stops all early detection. Vigil then finds the ATO fraud only after the bank change.
For an ATO fraudster in the Dataset, the email change then gives only 50 (Medium), not 60 (High).
Margaret stays High in the live system, because the graph Rules G2 and G3 add 45 points. Most fraudsters do not have this graph evidence.

A person must decide if this trade is acceptable. Vigil never makes this decision.

### 14.6 Clean up

1. Remove the `Investigation` section from `appsettings.Development.json`.
2. Make sure that `git status` shows no change to the file.
3. Commit the Rule-Tuning Report if you want to keep it.

## 15. Change a Rule safely

The Rule-Tuning Report never changes a Rules file.
A person does these steps:

1. Copy the current Rules file to a new version:

   ```bash
   cp config/rules.v2.json config/rules.v3.json
   ```

2. Open `config/rules.v3.json`.
3. Change `"version"` to `"v3"`.
4. Change the points of the Rule. For example, change R2 from `20` to `10`.
5. Compare the new version with the current version:

   ```bash
   dotnet run --project src/Vigil.Evaluation -- --rules-off config/rules.v2.json --rules-on config/rules.v3.json
   ```

6. Read the report.
7. Compare the detection rate, the false-positive rate and the early-detection rate.
8. If the new version is better, open `src/Vigil.Api/appsettings.json`.
9. Change `Rules:Path` to the new file:

   ```json
   "Rules": { "Path": "config/rules.v3.json" }
   ```

10. Start the API again. `GET /rules` now shows `"version": "v3"`.
11. Commit the new Rules file, the setting and the evaluation report together.

Each new Alert records `"rulesVersion": "v3"`.
Old Alerts keep `"v2"`. Vigil never scores them again with the new Rules.

Note: The API reads the Rules file only when it starts. If the file is not correct, the API does not start, and it shows the error.

## 16. Reset and recover

The [runbook](runbook.md) has the recovery procedures. Use it for these tasks:

| Task | Runbook section |
|---|---|
| Delete all data and start again | [Reset data](runbook.md#reset-data) |
| Build the graph again after Neo4j data is lost | [Reset data](runbook.md#reset-data), "Neo4j only" |
| Remove an old Dataset file after a Persona change | [Reset data](runbook.md#reset-data), "Stale Datasets" |
| Get the PostgreSQL connection string | [Find the Postgres connection string](runbook.md#find-the-postgres-connection-string) |
| Find the fix for an error | [Common failures](runbook.md#common-failures) |

To run the tests, read the [README](../README.md#tests).

# Vigil user guide

This guide tells you how to set up Vigil, run one fraud story through it, and measure how well it works.
It is for engineers and operators who are new to Vigil.

The guide has four parts:

- **Part A** tells you how to install and configure Vigil.
- **Part B** is a demo. It takes one fraud story from the first event to the decision of an analyst.
- **Part C** tells you how to measure the Rules and the AI agents, and what each measure means.
- **Part D** tells you how to recover, and gives the formats of the files and the fields.

The guide uses the terms in [GLOSSARY.md](../GLOSSARY.md).
A term with a capital letter, for example Alert, Case or Finding, has the meaning that the glossary gives.
For the design of each part, read [architecture.md](architecture.md).
If a step fails, read [runbook.md](runbook.md).

The example outputs in this guide come from real runs on 24 and 25 October 2026.
Identifiers such as Case IDs and ports are different on your computer.

## The shortest path

If you are new to Vigil, do these steps in sequence:

| Step | Do this | Time | Section |
|---|---|---|---|
| 1 | Install the tools and get the code | 15 minutes | [2](#2-before-you-start) |
| 2 | Set a budget alert, then create an Azure OpenAI model | 30 minutes | [3](#3-set-up-azure-openai) |
| 3 | Configure Vigil to use the model | 5 minutes | [4.4](#44-configure-the-api-for-azure-openai) |
| 4 | Start the system | 5 minutes | [5](#5-start-the-system) |
| 5 | Learn to send a request in Scalar | 5 minutes | [6](#6-use-scalar-to-call-the-api) |
| 6 | Run the demo, from the first event to the decision | 20 minutes | [7](#7-send-portal-events) to [12](#12-monitor-the-system) |
| 7 | Run the evaluation and read the report | 15 minutes | [14](#14-know-what-the-evaluation-measures) to [16](#16-read-the-evaluation-report) |

The demo uses a real AI model. It makes four Investigations, which cost approximately £0.02 in total.
If you cannot use Azure, skip steps 2 and 3. Vigil then uses the free Fake provider, which gives fixed answers ([section 13](#13-run-the-demo-again-or-without-azure)).

## Contents

**Part A: Set up and configure**

1. [What Vigil does](#1-what-vigil-does)
2. [Before you start](#2-before-you-start)
3. [Set up Azure OpenAI](#3-set-up-azure-openai)
4. [Configure Vigil](#4-configure-vigil)

**Part B: Run the demo from the start to the end**

5. [Start the system](#5-start-the-system)
6. [Use Scalar to call the API](#6-use-scalar-to-call-the-api)
7. [Send Portal Events](#7-send-portal-events)
8. [Read the Alert](#8-read-the-alert)
9. [Examine the knowledge graph](#9-examine-the-knowledge-graph)
10. [Follow the Case and the Investigation](#10-follow-the-case-and-the-investigation)
11. [Record the Analyst Decision](#11-record-the-analyst-decision)
12. [Monitor the system](#12-monitor-the-system)
13. [Run the demo again or without Azure](#13-run-the-demo-again-or-without-azure)

**Part C: Evaluate Vigil**

14. [Know what the evaluation measures](#14-know-what-the-evaluation-measures)
15. [Run the evaluation](#15-run-the-evaluation)
16. [Read the evaluation report](#16-read-the-evaluation-report)
17. [Run the evaluation with Azure OpenAI](#17-run-the-evaluation-with-azure-openai)
18. [Find Blind Spots with the Scenario Generator](#18-find-blind-spots-with-the-scenario-generator)
19. [Learn from Analyst Decisions](#19-learn-from-analyst-decisions)
20. [Change a Rule safely](#20-change-a-rule-safely)

**Part D: Recover and reference**

21. [Reset and recover](#21-reset-and-recover)
22. [File and field reference](#22-file-and-field-reference)

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

```text
Portal Event ─► PostgreSQL and graph ─► Rules ─► Alert ─► High? ─► Case ─► AI agents ─► Finding ─► analyst ─► decision
                                                                                                                  │
                         Rule-Tuning Report ◄──────────────────────────────────────────────────────────────────────┘
```

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
| A web browser | Current | Opens the dashboard, Scalar and Neo4j Browser |
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

## 3. Set up Azure OpenAI

**PAID STEPS.** The steps in this section create resources that cost money when you use them.

The AI agents of Vigil need a large language model (LLM). Vigil can use one of two LLM providers:

| Provider | What it does | Cost |
|---|---|---|
| AzureOpenAI | Sends each agent request to a real model in your Azure subscription. The model reads the evidence and writes its own Finding. | Approximately £0.003 for each Investigation |
| Fake | Calls no model. It gives fixed answers in the correct format. [Section 13.1](#131-know-what-the-fake-provider-does) tells you more. | Free |

Use AzureOpenAI for the demo, because it shows what the agents really do.
Use the Fake provider only if you cannot use Azure, or to test the system.

The setting in Git is Fake, so that Vigil can start without an Azure key.
This section and [section 4.4](#44-configure-the-api-for-azure-openai) change it to AzureOpenAI on your computer.

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
| `Rules:Path` | `config/rules.v2.json` | `appsettings.json` | You deploy a new Rules Version ([section 20](#20-change-a-rule-safely)) |
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
[Section 21](#21-reset-and-recover) tells you how.

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

Do these steps only after [section 3](#3-set-up-azure-openai).

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

# Part B: Run the demo from the start to the end

Part B is a demo. It tells one fraud story from the first event to the decision of the analyst.
Each section of the demo has the same parts:

- **In the story** tells you what happens to the people in the story.
- **Do this** gives the steps.
- **What you see** gives the result.
- **Behind the scenes** tells you what Vigil does, step by step.

Part B uses Azure OpenAI. Do [section 3](#3-set-up-azure-openai) and [section 4.4](#44-configure-the-api-for-azure-openai) first.
The demo makes four Investigations, which cost approximately £0.02 in total.
If you cannot use Azure, use the Fake provider. [Section 13](#13-run-the-demo-again-or-without-azure) tells you what changes.

Start with empty data. If you did the demo before, read [13.3](#133-run-the-demo-again) first.

### The story

Margaret is an investor. For many months, she logs in to the investor portal from her laptop in the UK.
Then a fraudster gets the password of her email account.
The fraudster uses her email to take over her portal Account.

The fraudster uses one device, `D-77`, in Romania.
Before Margaret, the fraudster used `D-77` on three other Accounts.
Vigil remembers each of these Accounts. This memory helps Vigil to find the attack on Margaret.

For the full story, read [PROJECT.md Section 3.1](../PROJECT.md#31-fraud-case--margaret).

### The people in the story

| Person | What happens | What Vigil must do |
|---|---|---|
| Derek | 1 September. The fraudster resets Derek's password and enrols a new MFA phone from `D-77`. Then the fraudster stops. | Remember `D-77` |
| Priya | 7 September. The fraudster takes over Priya's Account from `D-77` and changes her bank details. On 14 September, Priya reports the fraud, and her Account becomes a Confirmed Fraud. | Know that `D-77` is a fraud device |
| Lena | 11 September. The fraudster resets Lena's password and changes her phone number from `D-77`. | See that three Accounts now share `D-77` |
| Margaret | 21 September. In 10 minutes, the fraudster resets her password, enrols a new MFA phone and changes her email. The next day, the fraudster changes her bank details. | Find the takeover before the bank change |
| Mr Patel | A genuine Holder moves house. He changes his address and his phone from his usual laptop. | Do nothing. Give no false alarm. |

The dates come from the test data.
The simulator sends all the events in some seconds, but Vigil uses the date of each event.

### The demo at a glance

| Section | You do | You see | Behind the scenes, Vigil |
|---|---|---|---|
| [7](#7-send-portal-events) | Start the simulator | The API accepts the events of five Scenarios | Stores the events, links the Accounts in the graph, scores them, and opens Cases |
| [8](#8-read-the-alert) | Get the Alerts in Scalar | Margaret has a Score of 135, Critical | Applies the Rules to her events and to the graph |
| [9](#9-examine-the-knowledge-graph) | Open Neo4j Browser | Four Accounts share `D-77` | Connects Accounts that use the same identifier |
| [10](#10-follow-the-case-and-the-investigation) | Get the Case in Scalar | The Finding says "likely account takeover" | Runs two AI agents on masked data, then stops and waits |
| [11](#11-record-the-analyst-decision) | Send the decision in Scalar | The Case closes with a 7-day block on bank changes | Records the decision, the Intervention and the audit trail |
| [12](#12-monitor-the-system) | Open the traces | One trace from the event to the agents | Records traces, metrics and logs for each step |

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

These addresses do not change:

| Address | What |
|---|---|
| http://localhost:5107/scalar | Scalar, the API reference. You use it to send each request in this guide. |
| http://localhost:7474 | Neo4j Browser. The user is `neo4j`. The password is the secret `Parameters:neo4j-password`. |
| http://localhost:5107 | The API itself |

[Section 6](#6-use-scalar-to-call-the-api) tells you how to make sure that the API answers.

If the `api` resource stops after it starts, read its console log.
When a setting for Azure OpenAI is missing, the API stops and shows the name of the setting.

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

## 6. Use Scalar to call the API

Scalar is a web page that shows each operation of the API.
You can send a request from the page and see the response. You do not need a terminal.
The API shows Scalar only in the Development environment. The AppHost uses this environment.

### 6.1 Send your first request

**Do this.**

1. Open http://localhost:5107/scalar.
2. In the left panel, find the operation **GET /rules**. Its title is "The active rule set and its version".
3. Select the operation. The page shows its description and an example request.
4. Select **Test Request**. A request window opens.
5. Select **Send**.

**What you see.** The response panel shows the status `200` and the Rules that are in use:

```json
{
  "version": "v2",
  "riskPeriodDays": 7,
  "bands": { "medium": 30, "high": 60, "critical": 90 },
  ...
}
```

If you see `"version": "v2"`, the API works.

### 6.2 Fill in a parameter

Some operations need a value before you send them.
For example, **GET /alerts/{id}** needs the ID of an Alert.

1. Select **Test Request**.
2. Find the **Path Parameters** section in the request window.
3. Type the value in the `id` field.
4. Select **Send**.

An operation can also have optional **Query Parameters**, for example `band` or `status`. Leave a field empty if you do not need it.

An operation that sends data, for example **POST /cases/{id}/decision**, has a **Body** section.
Scalar puts an example body in it. Replace the example with the JSON that this guide gives.

Tip: Copy each ID from a response before you close the request window. You need the IDs in later steps.

### 6.3 The operations that the demo uses

The left panel puts the operations in groups. Most of them are in the group **Vigil.Api**.
Use the search box at the top of the panel to find an operation by its path.

| Operation | Group in Scalar | What it gives | Section |
|---|---|---|---|
| **GET /rules** | Vigil.Api | The Rules that are in use | [6.1](#61-send-your-first-request) |
| **GET /alerts** | Vigil.Api | All Alerts, the riskiest first. Optional filter: `band`. | [8.1](#81-get-the-alerts) |
| **GET /alerts/{id}** | Vigil.Api | One Alert with its Rule Hits | [8.3](#83-read-margarets-alert) |
| **GET /accounts/{accountId}/timeline** | Vigil.Api | All events of one Account, the oldest first | [10.4](#104-examine-the-evidence) |
| **GET /cases** | Vigil.Api | All Cases, the worst first. Optional filter: `status`. | [10.2](#102-find-the-case) |
| **GET /cases/{id}** | Vigil.Api | One Case with its Finding, decision and Intervention | [10.3](#103-read-the-finding) |
| **POST /cases/{id}/decision** | CaseEndpoints | Records the decision of the analyst | [11.1](#111-send-the-decision) |
| **GET /cases/{id}/audit** | Vigil.Api | The audit trail of one Case | [11.3](#113-read-the-audit-trail) |
| **POST /events** | EventEndpoints | Receives Portal Events. The simulator uses it. | [7](#7-send-portal-events) |
| **POST /accounts/{accountId}/confirmed-fraud** | Vigil.Api | Marks an Account as Confirmed Fraud. The simulator uses it. | [7.3](#73-behind-the-scenes-one-request-to-the-api) |

## 7. Send Portal Events

**In the story:** the fraudster attacks the four Accounts. Mr Patel moves house. The portal tells Vigil about each action.

On your computer, there is no real portal. The simulator acts as the portal.
It reads each Scenario file in `scenarios/` and sends its events to the API.
A Scenario is a JSON file that tells the story of one Account.
To read or write a Scenario file, use [the Scenario file reference](#221-the-scenario-file).

### 7.1 Send the Scenarios

**Do this.** Use one of the two procedures.

**Procedure 1: the dashboard**

1. On the **Resources** page, find `simulator`.
2. Select **Start**.
3. To see the output, select the `simulator` resource and open its console log.

**Procedure 2: a terminal**

The simulator cannot find the API by itself outside the AppHost.
Give it the API address with an environment variable:

```bash
services__api__http__0=http://localhost:5107 dotnet run --project src/Vigil.Simulator
```

**What you see.** The output has one line for each Scenario:

```text
info: Simulator[0]
      Posted derek-reset: {"accepted":15,"alreadyIngested":0}
info: Simulator[0]
      Posted priya-ato: {"accepted":17,"alreadyIngested":0}
info: Simulator[0]
      Seeded ACC-PRIYA as Confirmed Fraud, decided 09/14/2026 11:00:00 +00:00
info: Simulator[0]
      Posted lena-reset: {"accepted":15,"alreadyIngested":0}
info: Simulator[0]
      Posted margaret-ato: {"accepted":17,"alreadyIngested":0}
info: Simulator[0]
      Posted patel-house-move: {"accepted":15,"alreadyIngested":0}
```

Each line tells you how many events Vigil accepted.
Margaret has 17 events: 13 normal logins from her laptop, one each week, and the 4 events of the fraudster.

To send one Scenario only, add `-- --scenario scenarios/margaret-ato.json`.

You can send a Scenario again. Vigil does not store an event two times.
The second time, the output shows `"accepted":0` and a number in `alreadyIngested`.

### 7.2 Behind the scenes: what each Scenario teaches Vigil

The simulator sends the Scenarios in the sequence of their dates.
Each Scenario adds a new fact about `D-77`:

| Order | Scenario | What Vigil learns | What Vigil does |
|---|---|---|---|
| 1 | Derek | `D-77` is a new device in a new country for Derek. A password reset and an MFA change follow quickly. | Gives Derek an Alert with a Score of 40 (Medium). No Case yet. |
| 2 | Priya | The same attack on Priya, then a bank change. Then the simulator marks Priya as a Confirmed Fraud. | Opens a Case for Priya. Derek now shares `D-77` with a Confirmed Fraud, so his Score goes up to High, and Vigil opens a Case for him too. |
| 3 | Lena | A third Account uses `D-77`. Three Accounts on one device are a sign of a fraud ring. | Opens a Case for Lena. Scores Derek and Priya again, because they share `D-77` with Lena. |
| 4 | Margaret | The fraudster takes over Margaret's Account from `D-77`. | Opens a Case for Margaret |
| 5 | Mr Patel | Normal changes from his usual laptop | Nothing. No Rule agrees with his events. |

At the end, four Accounts have a Case: Margaret, Priya, Derek and Lena.

### 7.3 Behind the scenes: one request to the API

The simulator sends each Scenario to the API in one request, **POST /events**.
The API does these steps in one database transaction:

1. **Lock.** Vigil locks each Account in the request. Two requests cannot change one Account at the same time.
2. **Store.** Vigil writes the events to PostgreSQL. It ignores each event that it already has.
3. **Link.** Vigil writes the events to the Neo4j graph. The graph links each Account to its devices, emails, phones and bank accounts.
4. **Find the Neighbours.** A Neighbour is another Account that shares a device, an email, a phone or a bank account.
5. **Score.** Vigil applies the Rules to each Account in the request and to each Neighbour. The result is an Alert.
6. **Open Cases.** When an Alert reaches the High band, Vigil opens a Case.
7. **Commit.** Vigil saves all the changes together.
8. **Queue.** Vigil puts each new Case in the Investigation queue. [Section 10](#10-follow-the-case-and-the-investigation) tells you what happens next.

If one step fails, Vigil saves nothing. The simulator can then send the request again.

Step 5 is the reason that Derek's Score goes up when Priya and Lena arrive.
Vigil scores an Account again each time that a Neighbour changes.

To mark Priya as a Confirmed Fraud, the simulator uses a different request, **POST /accounts/{accountId}/confirmed-fraud**.
Vigil records the Confirmed Fraud in PostgreSQL and in the graph. Then it scores the Neighbours of Priya again.

The code is in `src/Vigil.Api/Events/Ingestion.cs`.

## 8. Read the Alert

**In the story:** the Rules see the takeover. Margaret's Account reaches the High band before the fraudster changes her bank details.

An Alert is the result of the Rules for one Account.
It has a Score, a Risk Band, and the Rule Hits that make the Score.

### 8.1 Get the Alerts

**Do this.**

1. In Scalar, select **GET /alerts**.
2. Select **Test Request**.
3. Select **Send**.

**What you see.** The response has four Alerts, the riskiest first. This is the first one:

```json
{
  "id": "01a174ea-fd9c-7781-a319-618a6940e0a2",
  "accountId": "ACC-MARGARET",
  "score": 135,
  "band": "Critical",
  "rulesVersion": "v2",
  "createdAt": "2026-10-25T21:15:19.836053+00:00"
}
```

The four Alerts are these:

| Account | Score | Band | Why |
|---|---|---|---|
| ACC-MARGARET | 135 | Critical | The full takeover, and the link to `D-77` |
| ACC-PRIYA | 110 | Critical | The full takeover, and three Accounts on `D-77` |
| ACC-DEREK | 85 | High | A reset and an MFA change from `D-77`, and the links to Priya and Lena |
| ACC-LENA | 75 | High | A reset and a phone change from `D-77`, and the links to Priya and Derek |

Mr Patel has no Alert. His events agree with no Rule.

Copy the `id` of Margaret's Alert. You need it in 8.3.
To get only one band, type a value in the `band` field, for example `High`.

### 8.2 Know how Margaret's Score went up

Vigil adds the points of each Rule Hit in the sequence of event time:

| Event of the fraudster | What the Rules see | Points | Score | Band |
|---|---|---|---|---|
| `PasswordReset` from `D-77` in Romania | R0: a new device and a new country. G2: `D-77` belongs to a Confirmed Fraud. G3: four Accounts share `D-77`. | +10, +25, +20 | 55 | Medium |
| `MfaEnrolled`, 4 minutes later | R1: a reset, then a new MFA phone, in less than 1 hour | +30 | 85 | **High. Vigil opens a Case.** |
| `EmailChanged`, 10 minutes after the reset | R2: a reset, then a contact change, in less than 24 hours | +20 | 105 | Critical |
| `BankDetailsChanged`, the next day | R3: a contact change, then a bank change, in less than 72 hours | +30 | 135 | Critical |

The Alert reached High before the bank change. This is the purpose of Vigil.

Note: The simulator sent all of Margaret's events in one request. Thus Vigil calculated the full Score of 135 in one step.
On a live portal, the events arrive one at a time, and the Score goes up as the table shows.

These rules apply to the Score:

- Each Rule adds its points only one time in an Alert.
- The Score never decreases. A band can only go up.
- A Rule Hit stays in the same Alert if it occurs at most 7 days after the previous hit. A longer gap starts a new Alert.

The bands start at these Scores: Medium 30, High 60, Critical 90.
The Rules, their points and their time limits are in `config/rules.v2.json`.
To read this file, use [the Rules file reference](#222-the-rules-file).

### 8.3 Read Margaret's Alert

**Do this.**

1. In Scalar, select **GET /alerts/{id}**.
2. Select **Test Request**.
3. In **Path Parameters**, type the Alert ID from 8.1 in the `id` field.
4. Select **Send**.

**What you see.** This is the response, with two Rule Hits removed so that it is shorter:

```json
{
  "id": "01a174ea-fd9c-7781-a319-618a6940e0a2",
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

Each Rule Hit tells the analyst three things: which Rule agreed, which events caused it, and why.

| Field | Meaning |
|---|---|
| `ruleId` and `points` | The Rule and the points that it adds to the Score |
| `evidenceEventIds` | The Portal Events that caused the hit. An analyst can find each one in the timeline ([10.4](#104-examine-the-evidence)). |
| `triggeredAt` | The event time of the hit, not the time when Vigil calculated it |
| `explanation` | A sentence that tells why the Rule agrees with the events |

The `rulesVersion` tells you which Rules file made the Alert.
Vigil never calculates an old Alert again with a newer Rules file.

## 9. Examine the knowledge graph

**In the story:** two Rules, G2 and G3, gave Margaret 45 points. These points did not come from her own events. They came from the graph.
This section shows you the graph evidence.

The graph is a map of Accounts and the identifiers that they use.
An identifier is a device, an email, a phone or a bank account.
A line connects each Account to each identifier that it uses.
When two Accounts use the same identifier, the graph connects them through that identifier.

### 9.1 Open Neo4j Browser

1. Open http://localhost:7474.
2. Type the user `neo4j`.
3. Type the password. To find it, do `dotnet user-secrets list --project src/Vigil.AppHost` and read `Parameters:neo4j-password`.
4. Select **Connect**.

### 9.2 See the Accounts that share D-77

**Do this.** Type this query and select **Run**:

```cypher
MATCH (a:Account)-[:USED_DEVICE]->(d:Device {value: 'D-77'})
OPTIONAL MATCH (c:Case {outcome: 'ConfirmedFraud'})-[:CONCERNS]->(a)
RETURN a.accountId AS account, c IS NOT NULL AS confirmedFraud
```

**What you see.** The result shows four Accounts: ACC-MARGARET, ACC-PRIYA, ACC-DEREK and ACC-LENA.
ACC-PRIYA shows `confirmedFraud = true`.

To see the graph as a picture, type this query:

```cypher
MATCH p = (:Account {accountId: 'ACC-MARGARET'})-[]->(id)<-[]-(:Account)
RETURN p
```

The picture shows Margaret's Account, the `D-77` node, and the three other Accounts that use `D-77`.

### 9.3 Behind the scenes: the graph Rules

After each request, Vigil asks the graph three questions about each Account.
Each "yes" is a Rule Hit:

| Rule | Points | The question |
|---|---|---|
| G1 Shared bank account | 30 | Does the Account pay to a bank account that a different Holder also pays to? |
| G2 Known-fraud proximity | 25 | Does the Account share a device, a bank account, an email or a phone with a Confirmed Fraud Account? |
| G3 Ring indicator | 20 | Do at least 3 Accounts, this one included, share one device, bank account, email or phone? |

For Margaret, G2 is "yes", because she shares `D-77` with Priya.
G3 is also "yes", because four Accounts share `D-77`.

IP addresses and postal addresses are in the graph, but they never link Accounts.
Many families, offices and mobile networks share them.

For more Cypher, read [graph-primer.md](graph-primer.md).

## 10. Follow the Case and the Investigation

**In the story:** Vigil opened a Case for Margaret. Two AI agents examine the Case and write a report for the analyst.
The agents do not decide. They only recommend.

### 10.1 Behind the scenes: from the queue to the analyst

A background worker in the API takes each Case from the queue, one at a time.
The worker runs an Investigation. This is a Microsoft Agent Framework workflow.

```text
Case opened ─► queue ─► budget check ─► Enrich ─► Investigator ─► evidence check ─► Reviewer ─► Await Approval
                                                       ▲                                 │
                                                       └──── rejected: one more try ─────┘
```

The worker does these steps:

1. **Check the budget.** The worker stops if Investigations are off, or if 20 Investigations started on this UTC day.
2. **Start.** The worker sets the Case to `Investigating`.
3. **Enrich.** Code writes a short brief about the Alert: the Score, the Rule Hits and their events.
4. **Mask.** Code replaces each real identifier in the brief with a Pseudonym. For example, `ACC-MARGARET` becomes `ACCOUNT-1`.
5. **Investigate.** The Investigator agent reads the brief and uses its tools to get more evidence. Then it writes a Finding.
6. **Check the evidence.** Code makes sure that the Finding cites only evidence that the tools gave.
7. **Review.** The Reviewer agent reads the Finding and the evidence. It approves or rejects the Finding.
8. **Try again one time.** If step 6 or step 7 rejects the Finding, the Investigator gets the reasons and writes a new Finding.
9. **Stop and wait.** The workflow saves its state in PostgreSQL. The Case becomes `AwaitingApproval`.

Nothing more happens until an analyst decides.

These details are important for the analyst:

- **The agents never see a real identifier.** `D-77` becomes `DEVICE-2`, for example. The numbers start again in each Investigation.
- **Each claim has evidence.** Each event that a tool gives has an Evidence Ref, for example `E-3`. Each graph fact has a ref, for example `G-2`.
- **A second failure gives a warning.** If the new Finding also fails a check, Vigil keeps it and sets `reviewFlagged` to `true`.
- **An LLM failure does not lose the Case.** The Case goes to the analyst with `investigationFailed: true` and no Finding.
- **The budget can skip a Case.** The Case then stays `Open` with `notInvestigated: true`. An analyst can examine it without a Finding.

The Investigator has three tools. Each tool can read only the Account of this Case:

| Tool | Gives |
|---|---|
| `GetAccountTimeline(days)` | The events of the Account in the last `days` days, at most 90 |
| `QueryGraphNeighbourhood()` | Each shared identifier, the other Accounts that use it, and if they are Confirmed Fraud |
| `GetHolderBaseline()` | The usual countries, devices and logins each week, from before the suspicious events |

These files and settings control the Investigation:

| What | Where |
|---|---|
| The instructions of the agents | `config/prompts/investigator.v1.md` and `config/prompts/reviewer.v1.md`. The file name is the Prompt Version. |
| The budget | `Investigation:Enabled` and `Investigation:DailyLimit` in `src/Vigil.Api/appsettings.json` ([4.2](#42-all-the-settings)) |
| The workflow | `src/Vigil.Agents/Workflow/InvestigationWorkflow.cs` |
| The worker and the budget check | `src/Vigil.Api/Cases/InvestigationWorker.cs` |

### 10.2 Find the Case

**Do this.**

1. In Scalar, select **GET /cases**.
2. Select **Test Request**.
3. In **Query Parameters**, type `AwaitingApproval` in the `status` field.
4. Select **Send**.

**What you see.** The response has four Cases, the worst first. This is Margaret's Case:

```json
{
  "id": "01a174ea-fd9c-7d50-93cc-2786dbbaf8a0",
  "alertId": "01a174ea-fd9c-7781-a319-618a6940e0a2",
  "accountId": "ACC-MARGARET",
  "status": "AwaitingApproval",
  "band": "Critical",
  "score": 135,
  "openedAt": "2026-10-25T21:15:19.836053+00:00",
  "notInvestigated": false
}
```

Copy the `id` of Margaret's Case. You need it in the next steps.

If the response has fewer than four Cases, wait some seconds and select **Send** again.
The worker investigates one Case at a time. With Azure OpenAI, each Investigation takes approximately 10 to 20 seconds.

A Case goes through these statuses: `Open`, `Investigating`, `AwaitingApproval`, `Closed`.
[The Case statuses reference](#223-case-statuses) tells you what each status means.

### 10.3 Read the Finding

**Do this.**

1. In Scalar, select **GET /cases/{id}**.
2. Select **Test Request**.
3. In **Path Parameters**, type the Case ID from 10.2 in the `id` field.
4. Select **Send**.

**What you see.** This is the response from a real run with `gpt-4.1-mini`, with some refs removed so that it is shorter:

```json
{
  "id": "01a174ea-fd9c-7d50-93cc-2786dbbaf8a0",
  "accountId": "ACC-MARGARET",
  "status": "AwaitingApproval",
  "reviewFlagged": false,
  "investigationFailed": false,
  "promptVersion": "investigator.v1+reviewer.v1",
  "finding": {
    "verdict": "LikelyAccountTakeover",
    "confidence": "High",
    "summary": "ACC-MARGARET shows a sudden shift from usual logins in GB on D-MARGARET-LAPTOP to a new device D-77 in RO, with a password reset, MFA enrollment, and email change within 10 minutes, followed by a bank detail change 28h 50m later. D-77 is shared by four accounts including ACC-DEREK, ACC-LENA, and confirmed fraud ACC-PRIYA, indicating a high fraud risk environment. The bank account changed is unique to ACC-MARGARET. This pattern strongly suggests an account takeover rather than genuine activity or registration fraud.",
    "recommendation": "BlockBankChanges",
    "evidenceRefs": [
      { "ref": "E-3", "eventIds": ["20b00859-9b5e-52c2-a911-57c037415019"] },
      { "ref": "E-4", "eventIds": ["96d126f5-e50f-5692-951f-49e0ff7a8e41"] },
      { "ref": "E-5", "eventIds": ["8c05be13-887b-589b-a83f-4ffed06551d5"] },
      { "ref": "E-6", "eventIds": ["c2de8b21-1cae-502f-8c04-408c61319664"] },
      { "ref": "G-6", "eventIds": ["20b00859-9b5e-52c2-a911-57c037415019", "cbfb76d0-806a-5ba8-940e-f6040186bef6",
                                   "eb0e5cec-0980-5394-83c8-f0f879fe8ebf", "3067743d-ea92-5009-8aa9-7b5be6470f6a"] }
    ]
  },
  "decision": null,
  "intervention": null
}
```

Read the Finding like this:

- The `verdict` tells you what the agents think happened. Here, it is a likely account takeover.
- The `summary` tells you why. The model found the new device, the fast changes, and the link to Priya through `D-77`.
- The `recommendation` tells you what the agents suggest. Here, it is a block on bank changes.
- Each ref in `evidenceRefs` points to real events. Use them to examine each claim.
- The `summary` shows real identifiers, but the model saw only Pseudonyms such as `ACCOUNT-1`. Vigil puts the real identifiers back after the model answers.
- `reviewFlagged` is `false`, so the Reviewer approved the Finding. If it is `true`, read the Finding with more care.

The model writes a different Finding for each Case, and the words can change from run to run.
With the Fake provider, each Case gets the same fixed Finding ([13.1](#131-know-what-the-fake-provider-does)).
[The Finding reference](#224-the-finding) gives all the fields and their values.

### 10.4 Examine the evidence

An analyst does not trust the Finding without a check.
This step shows the events behind one Evidence Ref.

**Do this.**

1. In Scalar, select **GET /accounts/{accountId}/timeline**.
2. Select **Test Request**.
3. In **Path Parameters**, type `ACC-MARGARET` in the `accountId` field.
4. Select **Send**.

**What you see.** The response has 17 events, the oldest first. The last event is the bank change:

```json
{
  "eventId": "c2de8b21-1cae-502f-8c04-408c61319664",
  "accountId": "ACC-MARGARET",
  "holderId": "HOLDER-MARGARET",
  "type": "BankDetailsChanged",
  "occurredAt": "2026-09-22T14:00:00+00:00",
  "deviceId": "D-77",
  "ipAddress": "198.51.100.77",
  "country": "RO",
  "channel": "web",
  "attributes": { "NewBankAccountHash": "BA-9f31" }
}
```

Compare each `eventId` in the Finding with the timeline.
For example, ref `E-3` points to event `20b00859-...`. That is the password reset from `D-77` in Romania.

### 10.5 See what the Investigation cost

**Do this.**

1. In Scalar, select **GET /cases/{id}/audit**.
2. Select **Test Request**.
3. In **Path Parameters**, type Margaret's Case ID in the `id` field.
4. Select **Send**.

**What you see.** The response has one entry for each agent. Read `model`, `inputTokens` and `outputTokens`:

| Agent | Model | Input tokens | Output tokens |
|---|---|---|---|
| Investigator | `gpt-4.1-mini-2025-04-14` | 6,838 | 469 |
| Reviewer | `gpt-4.1-mini-2025-04-14` | 1,978 | 161 |

At the default prices, this Investigation cost approximately £0.003.
The Investigator uses more tokens, because its tools add the timeline, the graph and the baseline to the conversation.
[Section 12](#12-monitor-the-system) shows the same cost in the dashboard.

## 11. Record the Analyst Decision

**In the story:** an analyst reads the Finding and the evidence. The analyst phones Margaret. Margaret says that she did not make the changes.
The analyst confirms the fraud and blocks bank changes on her Account.

### 11.1 Send the decision

**Do this.**

1. In Scalar, select **POST /cases/{id}/decision**. It is in the group **CaseEndpoints**.
2. Select **Test Request**.
3. In **Path Parameters**, type Margaret's Case ID in the `id` field.
4. In **Body**, replace the example with this JSON:

   ```json
   {
     "outcome": "ConfirmedFraud",
     "action": "BlockBankChanges",
     "analystId": "analyst-7",
     "notes": "Holder confirmed by phone it was not her."
   }
   ```

5. Select **Send**.

| Field | Values |
|---|---|
| `outcome` | `ConfirmedFraud` or `FalsePositive` |
| `action` | `BlockBankChanges`, `ContactHolder` or `NoAction`. It can be different from the recommendation. |
| `analystId` | The name or ID of the analyst |
| `notes` | Free text |

**What you see.** The status is `200`. The response is the closed Case. The new parts of the Case are these:

```json
"status": "Closed",
"decision": {
  "outcome": "ConfirmedFraud",
  "action": "BlockBankChanges",
  "analystId": "analyst-7",
  "notes": "Holder confirmed by phone it was not her.",
  "decidedAt": "2026-10-25T21:15:57.983925+00:00"
},
"intervention": {
  "kind": "BlockBankChanges",
  "until": "2026-11-01T21:15:57.983925+00:00",
  "recordedAt": "2026-10-25T21:15:57.983925+00:00"
}
```

The block lasts 7 days. The setting `Intervention:BlockBankChangesDays` controls this.

Select **Send** a second time. The status is now `409`.
The API refuses a decision on a Case that is not in `AwaitingApproval`, and it changes nothing.

### 11.2 Behind the scenes: what the decision does

The decision starts the saved workflow again at its last step, Act. No LLM call occurs.
Act does these tasks in one transaction:

1. It records the Analyst Decision.
2. It records the Intervention. For `BlockBankChanges`, the Intervention has an end date.
3. It writes two Audit Entries: `DecisionRecorded` and `ActionTaken`.
4. It closes the Case.
5. For `ConfirmedFraud`, it makes the Account a Confirmed Fraud. Vigil adds this fact to the graph.
6. It scores the Neighbours of the Account again.

After step 5, Margaret's Account counts as known fraud, as Priya's Account does.
Each Account that shares an identifier with Margaret can now get the G2 Rule Hit.

Vigil records the Intervention, but it does not stop bank changes itself.
The portal must read the Intervention and refuse the bank change.

### 11.3 Read the audit trail

**Do this.**

1. In Scalar, select **GET /cases/{id}/audit**.
2. Select **Test Request**.
3. In **Path Parameters**, type Margaret's Case ID in the `id` field.
4. Select **Send**.

**What you see.** The response has four Audit Entries. Together, they tell who did what, and with which version:

| Kind | Actor | Records |
|---|---|---|
| `InvestigationCompleted` | `vigil-investigator` | Model, Prompt Version, Rules Version, cited refs with their events, tokens |
| `ReviewCompleted` | `vigil-reviewer` | Model, Prompt Version, the review result, tokens |
| `DecisionRecorded` | `analyst-7` | The outcome and the notes |
| `ActionTaken` | `analyst-7` | The Intervention |

These are the first and the last entries, with the refs removed:

```json
{
  "kind": "InvestigationCompleted",
  "actor": "vigil-investigator",
  "model": "gpt-4.1-mini-2025-04-14",
  "promptVersion": "investigator.v1",
  "rulesVersion": "v2",
  "inputTokens": 6838,
  "outputTokens": 469,
  "detail": "LikelyAccountTakeover, High confidence, recommends BlockBankChanges",
  "at": "2026-10-25T21:15:43.274617+00:00"
},
...
{
  "kind": "ActionTaken",
  "actor": "analyst-7",
  "model": null,
  "promptVersion": "investigator.v1+reviewer.v1",
  "rulesVersion": "v2",
  "inputTokens": 0,
  "outputTokens": 0,
  "detail": "BlockBankChanges until 2026-11-01T21:15:57.9839250+00:00",
  "at": "2026-10-25T21:15:57.983925+00:00"
}
```

The analyst entries have no model, because no LLM takes part in the decision.

Vigil never changes an Audit Entry after it writes it.

### 11.4 Make sure that a restart does not lose a Case

The workflow can wait days for an analyst. This test shows that a restart does not lose the waiting Case.
Use Derek's Case, because it is still in `AwaitingApproval`.

1. In the dashboard, find the `api` resource.
2. Select **Stop**.
3. Wait until the API stops.
4. Select **Start**. Wait until the state is **Running**.
5. In Scalar, get Derek's Case with **GET /cases/{id}**. The status is still `AwaitingApproval`.
6. Send a decision with **POST /cases/{id}/decision**. The status is `200`.

The workflow keeps its state in the PostgreSQL table `workflow_checkpoints`.
The decision continues the workflow from that state.

## 12. Monitor the system

**In the story:** the demo is complete. Now you show how an engineer follows Margaret's Case through each part of the system.

### 12.1 See one trace from the event to the agents

**Do this.**

1. In the dashboard, select **Traces**.
2. Find the trace for `POST /events` that sent Margaret's events.
3. Open it.

**What you see.** One trace shows the full journey, from the request to the agents:

| Span | What |
|---|---|
| `POST /events` | The request from the simulator |
| `graph.project` | The write to Neo4j |
| `detect` and `graph.rules` | The scoring of each Account |
| `investigate case` | The Investigation. The worker runs it later, but it joins the same trace. |
| `invoke_agent Investigator(vigil-investigator)` | The Investigator, with `chat` and `execute_tool QueryGraphNeighbourhood` spans |
| `invoke_agent Reviewer(vigil-reviewer)` | The Reviewer |

The decision has its own trace, under `POST /cases/{id}/decision`.
Its `decide case` span has a link to the `investigate case` span.

### 12.2 See the metrics

Select **Metrics**, and then the `api` resource.

| Metric | What |
|---|---|
| `vigil.llm.tokens` | Tokens, by agent and by direction (input or output) |
| `vigil.llm.cost` | Estimated £ for each Investigation |
| `vigil.investigation.duration` | Seconds from the start of the Investigation to Await Approval |

With Azure OpenAI, `vigil.llm.cost` shows approximately £0.003 for each Investigation.
With the Fake provider, the tokens and the cost are 0.

### 12.3 See the logs

Select **Console** or **Structured** logs for the `api` resource.
Each LLM call writes one line from `TokenLoggingChatClient`, with the model, the tokens and the estimated £.
You see the cost immediately. The Azure cost view can be up to 24 hours late.

## 13. Run the demo again or without Azure

### 13.1 Know what the Fake provider does

The Fake provider acts as an LLM, but it calls no model. It costs nothing and it needs no key.
The automated tests use it, so that they never call a real model.

The Fake provider finds out which agent sends a request from the format of the answer that the agent asks for.
Then it gives a fixed answer:

| Agent | The answer of the Fake provider |
|---|---|
| Investigator | It calls the tool `QueryGraphNeighbourhood` one time. Then it writes the same Finding for each Case: `LikelyAccountTakeover`, `High` confidence, `BlockBankChanges`. |
| Reviewer | It always approves the Finding. |
| Judge | It gives the score 3 for each criterion. |
| Scenario Generator | It writes one fixed Scenario, `fake-late-mfa-slow-cash-out`. |

The fixed Finding cites only Evidence Refs that exist. Thus it passes the evidence check.
Each answer reports 0 tokens, so the cost is always £0.

The Fake provider does not read the evidence, and it does not think.
It gives the same verdict to a fraudster and to a genuine Holder.
Use it to make sure that each part of the system works: the workflow, the tools, the evidence check, the audit trail and the traces.
Do not use it to judge the quality of the agents.

The code is in `src/Vigil.Agents/Llm/FakeChatClient.cs`.

### 13.2 Run the demo with the Fake provider

**Do this.**

1. Stop the AppHost with Ctrl+C.
2. Open `src/Vigil.Api/appsettings.Development.json`.
3. Set `"Provider"` to `"Fake"`. You can keep the endpoint and the deployment name. The Fake provider ignores them.
4. Start the AppHost again.
5. Do sections [7](#7-send-portal-events) to [12](#12-monitor-the-system).

**What you see.** The events, the Alerts, the graph and the decision are the same as with Azure OpenAI. Only the agent results change:

```json
"finding": {
  "verdict": "LikelyAccountTakeover",
  "confidence": "High",
  "summary": "Scripted Finding from the Fake provider: the activity on ACC-MARGARET came from a device and country the Holder does not use, and that device is shared with a Confirmed Fraud Account.",
  "recommendation": "BlockBankChanges",
  "evidenceRefs": [
    { "ref": "E-3", "eventIds": ["20b00859-9b5e-52c2-a911-57c037415019"] },
    { "ref": "E-4", "eventIds": ["96d126f5-e50f-5692-951f-49e0ff7a8e41"] },
    ...
  ]
}
```

In the audit trail, `model` is `fake-model`, and `inputTokens` and `outputTokens` are 0.

To go back to the real model, set `"Provider"` to `"AzureOpenAI"` and start the AppHost again.

### 13.3 Run the demo again

Vigil ignores events that it already has. The event IDs come from the Scenario name.
Thus, if you send the same Scenarios again, Vigil opens no new Case. Use one of these two procedures.

**Procedure 1: start with empty data**

1. Reset the data. Use the procedure in the [runbook](runbook.md#reset-data).
2. Do sections [7](#7-send-portal-events) to [12](#12-monitor-the-system) again.

**Procedure 2: send a copy of Margaret's story**

This procedure keeps the old data and adds a new Account.

1. Make a copy of Margaret's Scenario with a new name and a new Account:

   ```bash
   sed -e 's/"margaret-ato"/"margaret-ato-2"/' -e 's/ACC-MARGARET/ACC-MARGARET-2/' \
       -e 's/HOLDER-MARGARET/HOLDER-MARGARET-2/' scenarios/margaret-ato.json > margaret-2.json
   ```

2. Send the copy:

   ```bash
   services__api__http__0=http://localhost:5107 dotnet run --project src/Vigil.Simulator -- --scenario margaret-2.json
   ```

3. In Scalar, use **GET /cases** to find the Case of `ACC-MARGARET-2`. Wait until its status is `AwaitingApproval`.
4. Continue from [10.3](#103-read-the-finding).
5. Delete `margaret-2.json`.

With Azure OpenAI, each new Case costs approximately £0.003.

---

# Part C: Evaluate Vigil

## 14. Know what the evaluation measures

The demo shows that Vigil works for one story. It does not show how well Vigil works in general.
The evaluation answers that question. It runs Vigil on 1,000 test Holders and compares each result with the truth.

### 14.1 The test data

The evaluation uses a Dataset. The simulator makes the Dataset from a seed, so the same seed always gives the same Dataset.
The default Dataset has 1,000 Holders and 20,220 events.

Each Holder comes from a Persona. A Persona is a type of behaviour, for example a quiet Holder or an ATO fraudster.
Each Holder also has a Label. The Label tells the truth: fraud or genuine.
Vigil never sees the Labels. Only the evaluation reads them, after Vigil gives its result.

| Persona | Label | Holders | What the Persona does | Expected result |
|---|---|---|---|---|
| QuietHolder | Genuine | 644 | Logs in now and then from the same device | Low |
| HouseMover | Genuine | 109 | Changes the address and the phone from the same device | Low |
| NewPhone | Genuine | 113 | Resets the password and enrols a new MFA phone | Medium |
| ActiveTrader | Genuine | 84 | Sells shares often, and keeps the same details | Low |
| TravellingHolder | Genuine | 20 | Abroad on a new device: resets the password, enrols MFA and changes the phone | High. This is a known false positive. |
| AtoFraudster | Fraud | 9 | Reset, MFA, email change, bank change, then a sale, as in Margaret's story | High before the bank change |
| RegistrationFraudster | Fraud | 8 | Registers a new Account, adds bank details, then sells | High |
| FraudRing | Fraud | 5 | Five Accounts that share a device, an IP address and a bank account | High, but only with the graph |
| SlowBurnFraudster | Fraud | 8 | The ATO steps, with each gap just longer than the time limit of its Rule | Low. This is a known miss. |

In total, the Dataset has 30 fraud Holders and 970 genuine Holders.

### 14.2 The questions and the measures

The evaluation answers seven questions:

| Question | Measure | Cost | Section |
|---|---|---|---|
| Do the Rules find the fraud? | Detection rate | Free | [16.1](#161-read-the-rule-measures) |
| Do the Rules find ATO fraud in time? | Early-detection rate | Free | [16.1](#161-read-the-rule-measures) |
| Do the Rules give false alarms? | False-positive rate, precision of each Rule | Free | [16.1](#161-read-the-rule-measures) |
| Does the graph help? | The same measures for v1 (no graph) and v2 (graph) | Free | [16.1](#161-read-the-rule-measures) |
| Do the agents write correct Findings with real evidence? | Evidence Grounding, Verdict Agreement, Judge scores | Paid with Azure | [16.2](#162-read-the-agent-measures) |
| What fraud can the Rules miss? | Blind Spots | Paid with Azure | [18](#18-find-blind-spots-with-the-scenario-generator) |
| Which Rules do analysts disagree with? | Precision from Analyst Decisions, What-if | Free | [19](#19-learn-from-analyst-decisions) |

### 14.3 The Rule measures in simple words

An Account is **Flagged** when one of its Alerts reaches High. A Flagged Account gets a Case in the live system.

| Measure | What it counts | Good value | Example from the report |
|---|---|---|---|
| Detection rate | Flagged fraud Holders ÷ all fraud Holders | High | 22 / 30: Vigil finds 22 of the 30 fraudsters |
| False-positive rate | Flagged genuine Holders ÷ all genuine Holders | Low | 20 / 970: 20 genuine Holders get a Case that they do not deserve |
| Early-detection rate | ATO fraudsters Flagged before the bank change ÷ all ATO fraudsters | High | 9 / 9: Vigil finds each takeover before the money can go |
| Precision of a Rule | Hits on fraud Holders ÷ all hits of the Rule | High | R3: 14 / 14. Each bank change after a contact change was fraud. |

A good change to the Rules increases the detection rate, keeps the early-detection rate, and decreases the false-positive rate.
Often, one change improves one measure and makes a different measure worse. A person must then decide.

### 14.4 The agent measures in simple words

The agents cost money for each Investigation. Thus the evaluation does not investigate all 1,000 Holders.
It investigates an Agent Sample. The default sample has 25 Accounts:

- **20 Flagged Accounts.** The evaluation takes them from all the Personas, with a fixed seed.
- **5 Near-misses.** A Near-miss is a genuine Account that reached Medium but not High. It tests if the agents can clear a false alarm.

| Measure | What it counts | Good value |
|---|---|---|
| Evidence Grounding | First Findings whose cited refs all exist ÷ all first Findings | 100%. A lower value means that the Investigator invents evidence. |
| Verdict Agreement | Findings whose verdict agrees with the Label ÷ all Findings | High |
| Verdict Agreement on Near-misses | Near-miss Findings that say `LikelyGenuine` or `Inconclusive` ÷ all Near-miss Findings | High. The agents must not accuse a genuine Holder. |
| Reviewer rejection rate | Reviews that rejected the Finding ÷ all Reviews | Low, but not always 0. A Reviewer that never rejects can be too weak. |
| Review Flag rate | Findings with the Review Flag ÷ all Findings | Low |
| Investigations failed | Investigations with no Finding ÷ all Investigations | 0 |
| Tokens, cost and seconds | The mean for each Investigation | Low |

A verdict agrees with the Label in these conditions:

| Label | Verdicts that agree |
|---|---|
| ATO fraud | `LikelyAccountTakeover` |
| Registration fraud | `LikelyRegistrationFraud` |
| Ring or evasion fraud | `LikelyAccountTakeover` or `LikelyRegistrationFraud` |
| Genuine | `LikelyGenuine` or `Inconclusive` |

`Inconclusive` agrees with a genuine Label, because it sends the Case to the analyst without an accusation.

**The Judge.** A third AI agent, the Judge, reads each Finding and the same masked evidence.
It gives a score from 1 (poor) to 5 (excellent) for four criteria:

| Criterion | The question |
|---|---|
| Grounded claims | Does the evidence support each claim? |
| Sound reasoning | Does the verdict follow from the evidence? |
| Proportionate recommendation | Does the recommended action agree with the risk? |
| Clarity | Can an analyst understand the Finding quickly? |

The Judge uses the same model as the Investigator. It can prefer the style of its own model.
Use the Judge scores to compare two runs, not as an absolute grade.
The live system never runs the Judge. It runs only in the evaluation.

### 14.5 What each run costs

| Command | LLM calls | Cost with Azure OpenAI |
|---|---|---|
| `dotnet run --project src/Vigil.Evaluation` with `--llm Fake` | None | Free |
| The same command with `--llm AzureOpenAI` | 25 Investigations and 25 Judge calls | Approximately £0.10 to £0.20 |
| `generate-scenarios --llm AzureOpenAI` | 1 call | Less than £0.01 |
| `tune` | None | Free |

The costs are estimates for `gpt-4.1-mini` at the default prices.
The evaluation never runs the agents on the full Dataset. This is the budget guard.

## 15. Run the evaluation

The evaluation does not use the API or the AppHost. It needs Docker only.
The first run uses the Fake provider. It is free and it takes approximately 90 seconds.
The Rule measures are real with each provider, because the Rules do not use the LLM.
The agent measures are real only with Azure OpenAI. After this run, do [section 17](#17-run-the-evaluation-with-azure-openai).

### 15.1 Run the command

**Do this.**

```bash
dotnet run --project src/Vigil.Evaluation
```

**What you see.** After approximately 90 seconds, the command shows a summary:

```text
Wrote eval-reports/20261024T162209Z-v1-v2-seed42.md
v1: detection rate 17/30, false-positive rate 20/970, early-detection rate 9/9
v2: detection rate 22/30, false-positive rate 20/970, early-detection rate 9/9
agents (Fake, not meaningful): Evidence Grounding 25/25, Verdict Agreement 10/25, 0 tokens, £0.0000; Judge 25/25 judged, 0 tokens
Blind Spots: 1 of 1 fraud Generated Scenarios never reached High on v2
```

Each line has a meaning:

| Line | Meaning |
|---|---|
| `Wrote ...` | The name of the full report |
| `v1: ...` | The Rule measures without the graph |
| `v2: ...` | The Rule measures with the graph. The detection rate goes up from 17 to 22. |
| `agents ...` | The agent measures. With the Fake provider, they only show that the agent code runs. |
| `Blind Spots ...` | Generated Scenarios of fraud that never reached High |

### 15.2 Behind the scenes

The command does these tasks:

1. **Make the Dataset.** It makes `datasets/seed-42-holders-1000.jsonl` if the file does not exist.
2. **Score without the graph.** It applies `rules.v1.json` to each Holder.
3. **Score with the graph.** It starts a temporary Neo4j container. It sends all events into it in the sequence of event time. Then it applies `rules.v2.json`.
4. **Compare with the Labels.** It counts the Flagged Holders for each Label and each Persona.
5. **Run the agents.** It runs the Investigation on each Account of the Agent Sample. Then the Judge scores each Finding.
6. **Find the Blind Spots.** It scores each Generated Scenario in `datasets/generated/`.
7. **Write the report.** It writes one Markdown file in `eval-reports/`.

In step 3, the graph grows in the sequence of event time. A Graph Rule never sees a link that formed after the event that it scores.
In step 5, the agents see only the events that existed when the Case would open.

### 15.3 Change the run

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

## 16. Read the evaluation report

Open the new file in `eval-reports/`. The header tells you what made the report:

```markdown
| Run ID | `20261024T162209Z-v1-v2-seed42` |
| Seed | 42 |
| Holders | 1000 |
| Rules Versions | v1 (graph off), v2 (graph on) |
| Prompt Versions | investigator.v1+reviewer.v1, judge.v1 |
| LLM | Fake, model fake-model |
```

To compare two runs, make sure that they have the same seed and the same number of Holders.

### 16.1 Read the Rule measures

The **Metrics** table gives the three main measures:

| Metric | v1 (graph off) | v2 (graph on) |
|---|---|---|
| Detection rate | 17 / 30 (56.7%) | 22 / 30 (73.3%) |
| False-positive rate | 20 / 970 (2.1%) | 20 / 970 (2.1%) |
| Early-detection rate | 9 / 9 (100.0%) | 9 / 9 (100.0%) |

The graph finds five more fraudsters and adds no false positive.

The **Per-rule precision** table shows how often each Rule agrees with fraud:

| Rule | v2 (graph on) | What it tells you |
|---|---|---|
| R0 New device / new country | 22 / 155 (14.2%) | R0 agrees with many genuine Holders. It gives only 10 points, so alone it never makes a Case. |
| R1 Recovery takeover | 9 / 142 (6.3%) | Most hits are genuine NewPhone Holders. R0 and R1 together give 40, which is only Medium. |
| R2 Contact hijack | 9 / 29 (31.0%) | R2 makes the Travelling holders reach High. It causes the 20 false positives. |
| R3 to R6 | 100% | These Rules agree only with fraud |
| G1 and G3 | 5 / 5 (100%) | These Rules find the fraud ring |
| G2 | 0 / 0 | The Dataset has no Confirmed Fraud, so G2 cannot agree. The Scenarios show G2. |

The **By Persona** table shows each Persona and the result that Section 10.1 of PROJECT.md expects.
Use it to find the Persona that causes a change in a measure.

From this report, you can learn three facts:

- **The graph finds the fraud ring.** With v1, each ring Account gets only Medium. With v2, G1 and G3 put all five Accounts in Critical.
- **The slow-burn fraudster is a known miss.** Each step occurs just after the time limit of its Rule. No fixed time limit can find it.
- **The 20 false positives are Travelling holders.** A genuine Holder abroad resets the password, enrols MFA and changes the phone. Rules R0, R1 and R2 give 60, which is High.

### 16.2 Read the agent measures

The **Agents** section gives the agent measures, the Judge scores and one row for each Account of the Agent Sample.

With the Fake provider, the section starts with this warning:

```markdown
> **Not meaningful:** the Fake provider writes the same scripted Finding for every Account, so these numbers only
> show that the harness runs. Run with `--llm AzureOpenAI` for real ones.
```

The Fake provider always says `LikelyAccountTakeover`. Thus its Verdict Agreement is 10 / 25: it is correct only for the ATO and ring Accounts.
The Judge scores are all 3, because no model judged the Findings.
For real values, do [section 17](#17-run-the-evaluation-with-azure-openai).

The **Agent Sample** table has one row for each Investigation. Use it to find a Finding that went wrong:

| Column | Meaning |
|---|---|
| Account, Persona, Label | The Account and its truth |
| Sampled as | `Flagged` or `Near-miss` |
| Alert | The Score and the band when the Case would open |
| Verdict and Agrees | The verdict of the Finding, and if it agrees with the Label |
| Grounded | `yes` if each cited ref of the first Finding exists |
| Reviews and Review Flag | The result of each review, and if the Finding has the Review Flag |
| Judge G/R/P/C | The four Judge scores |
| Tokens, £, Seconds | The cost and the time of the Investigation |

### 16.3 Read the Blind Spots

The **Blind Spots** section lists each fraud Generated Scenario that never reached High:

```markdown
| Scenario | Fraud kind | Intent | Peak band | Rules hit |
|---|---|---|---|---|
| fake-late-mfa-slow-cash-out | Ato | Fake-written placeholder, to be replaced by the Azure run: ... | Low | R0 |
```

The `Intent` column tells you how the attacker tries to avoid the Rules.
[Section 18](#18-find-blind-spots-with-the-scenario-generator) tells you how to make real Generated Scenarios.

## 17. Run the evaluation with Azure OpenAI

**PAID STEP.** This step costs money. Before you do it, set the budget alert in [section 3.2](#32-set-a-budget-alert-before-you-spend-money).

Do this step only after [section 3](#3-set-up-azure-openai) and [section 4.5](#45-configure-the-evaluation-command-for-azure-openai).
The evaluation reads the Azure settings from environment variables, not from the user secrets.

**Do this.**

1. Do a small test first. `--agent-sample 4` gives 4 Flagged Accounts and 1 Near-miss, so 5 Investigations:

   ```bash
   dotnet run --project src/Vigil.Evaluation -- --llm AzureOpenAI --agent-sample 4
   ```

2. Open the report. Make sure that the `LLM` line shows your model, and that the tokens are more than 0.
3. Do the full run:

   ```bash
   dotnet run --project src/Vigil.Evaluation -- --llm AzureOpenAI
   ```

The full run makes 25 Investigations and 25 Judge calls.
Each Investigation has at most two Investigator attempts and two Reviewer calls.
The report shows the total tokens and the total estimated £.

If a setting is missing, the command stops in the first second, before the slow steps.

**What to look for in the report.**

- **Evidence Grounding** must be near 100%. If it is lower, read the Findings that are not grounded.
- **Verdict Agreement on Near-misses** shows if the agents can clear a false alarm.
- **The Travelling holders** are the false positives of the Rules. Look at their rows. A good Finding says `LikelyGenuine` or `Inconclusive`.
- **The Judge scores** are useful when you compare two Prompt Versions on the same seed.

## 18. Find Blind Spots with the Scenario Generator

**PAID STEP** with `--llm AzureOpenAI`. With `--llm Fake`, the command is free, but it gives only one fixed Scenario.

The Scenario Generator is an AI agent that acts as an attacker.
It reads the Rules and the example Scenarios. Then it writes new fraud Scenarios that the Rules can miss.
A fraud Scenario that never reaches High is a Blind Spot.

### 18.1 Generate Scenarios

**Do this.**

```bash
dotnet run --project src/Vigil.Evaluation -- generate-scenarios --count 5 --llm AzureOpenAI
```

| Option | Default | Use |
|---|---|---|
| `--count` | `5` | The number of Scenarios to ask for. The maximum is 10. |
| `--rules` | `config/rules.v2.json` | The Rules that the generator tries to avoid |
| `--llm` | `Fake` | `Fake` or `AzureOpenAI` |
| `--out` | `datasets/generated` | The folder for the new Scenarios |

**Behind the scenes.** The command makes one LLM call.
It keeps a Scenario only if all these conditions are true:

- The JSON agrees with the Scenario format.
- The Scenario has a `label` with a fraud kind and an intent.
- The sequence of event types and gaps is different from each Scenario that is already in the folder.

A Generated Scenario has a `label` that tells you how the attacker tries to avoid the Rules:

```json
"label": {
  "kind": "Ato",
  "intent": "MFA 65 minutes after the reset misses R1's 60-minute Window, and the sale waits out R4."
}
```

### 18.2 Check and keep the Scenarios

1. Read each new file in `datasets/generated/`.
2. Do a dry run of each file that you are not sure about ([20.1](#201-try-a-change-on-one-scenario)):

   ```bash
   dotnet run --project src/Vigil.Evaluation -- scenario datasets/generated/<name>.json
   ```

3. Delete the Fake placeholder:

   ```bash
   git rm datasets/generated/fake-late-mfa-slow-cash-out.json
   ```

4. Run the evaluation again. The report lists each new Blind Spot.
5. Commit the Generated Scenarios and the new report together.

A person decides if a Blind Spot needs a new Rule. The generator does not change the Rules.

## 19. Learn from Analyst Decisions

In the demo, one analyst decided one Case. In real use, analysts decide many Cases.
Each `FalsePositive` decision tells you that a Rule gave a false alarm.
The Rule-Tuning Report collects these decisions and shows which Rules give too many false alarms.

On your computer, there are no real analysts. The Stand-in Analyst acts as the analysts.
It decides each Case from the Label in the Dataset.

The full procedure has five steps:

1. Prepare the API for many Cases (19.1).
2. Send the 1,000-holder Dataset to the API (19.2). Vigil opens a Case for each Account that reaches High.
3. Let the Stand-in Analyst decide each Case (19.3).
4. Make the Rule-Tuning Report from the decisions (19.4).
5. Read the report and decide what to do (19.5).

The full procedure uses the Fake provider. It costs nothing.

The evaluation report and the Rule-Tuning Report both give a precision for each Rule, but they count different things.
The evaluation report counts Labels. The Rule-Tuning Report counts Analyst Decisions, as a real team would.

### 19.1 Prepare the API

1. Stop the AppHost.
2. Reset the data, so that the report counts only the decisions of this run. Use the procedure in the [runbook](runbook.md#reset-data).
3. Open `src/Vigil.Api/appsettings.Development.json`.
4. Set `"Provider"` to `"Fake"`. The Rule-Tuning Report does not use the Findings, so do not pay for 42 Investigations.
5. Add an `Investigation` section with a higher daily limit:

   ```json
   {
     "Database": { "MigrateOnStartup": true },
     "Llm": { "Provider": "Fake" },
     "Investigation": { "DailyLimit": 100 }
   }
   ```

6. Start the AppHost.
7. Send the Scenarios again ([7.1](#71-send-the-scenarios)), so that Margaret's story is in the data.

CAUTION: Do step 5 before you send the Dataset.
The Dataset opens approximately 42 Cases. With the limit of 20, the other Cases stay `Open` and the worker never investigates them.
An `Open` Case cannot get a decision.

CAUTION: Do not commit the change to `appsettings.Development.json`. Remove it when you finish.

### 19.2 Send the Dataset

**Do this.**

```bash
dotnet run --project src/Vigil.Simulator -- generate --seed 42 --holders 1000
services__api__http__0=http://localhost:5107 dotnet run --project src/Vigil.Simulator -- post-dataset
```

**What you see.**

```text
Wrote datasets/seed-42-holders-1000.jsonl
info: Simulator[0]
      Posted 20220 events from 1000 Accounts in datasets/seed-42-holders-1000.jsonl
```

**Behind the scenes.** The simulator sends the events without their Labels, in sequence of event time, in batches of 200.
Thus Vigil cannot see the truth. This takes approximately 75 seconds.
Each batch goes through the same steps as in [7.3](#73-behind-the-scenes-one-request-to-the-api).

The worker investigates the new Cases in the background, one at a time.
To see the progress, use Scalar:

1. Select **GET /cases**.
2. Select **Test Request**.
3. Type `Open` in the `status` field. Select **Send**.
4. Type `Investigating` in the `status` field. Select **Send**.

When both responses are an empty list, `[]`, the worker has finished.
Then the Dataset has approximately 42 Cases in `AwaitingApproval`.

### 19.3 Let the Stand-in Analyst decide

**Do this.**

```bash
services__api__http__0=http://localhost:5107 dotnet run --project src/Vigil.Simulator -- decide --by-label
```

**What you see.**

```text
info: Simulator[0]
      Stand-in Analyst decided 42 Cases; 0 were decided by someone else first
warn: Simulator[0]
      Left for a human, not in datasets/seed-42-holders-1000.jsonl: ACC-PRIYA, ACC-DEREK, ACC-LENA
```

**Behind the scenes.** The Stand-in Analyst decides each Case in `AwaitingApproval` from the Label of its Account:

| Label | Outcome | Action |
|---|---|---|
| Fraud | `ConfirmedFraud` | `BlockBankChanges` |
| Genuine | `FalsePositive` | `NoAction` |

It sends each decision to **POST /cases/{id}/decision**, as you did in [11.1](#111-send-the-decision), with the analyst ID `stand-in`.
It does not decide a Case whose Account is not in the Dataset. The Scenario Accounts stay for you.

If the command tells you that Cases are still `Open` or `Investigating`, wait some seconds. Then do the command again.

### 19.4 Make the Rule-Tuning Report

The `tune` command reads the decisions from PostgreSQL. It needs the connection string.

1. Get the connection string. Use the procedure in the [runbook](runbook.md#find-the-postgres-connection-string).
2. Do the command:

   ```bash
   dotnet run --project src/Vigil.Evaluation -- tune \
     --connection "Host=localhost;Port=<port>;Database=vigil;Username=postgres;Password=<password>"
   ```

**What you see.**

```text
R0 10 → 5 (precision 15/35): false-positive rate 20 → 0, early-detection rate 9 → 0 of 9
R1 30 → 15 (precision 10/30): false-positive rate 20 → 0, early-detection rate 9 → 0 of 9
R2 20 → 10 (precision 10/30): false-positive rate 20 → 0, early-detection rate 9 → 0 of 9
Wrote eval-reports/rule-tuning-20261024T172201Z-v2-seed42.md
```

**Behind the scenes.** For each Rule, the command counts the decided Cases that the Rule helped to open.
Then it calculates how many of them were real fraud. This is the precision.
For each Rule with a low precision, it suggests fewer points.
Then it scores the full Dataset again with that change. This is the What-if.
The What-if uses the graph Rules, so the command also starts a temporary Neo4j container.

### 19.5 Read the Rule-Tuning Report

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

### 19.6 Clean up

1. Remove the `Investigation` section from `appsettings.Development.json`.
2. Make sure that `git status` shows no change to the file.
3. Commit the Rule-Tuning Report if you want to keep it.

## 20. Change a Rule safely

The Rule-Tuning Report never changes a Rules file. A person changes the Rules.
First, try the change on one Scenario. Then measure it on the full Dataset. Then deploy it.

### 20.1 Try a change on one Scenario

You can score one Scenario in memory. The API does not have to run.

**Do this.**

```bash
dotnet run --project src/Vigil.Evaluation -- scenario scenarios/margaret-ato.json
```

**What you see.**

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

The dry run has no graph. Thus Margaret gets 90, not 135, and she reaches High one step later.
To try a changed Rule, copy the Rules file, change it, and add `--rules my-rules.json`.

### 20.2 Measure and deploy the change

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

6. Read the report ([16.1](#161-read-the-rule-measures)).
7. Compare the detection rate, the false-positive rate and the early-detection rate.
8. If the new version is better, open `src/Vigil.Api/appsettings.json`.
9. Change `Rules:Path` to the new file:

   ```json
   "Rules": { "Path": "config/rules.v3.json" }
   ```

10. Start the API again.
11. In Scalar, send **GET /rules**. The response now shows `"version": "v3"`.
12. Commit the new Rules file, the setting and the evaluation report together.

Each new Alert records `"rulesVersion": "v3"`.
Old Alerts keep `"v2"`. Vigil never scores them again with the new Rules.

Note: The API reads the Rules file only when it starts. If the file is not correct, the API does not start, and it shows the error.

---

# Part D: Recover and reference

## 21. Reset and recover

The [runbook](runbook.md) has the recovery procedures. Use it for these tasks:

| Task | Runbook section |
|---|---|
| Delete all data and start again | [Reset data](runbook.md#reset-data) |
| Build the graph again after Neo4j data is lost | [Reset data](runbook.md#reset-data), "Neo4j only" |
| Remove an old Dataset file after a Persona change | [Reset data](runbook.md#reset-data), "Stale Datasets" |
| Get the PostgreSQL connection string | [Find the Postgres connection string](runbook.md#find-the-postgres-connection-string) |
| Find the fix for an error | [Common failures](runbook.md#common-failures) |

To run the tests, read the [README](../README.md#tests).

## 22. File and field reference

### 22.1 The Scenario file

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
| `name` | Yes | A short name in lower case with hyphens. Vigil makes the event IDs from it. Generated Scenarios use it as the file name. |
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

### 22.2 The Rules file

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
| `accountThreshold` | For G3: the smallest number of Accounts on one identifier that makes a Ring |

### 22.3 Case statuses

| Status | Meaning |
|---|---|
| `Open` | The Case waits for the worker. If `notInvestigated` is `true`, the budget stopped the Investigation. |
| `Investigating` | The agents work on the Case |
| `AwaitingApproval` | The Finding is ready. The Case waits for an analyst. |
| `Closed` | An analyst recorded a decision |

The list of Cases shows the worst Cases first: by band, then by Score, then by the time that the Case opened.

### 22.4 The Finding

| Field | Meaning |
|---|---|
| `verdict` | `LikelyAccountTakeover`, `LikelyRegistrationFraud`, `Inconclusive` or `LikelyGenuine` |
| `confidence` | `Low`, `Medium` or `High`. The Investigator gives this value. |
| `summary` | At most 120 words. You see real identifiers here. The LLM saw only Pseudonyms. |
| `recommendation` | `BlockBankChanges`, `ContactHolder` or `NoAction` |
| `evidenceRefs` | Each cited ref and the events behind it. Use them to examine each claim. |
| `reviewFlagged` | `true` if the Investigator wrote two Findings and the second one also failed a check. Read the Finding with more care. |
| `investigationFailed` | `true` if the LLM failed. There is no Finding. Examine the Case without it. |

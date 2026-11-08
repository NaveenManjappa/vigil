# Vigil user guide

This guide tells you how to set up Vigil, start it and follow one fraud case from start to end.
You send one event at a time in Scalar. After each event, you stop and look at what changed.

The guide is in Simplified Technical English (ASD-STE100).
To reset Vigil or to fix a failure, read the [runbook](runbook.md).

## Contents

1. [What Vigil does](#1-what-vigil-does)
2. [Before you start](#2-before-you-start)
3. [Start Vigil](#3-start-vigil)
4. [Walk through Margaret's takeover](#4-walk-through-margarets-takeover)
5. [Walk through Mr Patel's house move](#5-walk-through-mr-patels-house-move)
6. [Use a real model on Azure OpenAI](#6-use-a-real-model-on-azure-openai)
7. [All the settings](#7-all-the-settings)
8. [All the endpoints](#8-all-the-endpoints)
9. [Run the tests](#9-run-the-tests)

## 1. What Vigil does

Vigil finds account takeover on an investor portal.
It finds the fraud before the fraudster gets the money.

Vigil does these steps for each Portal Event:

1. It keeps the event in PostgreSQL.
2. It copies the event into a Neo4j graph. The graph shows which Accounts share a device or a bank account.
3. It runs the Rules. Each Rule that the event triggers adds points to the Account's Alert.
4. When the Alert reaches the High band, Vigil opens a Case.

Then you continue the work:

1. You ask the Investigator agent to investigate the Case. The agent writes a Finding.
2. You record the analyst's decision. This closes the Case.

```text
event ─► PostgreSQL + graph ─► Rules ─► Alert (Score, band) ─► High? ─► Case
                                                                          │
                       you call: investigate ─► Finding ─► you call: decision ─► Case closed
```

The Rules are cheap, so they examine each event.
The agent costs money, so it examines only a Case, and only when you ask.
A person makes the decision.

Vigil has these parts:

| Part | What it does |
|---|---|
| `Vigil.Api` | Receives events and gives all the endpoints. Scalar shows the endpoints in your browser. |
| `Vigil.Detection` | The Rules that look at one Account |
| `Vigil.Graph` | Copies events into Neo4j, and runs the two Rules that look at other Accounts |
| `Vigil.Agents` | The Investigator agent and its three tools |
| PostgreSQL | Keeps the events, the Alerts and the Cases |
| Neo4j | Keeps the graph of Accounts and their devices, emails, phones and bank accounts |
| `Vigil.AppHost` | Starts all the parts on your computer and shows the Aspire dashboard |

## 2. Before you start

Install these tools:

| Tool | Version | Use |
|---|---|---|
| .NET SDK | 10 | Builds and runs Vigil |
| Docker Desktop | Current | Runs PostgreSQL and Neo4j |
| Git | Current | Gets the code |
| A web browser | Current | Opens the dashboard, Scalar and Neo4j Browser |

Get the code:

```bash
git clone https://github.com/NaveenManjappa/vigil.git
cd vigil
dotnet build
```

Do all the commands in this guide from the repository root.

## 3. Start Vigil

1. Start Docker Desktop.
2. Start the AppHost:

   ```bash
   dotnet run --project src/Vigil.AppHost
   ```

3. Open the dashboard URL that the terminal shows.
4. Wait until `postgres`, `neo4j` and `api` show **Running**.
5. In the `api` row, select the `/scalar` link. Scalar opens at `http://localhost:5107/scalar`.

Scalar shows the endpoints in four groups: **0. Setup**, **1. Events**, **2. Accounts** and **3. Cases**.
To send a request, select the endpoint, select **Test Request**, paste the body and select **Send**.

You can also look at the graph in Neo4j Browser:

1. Get the Neo4j password:

   ```bash
   dotnet user-secrets list --project src/Vigil.AppHost
   ```

2. Open `http://localhost:7474`.
3. Sign in with the user `neo4j` and the value of `Parameters:neo4j-password`.

Vigil starts with the Fake model. The Fake model is free and calls no real model.
It always calls one tool, then returns a fixed Finding.
To see real reasoning, read [section 6](#6-use-a-real-model-on-azure-openai).

## 4. Walk through Margaret's takeover

Margaret uses her laptop in Great Britain.
A fraudster gets her email password.
The fraudster resets her portal password from a device in Romania, then changes her bank details.
The fraudster uses device `D-77`. Derek's Account used the same device, and Derek is known fraud.

Do the steps in sequence. After each step, read the response before you do the next step.

### Step 0: Start from nothing

Send `POST /reset` with no body.

Look at the response. Vigil deleted all events, Alerts, Cases and graph nodes.

### Step 1: Derek logs in from device D-77

Send `POST /events` with this body:

```json
{
  "accountId": "ACC-DEREK",
  "holderId": "HOLDER-DEREK",
  "type": "LoginSucceeded",
  "occurredAt": "2026-09-01T10:00:00Z",
  "deviceId": "D-77",
  "ipAddress": "198.51.100.77",
  "country": "RO"
}
```

Look at `message`: `Event #1 added no Rule Hits.`
This event is the first event of the Account, so Vigil has nothing to compare it with.

### Step 2: Mark Derek as known fraud

Send `POST /accounts/{accountId}/confirm-fraud` with `accountId` set to `ACC-DEREK`.

In Neo4j Browser, run this query:

```cypher
MATCH (n) RETURN n
```

Look at the graph. The `ACC-DEREK` node has the property `confirmedFraud: true`, and it links to the `D-77` node.

### Step 3: Margaret logs in as usual

Send `POST /events` with this body:

```json
{
  "accountId": "ACC-MARGARET",
  "holderId": "HOLDER-MARGARET",
  "type": "LoginSucceeded",
  "occurredAt": "2026-09-01T08:30:00Z",
  "deviceId": "D-MARGARET-LAPTOP",
  "ipAddress": "203.0.113.51",
  "country": "GB"
}
```

Look at `message`: `Event #1 added no Rule Hits.`

### Step 4: Margaret logs in again

Send the body of step 3 again, but set `occurredAt` to `2026-09-15T08:30:00Z`.

Look at `message`: `Event #2 added no Rule Hits.` The device and the country are usual for Margaret.

### Step 5: The fraudster resets the password

Send `POST /events` with this body:

```json
{
  "accountId": "ACC-MARGARET",
  "holderId": "HOLDER-MARGARET",
  "type": "PasswordReset",
  "occurredAt": "2026-09-22T09:00:00Z",
  "deviceId": "D-77",
  "ipAddress": "198.51.100.77",
  "country": "RO"
}
```

Look at `message`: `Event #3 added R0 (+10), G2 (+25). Alert: Score 35, Medium.`

- R0 hits because Margaret never used device `D-77` or the country `RO` before.
- G2 hits because device `D-77` is also on Derek's Account, and Derek is known fraud.

Look at `newHits`. Each hit has an `explanation` in plain English.
Medium is below High, so Vigil opens no Case yet.

### Step 6: The fraudster adds their own MFA

Send the body of step 5 again, but set `type` to `MfaEnrolled` and `occurredAt` to `2026-09-22T09:04:00Z`.

Look at the response:

- `newHits` has R1, because the fraudster added MFA 4 minutes after a password reset.
- `repeatedRules` has R0 and G2. Each Rule counts only one time on an Alert, so they add no points.
- `alert.score` is 65 and `alert.band` is `High`.
- `caseOpened` has a Case ID. Copy it for step 10.

### Step 7: The fraudster changes the email address

Send `POST /events` with this body:

```json
{
  "accountId": "ACC-MARGARET",
  "holderId": "HOLDER-MARGARET",
  "type": "EmailChanged",
  "occurredAt": "2026-09-22T09:10:00Z",
  "deviceId": "D-77",
  "ipAddress": "198.51.100.77",
  "country": "RO",
  "attributes": { "NewEmail": "m.attacker@example.net" }
}
```

Look at the response. R2 adds 20 points, and the Score is 85.
`caseOpened` is null, because the Alert already has a Case.

### Step 8: The fraudster changes the bank details the next day

Send `POST /events` with this body:

```json
{
  "accountId": "ACC-MARGARET",
  "holderId": "HOLDER-MARGARET",
  "type": "BankDetailsChanged",
  "occurredAt": "2026-09-23T14:10:00Z",
  "deviceId": "D-77",
  "ipAddress": "198.51.100.77",
  "country": "RO",
  "attributes": { "NewBankAccountHash": "BA-9f31" }
}
```

Look at the response. R3 adds 30 points, and the Score is 115.
The band is now `Critical`.

### Step 9: Look at the whole Account

Send `GET /accounts/{accountId}` with `accountId` set to `ACC-MARGARET`.

Look at these parts of the response:

| Part | What you see |
|---|---|
| `events` | The six events. Each event has a `number`. The agent uses these numbers in its Finding. |
| `alerts` | One open Alert with five hits: R0, G2, R1, R2 and R3 |
| `cases` | One Case with the status `Open` |
| `linkedIdentifiers` | Device `D-77` is also used by `ACC-DEREK`, which is Confirmed Fraud |

In Neo4j Browser, run `MATCH (n) RETURN n` again.
Margaret's Account and Derek's Account now both link to `D-77`.

### Step 10: Ask the agent to investigate

Send `POST /cases/{id}/investigate` with `id` set to the Case ID from step 6.

The agent gets a brief: the Alert's Rule Hits and the flagged events.
It can call three tools to get more evidence. Then it writes a Finding.

Look at the response:

- `status` is `AwaitingDecision`.
- `finding.verdict`, `finding.confidence` and `finding.recommendation` give the agent's view.
- `finding.summary` gives the reasons in plain English.
- `finding.events` gives the numbers of the events that the summary uses, for example `[3, 4, 5, 6]`.
- `inputTokens` and `outputTokens` give the cost of the work. The Fake model shows 0.

In the Aspire dashboard, open **Traces** and select the trace of this request.
Each call to the model and each tool call show as a separate span.

If the model fails, the response is the error 502 and the Case stays `Open`.
Send the request again.

### Step 11: Record the analyst's decision

Send `POST /cases/{id}/decision` with the same `id` and this body:

```json
{
  "outcome": "ConfirmedFraud",
  "action": "BlockBankChanges",
  "analystId": "analyst-1",
  "notes": "Matches the takeover pattern."
}
```

Look at the response:

- `status` is `Closed`.
- `intervention` is `BlockBankChanges`. The portal must refuse bank changes until `interventionUntil`, 7 days later.

Send `GET /accounts/{accountId}` for `ACC-MARGARET` again.
`isConfirmedFraud` is now `true`.
G2 now hits each Account that later uses one of Margaret's identifiers.

## 5. Walk through Mr Patel's house move

Mr Patel moves house. He changes his address, then his bank account, from his own laptop.
This is a genuine change. It shows that Vigil does not call the agent for each change.

1. Send this login:

   ```json
   {
     "accountId": "ACC-PATEL",
     "holderId": "HOLDER-PATEL",
     "type": "LoginSucceeded",
     "occurredAt": "2026-09-10T19:00:00Z",
     "deviceId": "D-PATEL-LAPTOP",
     "ipAddress": "203.0.113.20",
     "country": "GB"
   }
   ```

2. Send the same body, but set `type` to `AddressChanged` and `occurredAt` to `2026-09-20T18:00:00Z`. Add `"attributes": { "NewPostcode": "RG1 4AB" }`.
3. Send the same body, but set `type` to `BankDetailsChanged` and `occurredAt` to `2026-09-21T19:00:00Z`. Set `attributes` to `{ "NewBankAccountHash": "BA-PATEL-NEW" }`.

Look at `message` after step 3: `Event #3 added R3 (+30). Alert: Score 30, Medium.`
The device and the country are usual, so R0 does not hit.
Medium is below High, so Vigil opens no Case and the agent does not run.

## 6. Use a real model on Azure OpenAI

**PAID STEPS.** The steps in this section create resources that cost money.
One Investigation costs approximately £0.003 with `gpt-4.1-mini`.

The steps agree with Microsoft Learn on 24 October 2026.
If a screen in the Azure portal is different, use the Microsoft Learn page that the step gives.

### 6.1 Set a budget alert

Do this step first.
You must have an Azure subscription with Pay-As-You-Go billing.

1. In the [Azure portal](https://portal.azure.com), open **Cost Management + Billing**.
2. Select your subscription, then select **Budgets**.
3. Select **Add**.
4. Set **Name** to `vigil-llm`, **Reset period** to **Monthly** and **Amount** to `10`.
5. Add an alert of type **Actual** at `90` %, with your email address.
6. Select **Create**.

Reference: [Tutorial: Create and manage budgets](https://learn.microsoft.com/azure/cost-management-billing/costs/tutorial-acm-create-budgets).

### 6.2 Create the resource and deploy a model

Do these commands with the Azure CLI:

```bash
az login
az group create --name rg-vigil --location swedencentral

az cognitiveservices account create \
  --resource-group rg-vigil --name vigil-openai-<your initials> \
  --location swedencentral --kind OpenAI --sku S0 \
  --custom-domain vigil-openai-<your initials>

az cognitiveservices account list-models \
  --resource-group rg-vigil --name vigil-openai-<your initials> \
  --query "[?name=='gpt-4.1-mini'].{name:name, version:version}" -o table

az cognitiveservices account deployment create \
  --resource-group rg-vigil --name vigil-openai-<your initials> \
  --deployment-name vigil-chat \
  --model-name gpt-4.1-mini --model-version "<version from the list>" --model-format OpenAI \
  --sku-name GlobalStandard --sku-capacity 50
```

CAUTION: Vigil uses the deployment name `vigil-chat`, not the model name. The model name gives the error 404.

References: [Create and deploy an Azure OpenAI resource](https://learn.microsoft.com/azure/ai-foundry/openai/how-to/create-resource), [Deployment types](https://learn.microsoft.com/azure/ai-foundry/openai/how-to/deployment-types).

### 6.3 Get the endpoint and the key

```bash
az cognitiveservices account show -g rg-vigil -n vigil-openai-<your initials> --query properties.endpoint -o tsv
az cognitiveservices account keys list -g rg-vigil -n vigil-openai-<your initials> --query key1 -o tsv
```

WARNING: Keep the key secret. Do not put it in a file that Git tracks.

Use the endpoint exactly as Azure shows it, for example `https://vigil-openai-ab.openai.azure.com/`.
Do not add `/openai/v1/`. Vigil adds it.

### 6.4 Configure Vigil

1. Put the key into the user secrets of the AppHost:

   ```bash
   dotnet user-secrets set "Parameters:azure-openai-key" "<key>" --project src/Vigil.AppHost
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

4. Stop the AppHost with Ctrl+C.
5. Start the AppHost again.
6. Do the walkthrough in [section 4](#4-walk-through-margarets-takeover) again, from step 0.

At step 10, the Finding now comes from the real model.
The API log shows one line for each call to the model, with its tokens and its estimated cost in pounds.

### 6.5 Delete the Azure resources when you finish

```bash
az group delete --name rg-vigil --yes
```

## 7. All the settings

All the settings are in `src/Vigil.Api/appsettings.json` and `src/Vigil.Api/appsettings.Development.json`.
An environment variable uses two underscores where a setting uses a colon, for example `Llm__Provider`.

| Setting | Default | Change it when |
|---|---|---|
| `Rules:Path` | `config/rules.json` | You want a different Rules file |
| `Llm:Provider` | `Fake` | You want a real model: set `AzureOpenAI` |
| `Llm:Endpoint` | none | You use Azure OpenAI: the endpoint from [6.3](#63-get-the-endpoint-and-the-key) |
| `Llm:Deployment` | none | You use Azure OpenAI: the deployment name, for example `vigil-chat` |
| `Llm:ApiKey` | a placeholder | Set it as the user secret `Parameters:azure-openai-key`, never in a file |
| `Llm:InputPricePer1kTokens` | `0.0003` | Your input price in pounds for 1,000 tokens is different |
| `Llm:OutputPricePer1kTokens` | `0.0012` | Your output price is different |
| `Intervention:BlockBankChangesDays` | `7` | The business changes the length of a bank-change block |
| `Database:MigrateOnStartup` | `true` in Development | Do not change it on your computer |

The Rules, their points, the band thresholds and the band that opens a Case (`openCaseAt`) are in `config/rules.json`.
To see the Rules that the API uses, send `GET /rules`.
After you change the file, stop the AppHost and start it again.

## 8. All the endpoints

| Endpoint | What it does |
|---|---|
| `POST /reset` | Deletes all data. It is available in Development only. |
| `GET /rules` | Shows the Rules that the API uses |
| `POST /events` | Receives one event and tells you what it changed |
| `GET /accounts/{accountId}` | Shows one Account: events, Alerts, Cases and linked Accounts |
| `POST /accounts/{accountId}/confirm-fraud` | Marks an Account as known fraud |
| `GET /cases` | Shows the Cases, the most risky first. You can filter by `status`. |
| `GET /cases/{id}` | Shows one Case |
| `POST /cases/{id}/investigate` | Runs the Investigator on an `Open` Case |
| `POST /cases/{id}/decision` | Records the decision on an `AwaitingDecision` Case and closes it |

## 9. Run the tests

Start Docker Desktop, then do this command:

```bash
dotnet test
```

The API and graph tests start temporary PostgreSQL and Neo4j containers.
No test calls a real model.
The test `WalkthroughTests` sends the same requests as [section 4](#4-walk-through-margarets-takeover) and [section 5](#5-walk-through-mr-patels-house-move).
If the walkthrough in this guide changes, change that test too.

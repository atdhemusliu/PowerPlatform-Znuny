# Znuny (OTRS REST) custom connector

Maker-friendly wrapper around the Znuny / OTRS **Generic Interface REST** ticket operations described in [otrs-rest-connector.openapi.yaml](./otrs-rest-connector.openapi.yaml) (OpenAPI 3.1 reference only; Power Platform uses **Swagger 2.0** in `apiDefinition.swagger.json`).

## What this connector simplifies

| Without this connector | With this connector |
|------------------------|---------------------|
| Every flow repeats `UserLogin` / `Password` in the JSON body | Stored once on the **connection**; script injects them |
| Nested `Ticket` / `Article` objects | **Flat** action parameters (e.g. `Title`, `ArticleBody`) |
| HTTP 200 responses that still contain an `Error` object | Script maps logical failures to **4xx** with `{ "error": { "code", "message", "raw" } }` |
| Guessing Base64 for small attachments | Optional auto-encoding when the value is not valid Base64 |

## Prerequisites (Znuny admin)

1. **Generic Interface** web service configured for REST (often imported as **RESTConnector** or a custom name).
2. Operations enabled: **TicketCreate**, **TicketUpdate**, **TicketGet**, **TicketSearch** (matching your Znuny version).
3. An **API agent** account (recommended) whose login/password you will store on the connection.

See also [Agents.md](./Agents.md) and the parent repo [AGENTS.md](../AGENTS.md).

## Connection parameters

| Parameter | Required | Description |
|-----------|----------|-------------|
| **Znuny base URL** (`znunyBaseUrl`) | Yes | Full URL with scheme, e.g. `https://znuny.contoso.com` (no trailing slash). Routed via `dynamichosturl` policy. |
| **Web service name** (`webServiceName`) | No | Defaults to `RESTConnector` if empty. Must match the web service name in the URL path on your server. |
| **Agent login** (`agentUserLogin`) | Yes | Maps to JSON `UserLogin`. |
| **Agent password** (`agentPassword`) | Yes | Secure string; maps to JSON `Password`. |

## Actions (operation IDs)

All actions are **POST**; internal paths `/znuny/...` are rewritten by `script.csx` to:

`POST /otrs/nph-genericinterface.pl/Webservice/{webServiceName}/{Operation}`

| Action | Maps to | Notes |
|--------|---------|--------|
| **CreateTicket** | TicketCreate | Requires ticket + first article fields; optional attachment + dynamic fields. |
| **UpdateTicket** | TicketUpdate | Send only fields you want to change (partial `Ticket` object). |
| **GetTicket** | TicketGet | Optional booleans → `DynamicFields` / `AllArticles` / `Attachments` = `"1"`. |
| **SearchTickets** | TicketSearch | Optional `ServiceID`, `Title`, comma-separated `States` → JSON array `States`. Merge `ExtraFilters` for advanced parameters supported by your server. |
| **AddArticle** | TicketUpdate | Adds an `Article` to an existing ticket (same backend operation as a subset of update). |

## Error normalization

Znuny may return **HTTP 200** with a body like:

```json
{
  "Error": {
    "ErrorCode": "TicketCreate.MissingParameter",
    "ErrorMessage": "..."
  }
}
```

The script turns that into a **non-2xx** status (typically **400**, or **401** when the error code/message suggests authentication). The body includes `error.raw` with the original JSON for troubleshooting.

Some success schemas in the upstream doc also use a legacy **`Error Message`** property (note the space); that is treated as an error as well.

## Sample `CreateTicket` body (flow designer)

```json
{
  "Title": "Printer not working",
  "QueueID": "5",
  "State": "new",
  "PriorityID": "3",
  "CustomerUser": "user@contoso.com",
  "ArticleBody": "Cannot print to HR printer.",
  "ArticleCommunicationChannel": "Internal",
  "ArticleContentType": "text/plain; charset=utf8"
}
```

Optional: `DynamicFields`: `[{ "Name": "Location", "Value": "Berlin" }]`, attachment fields `AttachmentFilename`, `AttachmentContentType`, `AttachmentContentBase64`.

## Sample `SearchTickets` with power-user filters

```json
{
  "States": "new,open",
  "ExtraFilters": {
    "QueueIDs": [1, 5],
    "Title": "%printer%"
  }
}
```

Exact keys inside `ExtraFilters` depend on your Znuny **TicketSearch** operation configuration and version; invalid keys may be ignored or rejected by the server.

## Files in this folder

| File | Role |
|------|------|
| `apiDefinition.swagger.json` | Swagger 2.0 — maker-visible operations and flat schemas. |
| `apiProperties.json` | Connection parameters, `dynamichosturl`, `scriptOperations`. |
| `script.csx` | Request shaping, auth injection, URI rewrite, response normalization. |
| `otrs-rest-connector.openapi.yaml` | Canonical OTRS REST contract (reference). |
| `Agents.md` | Agent-focused notes for this connector. |
| `settings.json` | Optional paths for **paconn** CLI. |

## Version assumptions

Test against your target **Znuny** (fork of OTRS) / **OTRS** version and web service export. Field names follow the Generic Interface REST examples; your admin may restrict or remap operations.

## Publishing / CLI

Use **Power Automate** / **Power Apps** custom connector import, or the **paconn** CLI from [Microsoft Power Platform Connectors tooling](https://github.com/microsoft/PowerPlatformConnectors) with `settings.json` in this folder.

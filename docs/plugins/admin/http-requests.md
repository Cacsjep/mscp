<div class="show-title" markdown>

# HTTP Requests

Configure and execute HTTP requests as XProtect rule actions. Create request folders containing individual requests (like Postman), with custom JSON payloads merged with Milestone event data and flexible authentication options.

## Quick Start

1. Open the **Management Client**
2. Navigate to the **HTTP Requests** node in the sidebar
3. Right-click and **Create New** to add a Request Folder
4. Expand the folder, right-click and **Create New** to add an HTTP Request
5. Configure method, URL, payload, authentication, and headers
6. Save the configuration
7. Create a **Rule** with the **Execute HTTP Request** action — target a specific request, all in a folder, or all requests

## Request Configuration

| Property | Description |
|---|---|
| **Method** | GET, POST, PUT, DELETE, PATCH (body hidden for GET/DELETE) |
| **URL** | Target endpoint (http:// or https://) |
| **Payload Type** | `json`, `form`, or `none` |
| **User Payload** | Custom JSON body (merged with event data) |
| **Custom Headers** | Key-value header pairs (Headers tab) |
| **Query Params** | Key-value URL parameters (Query Params tab) |
| **Authentication** | None, Basic, Bearer, or Digest |
| **Timeout** | Request timeout in milliseconds |
| **Include Event Data** | Merge Milestone event data into the payload |
| **Skip Cert Validation** | Disable HTTPS certificate verification |

## Authentication

| Type | Header Sent | Credentials Format |
|---|---|---|
| **None** | *(no auth header)* | — |
| **Basic** | `Authorization: Basic <base64>` | `username:password` |
| **Bearer** | `Authorization: Bearer <token>` | Token value |
| **Digest** | *(negotiated automatically)* | `username:password` |

## Rule Action

The plugin registers one action: **Execute HTTP Request**. The XProtect Rules engine lets you choose the scope:

- **Specific request** — select an individual HTTP Request item
- **All in a folder** — select a Request Folder; all enabled requests execute
- **All requests** — select "ALL HTTP Requests" to execute every enabled request

This targeting is handled natively by the Milestone Rules engine.

## JSON Payload

When **Include Milestone event data** is enabled, your custom payload is merged with the event:

```json
{
  "alert_type": "motion",
  "zone": "parking_lot",
  "Event": {
    "EventHeader": {
      "ID": "e0d6726f-fa1a-4ade-b1c0-fb01e1a175bf",
      "Timestamp": "2026-03-09T06:16:58.258Z",
      "Type": "System Event",
      "Name": "Motion Detected",
      "Message": "Motion Detected",
      "Priority": 1,
      "Source": {
        "Name": "Camera 512",
        "FQID": {
          "ObjectId": "35f564f6-4af8-4f59-a1b0-3e0eaf88ac04",
          "Kind": "5135ba21-f1dc-4321-806a-6ce2017343c0"
        }
      }
    }
  },
  "Site": {
    "ServerHostname": "acs",
    "AbsoluteUri": "https://acs/"
  }
}
```

Your keys (`alert_type`, `zone`) come first, then `Event` and `Site` are appended automatically.

## Events

The plugin fires analytics events that can be used in further rules:

| Event | Description |
|---|---|
| HTTP Request Executed | Request completed successfully |
| HTTP Request Failed | Request failed (timeout, connection error, HTTP error) |

## vs. Milestone Webhooks

| Feature | Milestone Webhooks | HTTP Requests Plugin |
|---|---|---|
| HTTP Methods | POST only | GET, POST, PUT, DELETE, PATCH |
| Custom payload | No (fixed schema) | Yes (fully customizable + event merge) |
| Custom headers | No | Yes |
| Authentication | OAuth 2.0 | Basic, Bearer, Digest |
| Cert validation skip | No | Yes (per request) |
| Batch execution | No | Yes (target folder or all from one rule) |
| Result events | No | Yes (success/failure for chaining) |
| XProtect version | 2023 R3+ (add-on license) | Any MIP SDK version, no extra license |

## Architecture

| Component | Environment | Role |
|---|---|---|
| Admin UI | Management Client | Configure request folders and individual requests |
| Background Plugin | Event Server | Execute HTTP requests when rules fire, log results |

## Troubleshooting

| Problem | Fix |
|---|---|
| Requests not executing | Verify a rule targets the request/folder and the Event Server is running |
| HTTPS connection failed | Enable **Skip cert validation** for self-signed certificates |
| Timeout errors | Increase the timeout value (default: 10000ms) |
| Auth errors (401/403) | Verify credentials format matches the auth type |
| No event data in payload | Ensure **Include Milestone event data** is checked |

</div>

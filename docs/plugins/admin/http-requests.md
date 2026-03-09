<div class="show-title" markdown>

# HTTP Requests

HTTP requests that are more powerful and flexible.

## Quick Start

1. Open the **Management Client**
2. Navigate to the **HTTP Requests** node in the sidebar
3. Right-click and **Create New** to add a Request Folder
4. Expand the folder, right-click and **Create New** to add an HTTP Request
5. Configure method, URL, payload, authentication, and headers
6. Save the configuration
7. Create a **Rule** with the **Execute HTTP Request** action - target a specific request, all in a folder, or all requests

## Request Configuration

| Property | Description |
|---|---|
| **Method** | GET, POST, PUT, DELETE, PATCH (body hidden for GET/DELETE) |
| **URL** | Target endpoint (http:// or https://) |
| **Payload Type** | JSON, form-urlencoded, or none |
| **User Payload** | Custom JSON body (merged with event data when enabled) |
| **Custom Headers** | Key-value header pairs (Headers tab) |
| **Query Params** | Key-value URL parameters (Query Params tab) |
| **Authentication** | None, Basic, Bearer, or Digest |
| **Timeout** | Request timeout in milliseconds (default: 10000) |
| **Include Event Data** | Merge Milestone event data into the payload |
| **Skip Cert Validation** | Disable HTTPS certificate verification |
| **Enabled** | Enable/disable individual requests |

## Authentication

| Type | Header Sent | Credentials Format |
|---|---|---|
| **None** | *(no auth header)* | - |
| **Basic** | `Authorization: Basic <base64>` | Username + Password |
| **Bearer** | `Authorization: Bearer <token>` | Token value |
| **Digest** | *(negotiated automatically)* | Username + Password |

## Rule Action

The plugin registers one action: **Execute HTTP Request**. The XProtect Rules engine lets you choose the scope:

- **Specific request** - select an individual HTTP Request item
- **All in a folder** - select a Request Folder; all enabled requests execute
- **All requests** - select "ALL HTTP Requests" to execute every enabled request

This targeting is handled natively by the Milestone Rules engine.

## JSON Payload

When **Include Milestone event data** is enabled, your custom payload is merged with the event:

```json
{
  "alert_type": "motion",
  "zone": "parking_lot",
  "Event": {
    "EventHeader": {
      "ID": "2b09afeb-8676-4a05-b415-4ffcc9b2944e",
      "Timestamp": "2026-03-09T08:38:41.0760000Z",
      "Type": "System Event",
      "Name": "Motion Detected",
      "Message": "Motion Detected",
      "Priority": 1,
      "CustomTag": "",
      "Source": {
        "Name": "RTSP Source (10.0.0.48) - Channel 1",
        "FQID": {
          "ObjectId": "57a5944c-0a55-4e62-bb6c-e87b527fa8cd",
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

### GET request with query parameters

For GET and DELETE requests, the body section is hidden. Use the **Query Params** tab to add key-value parameters, or include them directly in the URL. No body is sent for GET/DELETE requests.

## Events

The plugin fires events that can be used in further rules:

| Event | Description |
|---|---|
| HTTP Request Executed | Request completed successfully |
| HTTP Request Failed | Request failed (timeout, connection error, HTTP error) |

## vs. Webhooks

| Feature | Milestone Webhooks | HTTP Requests Plugin |
|---|---|---|
| HTTP Methods | POST only | GET, POST, PUT, DELETE, PATCH |
| Custom JSON payload | No (fixed schema) | Yes (fully customizable + event merge) |
| Custom HTTP headers | No | Yes (key-value pairs) |
| Authentication | OAuth 2.0 | Basic, Bearer, Digest |
| Cert validation skip | No | Yes (per request) |
| Configurable timeout | No | Yes (per request) |
| Query parameter editor | Manual (in URL) | Yes (dedicated key-value editor) |
| Postman-like UI | No | Yes (method, URL, headers, body, auth, test, duplicate) |
| Result events | No | Yes (success/failure for chaining rules) |
| System Log entries | Limited | Yes (per-request logging) |
| Payload types | JSON (fixed) | JSON, form-urlencoded, or none |

## Logging

### Milestone System Log

All request executions are logged to the Milestone System Log (*Logs > System Log* in the Management Client):

- **Request Executed** - method, URL, status code, elapsed time
- **Request Failed** - method, URL, error message

### Event Server Log Files

Detailed plugin logs at: `C:\ProgramData\Milestone\XProtect Event Server\logs\MIPLogs\MIP<date>.log`

## Troubleshooting

| Problem | Fix |
|---|---|
| Requests not executing | Verify a rule targets the request/folder and the Event Server is running |
| HTTPS connection failed | Enable **Skip cert validation** for self-signed certificates |
| Timeout errors | Increase the timeout value (default: 10000ms) |
| Auth errors (401/403) | Verify credentials format matches the auth type |
| No event data in payload | Ensure **Include Milestone event data** is checked |
| JSON validation fails | Check syntax in the body editor - the plugin validates JSON before sending |

</div>

# Flatline Bug-Filing API

This document explains how an external tool, agent, or LLM can file bugs into
a Flatline instance over HTTP. Paste this file (or its contents) into a system
prompt to give an LLM everything it needs to file a bug.

## Server URL

```
<FILL IN YOUR FLATLINE BASE URL, e.g. https://flatline.example.com:5443>
```

All paths below are relative to this base URL. HTTPS uses a self-signed
certificate by default — clients may need to skip verification (e.g.
`curl -k`, or set `rejectUnauthorized: false` in Node).

## Authentication

External requests authenticate with an API key passed in a request header:

```
X-API-Key: flk_<rest-of-key>
```

API keys are created by a Flatline admin under **Settings → API Keys**.
Each key is owned by a Flatline user; bugs filed with the key are recorded
as created by that user. Keys are shown once at creation; only a SHA-256
hash and a prefix are stored server-side.

## Endpoints

All endpoints require the `X-API-Key` header. Endpoints under
`/api/external/` are the only endpoints that accept API-key auth; the
session-cookie endpoints under `/api/...` (used by the web UI) still
require a logged-in user.

| Method | Path                                | Purpose                              |
|--------|-------------------------------------|--------------------------------------|
| GET    | `/api/external/projects`            | List projects (id, name, etc.).      |
| POST   | `/api/external/bugs`                | Create a new bug.                    |
| GET    | `/api/external/bugs`                | List bugs (filterable).              |
| GET    | `/api/external/bugs/{id}`           | Fetch one bug by id.                 |
| PUT    | `/api/external/bugs/{id}`           | Change a bug's status or assignee.   |
| POST   | `/api/external/bugs/{id}/comments`  | Post a comment on a bug.             |
| GET    | `/api/external/bugs/{id}/comments`  | List comments on a bug.              |
| GET    | `/api/external/bugs/{id}/related`   | List bugs related to a bug.          |

## Create a bug

```
POST /api/external/bugs
Content-Type: application/json
X-API-Key: flk_<rest-of-key>
```

### Request body

| Field              | Type    | Required | Notes                                                                 |
|--------------------|---------|----------|-----------------------------------------------------------------------|
| `Title`            | string  | yes      | Non-empty.                                                            |
| `Description`      | string  | no       | Plain text. May contain newlines. Defaults to empty.                  |
| `Priority`         | string  | no       | One of the priority enum values below. Defaults to `Normal`.          |
| `ProjectId`        | integer | yes      | Must be `> 0` and refer to an existing project.                       |
| `AssignedTo`       | integer | no       | User ID. Omit or set to `0` for unassigned.                           |
| `FoundInVersionId` | integer | no       | Version ID. Must belong to `ProjectId` if non-zero. `0` = none.       |
| `FixedInVersionId` | integer | no       | Version ID. Must belong to `ProjectId` if non-zero. `0` = none.       |

Field names are case-sensitive and match the JSON wire names exactly.

Newly created bugs always start with `Status = Open`. To change status,
update the bug through the authenticated UI/API; the external endpoint
does not accept a status field.

### Enum values

`Priority` (string, case-sensitive):

| Value      | Meaning                              |
|------------|--------------------------------------|
| `Low`      | Nice to have, not blocking anything. |
| `Normal`   | Default. Routine bug.                |
| `High`     | Hurting workflows, fix soon.         |
| `Critical` | Blocking / data loss / security.     |

`Status` (returned in responses, not accepted on create):

| Value         | Meaning                                                                                  |
|---------------|------------------------------------------------------------------------------------------|
| `Open`        | Not yet started.                                                                         |
| `InProgress`  | Someone is working on it.                                                                |
| `NeedsReview` | Active triage state — unverified issue that needs investigation before it moves on.      |
| `Resolved`    | Believed fixed, awaiting verification.                                                   |
| `Closed`      | Verified fixed / closed out.                                                             |
| `WontFix`     | Closed without a fix; out of scope.                                                      |
| `AsDesigned`  | Terminal — reported behavior is actually intentional and was a misunderstanding.         |

### Success response

`200 OK` with the created bug as JSON, e.g.

```json
{
  "Id": 142,
  "ProjectId": 3,
  "Title": "Null reference in InvoiceRenderer.Format",
  "Description": "Stack: ...",
  "Status": "Open",
  "Priority": "High",
  "CreatedBy": 7,
  "CreatedByDisplayName": "API Bot",
  "AssignedTo": 0,
  "AssignedToDisplayName": null,
  "FoundInVersionId": 0,
  "FixedInVersionId": 0,
  "CreatedAt": "2026-05-23T18:04:11.1234567Z",
  "UpdatedAt": "2026-05-23T18:04:11.1234567Z"
}
```

### Error responses

| Status | Body                                                       | Cause                                  |
|--------|------------------------------------------------------------|----------------------------------------|
| 400    | `{"error":"Body is required."}`                            | Empty/invalid JSON body.               |
| 400    | `{"error":"Title is required."}`                           | Missing/blank `Title`.                 |
| 400    | `{"error":"Project is required."}`                         | `ProjectId` missing or `<= 0`.         |
| 400    | `{"error":"Project not found."}`                           | `ProjectId` does not exist.            |
| 400    | `{"error":"Found-in version does not belong ..."}`         | Version ID belongs to a different project. |
| 400    | `{"error":"Fixed-in version does not belong ..."}`         | Version ID belongs to a different project. |
| 401    | `{"error":"Invalid or missing API key."}`                  | `X-API-Key` header missing or unknown. |

## List bugs

```
GET /api/external/bugs[?<filters>]
X-API-Key: flk_<rest-of-key>
```

Returns a JSON array of bug objects in the same shape as the create
response (above). Newest first by default.

### Query parameters

All parameters are optional. Strings are case-sensitive enum values.
Multiple values for `status`, `priority`, and `assignedTo` are passed as
comma-separated lists.

| Parameter        | Type        | Notes                                                                 |
|------------------|-------------|-----------------------------------------------------------------------|
| `status`         | enum list   | e.g. `status=Open,InProgress`. See enum table below.                  |
| `priority`       | enum list   | e.g. `priority=High,Critical`.                                        |
| `assignedTo`     | int list    | User IDs. Combine with `unassigned=true` to include unassigned bugs.  |
| `unassigned`     | `true`      | If set, includes bugs with no assignee.                               |
| `createdBy`      | integer     | Filter by creator user ID.                                            |
| `projectId`      | integer     | Filter by project ID.                                                 |
| `createdSince`   | ISO-8601    | Bugs created at or after this time (UTC).                             |
| `updatedSince`   | ISO-8601    | Bugs updated at or after this time (UTC).                             |
| `excludeClosed`  | `true`      | If set, drops bugs in `Closed` status.                                |
| `search`         | string      | Case-insensitive substring match against `Title` (LIKE-escaped).      |
| `sort`           | string      | One of `priority`, `status`, `updated`. Default: newest created first.|
| `limit`          | integer     | Max rows in this response. Default `50`, capped at `200`.             |
| `offset`         | integer     | Rows to skip before returning. Default `0`. Use with `limit` to page. |

### Examples

List every open or in-progress bug in project 1:

```bash
curl -k "https://<your-flatline-host>:5443/api/external/bugs?projectId=1&status=Open,InProgress" \
  -H "X-API-Key: flk_REPLACE_ME"
```

List high/critical bugs created in the last 24 hours:

```bash
curl -k "https://<your-flatline-host>:5443/api/external/bugs?priority=High,Critical&createdSince=$(date -u -d '24 hours ago' +%Y-%m-%dT%H:%M:%SZ)" \
  -H "X-API-Key: flk_REPLACE_ME"
```

### Errors

| Status | Body                                                  | Cause                                  |
|--------|-------------------------------------------------------|----------------------------------------|
| 400    | `{"error":"Invalid status filter."}`                  | Unknown value in `status=`.            |
| 400    | `{"error":"Invalid priority filter."}`                | Unknown value in `priority=`.          |
| 400    | `{"error":"Invalid assignedTo filter."}`              | Non-integer in `assignedTo=`.          |
| 400    | `{"error":"Invalid createdBy filter."}`               | Non-integer in `createdBy=`.           |
| 400    | `{"error":"Invalid projectId filter."}`               | Non-integer in `projectId=`.           |
| 401    | `{"error":"Invalid or missing API key."}`             | `X-API-Key` header missing or unknown. |

## Fetch one bug

```
GET /api/external/bugs/{id}
X-API-Key: flk_<rest-of-key>
```

Returns the full bug object for the given numeric id. 404 if the id does
not exist; 401 if the key is invalid.

```bash
curl -k "https://<your-flatline-host>:5443/api/external/bugs/42" \
  -H "X-API-Key: flk_REPLACE_ME"
```

## Change a bug's status or assignee

```
PUT /api/external/bugs/{id}
Content-Type: application/json
X-API-Key: flk_<rest-of-key>
```

Updates `Status`, `AssignedTo`, or both. Other fields (title, priority,
project, versions) still require the session-cookie `PUT /api/bugs/{id}`
from the web UI.

### Request body

| Field        | Type    | Required                         | Notes                                                                 |
|--------------|---------|----------------------------------|-----------------------------------------------------------------------|
| `Status`     | string  | one of Status / AssignedTo, yes  | One of the status enum values. Omit (or send `""`) to leave unchanged.|
| `AssignedTo` | integer | one of Status / AssignedTo, yes  | User id. `0` unassigns. Omit (or send `-1`) to leave unchanged.       |

At least one of `Status` or `AssignedTo` must be a real change. A body
with neither (or both at sentinel values) returns 400.

### Response

`200 OK` with the updated bug object on success, same shape as the
create/get responses.

### Errors

| Status | Body                                                                 | Cause                                     |
|--------|----------------------------------------------------------------------|-------------------------------------------|
| 400    | `{"error":"Body is required."}`                                      | Empty/invalid JSON body.                  |
| 400    | `{"error":"Nothing to update. Provide Status, AssignedTo, or both."}`| Body omits both fields.                   |
| 400    | `{"error":"Invalid status."}`                                        | `Status` is not one of the enum values.   |
| 400    | `{"error":"AssignedTo user not found."}`                             | `AssignedTo` > 0 but no such user id.     |
| 401    | `{"error":"Invalid or missing API key."}`                            | `X-API-Key` header missing or unknown.    |
| 404    | `{"error":"Bug not found."}`                                         | No bug with that id.                      |

### Examples

Close a bug:

```bash
curl -k -X PUT "https://<your-flatline-host>:5443/api/external/bugs/42" \
  -H "Content-Type: application/json" \
  -H "X-API-Key: flk_REPLACE_ME" \
  -d '{"Status":"Closed"}'
```

Assign a bug to user 2:

```bash
curl -k -X PUT "https://<your-flatline-host>:5443/api/external/bugs/42" \
  -H "Content-Type: application/json" \
  -H "X-API-Key: flk_REPLACE_ME" \
  -d '{"AssignedTo":2}'
```

Unassign and close in one call:

```bash
curl -k -X PUT "https://<your-flatline-host>:5443/api/external/bugs/42" \
  -H "Content-Type: application/json" \
  -H "X-API-Key: flk_REPLACE_ME" \
  -d '{"Status":"Closed","AssignedTo":0}'
```

## Post a comment on a bug

```
POST /api/external/bugs/{id}/comments
Content-Type: application/json
X-API-Key: flk_<rest-of-key>
```

Adds a comment to the bug. The comment is recorded as authored by the
user who owns the API key. Comments render markdown in the UI, so you
can paste PR links, fenced code blocks, etc.

### Request body

| Field  | Type   | Required | Notes                              |
|--------|--------|----------|------------------------------------|
| `Text` | string | yes      | Non-empty. May contain newlines.   |

### Response

`200 OK` with the created comment object:

```json
{
  "Id": 17,
  "BugId": 42,
  "UserId": 2,
  "Text": "Fixed in PR #123.",
  "CreatedAt": "2026-05-23T18:04:11.1234567Z",
  "Username": "api-bot",
  "DisplayName": "API Bot"
}
```

The bug's `UpdatedAt` is also bumped server-side.

### Errors

| Status | Body                                          | Cause                                  |
|--------|-----------------------------------------------|----------------------------------------|
| 400    | `{"error":"Comment text is required."}`       | Body missing or `Text` empty.          |
| 401    | `{"error":"Invalid or missing API key."}`     | `X-API-Key` header missing or unknown. |
| 404    | `{"error":"Bug not found."}`                  | No bug with that id.                   |

### Example

```bash
curl -k -X POST "https://<your-flatline-host>:5443/api/external/bugs/42/comments" \
  -H "Content-Type: application/json" \
  -H "X-API-Key: flk_REPLACE_ME" \
  -d '{"Text":"Fixed in PR https://github.com/therobm/Flatline/pull/123."}'
```

## List comments on a bug

```
GET /api/external/bugs/{id}/comments
X-API-Key: flk_<rest-of-key>
```

Returns every comment on the bug as a JSON array, ordered oldest-first
by `CreatedAt`. Each entry has the same shape as the create-comment
response (above). An unknown bug id returns an empty array rather than
a 404.

### Errors

| Status | Body                                          | Cause                                  |
|--------|-----------------------------------------------|----------------------------------------|
| 401    | `{"error":"Invalid or missing API key."}`     | `X-API-Key` header missing or unknown. |

### Example

```bash
curl -k "https://<your-flatline-host>:5443/api/external/bugs/42/comments" \
  -H "X-API-Key: flk_REPLACE_ME"
```

## List related bugs

```
GET /api/external/bugs/{id}/related
X-API-Key: flk_<rest-of-key>
```

Returns a JSON array of every bug related to `{id}` (the relation is
symmetric — both directions are stored), ordered by id ascending. Each
entry is a compact summary, not the full bug body. Useful for "before I
work on this bug, what else has been linked to it" lookups.

### Response

```json
[
  {
    "Id": 17,
    "Title": "Album cover art doesn't refresh after rescan",
    "Status": "Open",
    "Priority": "Normal"
  }
]
```

`Status` and `Priority` are the same enum strings as elsewhere in the
API. An unknown bug id returns an empty array rather than a 404.

### Errors

| Status | Body                                          | Cause                                  |
|--------|-----------------------------------------------|----------------------------------------|
| 401    | `{"error":"Invalid or missing API key."}`     | `X-API-Key` header missing or unknown. |

### Example

```bash
curl -k "https://<your-flatline-host>:5443/api/external/bugs/42/related" \
  -H "X-API-Key: flk_REPLACE_ME"
```

## List projects

```
GET /api/external/projects
X-API-Key: flk_<rest-of-key>
```

Returns a JSON array of every project the server knows about, ordered
by name. Use this when you only know a project *name* and need to
resolve it to the numeric `ProjectId` that the bug endpoints require.

### Response

```json
[
  {
    "Id": 1,
    "Name": "Flatline",
    "CreatedAt": "2026-05-23T08:00:00.0000000Z",
    "VersionCount": 0
  }
]
```

### Errors

| Status | Body                                          | Cause                                  |
|--------|-----------------------------------------------|----------------------------------------|
| 401    | `{"error":"Invalid or missing API key."}`     | `X-API-Key` header missing or unknown. |

### Example

```bash
curl -k "https://<your-flatline-host>:5443/api/external/projects" \
  -H "X-API-Key: flk_REPLACE_ME"
```

## Discovering version IDs

The bug endpoints accept optional `FoundInVersionId` / `FixedInVersionId`.
Version IDs are not currently exposed on the external API; an admin can
read them from the Settings → Projects UI or query the SQLite database:

```sql
SELECT id, project_id, name FROM versions;
```

## Working example (curl)

```bash
curl -k -X POST "https://<your-flatline-host>:5443/api/external/bugs" \
  -H "Content-Type: application/json" \
  -H "X-API-Key: flk_REPLACE_ME" \
  -d '{
        "Title": "Null reference in InvoiceRenderer.Format",
        "Description": "Repro: open invoice #42 with no line items.\nStack trace: ...",
        "Priority": "High",
        "ProjectId": 3,
        "FoundInVersionId": 0,
        "FixedInVersionId": 0,
        "AssignedTo": 0
      }'
```

`-k` skips TLS verification for the self-signed cert; remove it if the server
uses a trusted certificate.

## Minimal request

The smallest valid body is just a title and a project:

```json
{ "Title": "Login button does nothing", "ProjectId": 3 }
```

`Priority` defaults to `Normal`, `Description` defaults to empty, and the bug
is created unassigned with `Status = Open`.

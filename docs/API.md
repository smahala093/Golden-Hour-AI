# API contract

## Scope and discovery

The ASP.NET Core application exposes versioned JSON endpoints under `/api/v1`, a same-origin SignalR hub at `/hubs/emergency`, and unversioned operational health endpoints. In Development, OpenAPI is available at `/swagger/v1/swagger.json` with Swagger UI at `/swagger`; it is intentionally not exposed by the current Production pipeline.

This document describes the checked-in prototype contract. Generated OpenAPI and integration tests are the executable HTTP authority. It is not a clinical interface and no endpoint confirms emergency-service contact without explicit user/provider evidence.

## Conventions

- HTTPS is required outside localhost. Production browser calls are same-origin.
- JSON uses camel-case properties and snake-case enum strings, for example `chest_pain` and `hospital_handover`.
- Timestamps are UTC ISO 8601. IDs and concurrency tokens are UUIDs.
- Request/response media type is `application/json` except the voice upload.
- Send `X-Correlation-ID` with 1–80 characters from `[A-Za-z0-9._-]`, or the server generates one. The response echoes it.
- Errors use RFC 7807-compatible `application/problem+json`; validation adds an `errors` map.
- Never place profile/emergency content in a URL, correlation/idempotency key, log label, or filename.
- Cancellation, request-size limits, rate limits, resource authorization, and optimistic concurrency can terminate a request without applying the mutation.

Example error:

```json
{
  "type": "https://httpstatuses.io/409",
  "title": "Concurrent update conflict",
  "status": 409,
  "detail": "The task was updated by another participant.",
  "correlationId": "demo-safe-correlation-id"
}
```

Common status codes:

| Status | Meaning |
| --- | --- |
| `200` / `201` / `202` / `204` | Read/update, create, accepted callback, or successful no-content operation |
| `400` | Boundary/domain validation failed |
| `401` | Browser authentication missing/expired/rejected |
| `403` | Authenticated principal/grant cannot perform this resource operation or cross-site mutation rejected |
| `404` | Resource not found (may also avoid resource enumeration) |
| `409` | Invalid state transition, duplicate conflict, or optimistic concurrency failure |
| `413` / `415` | Upload/body too large or unsupported media type |
| `423` | Account temporarily locked |
| `429` | Rate limit exceeded |
| `500` / `503` | Safe unexpected failure or dependency not ready; no external action should be inferred |

## Browser authentication

Register/login responses set two `Secure`, `HttpOnly`, `SameSite=Strict` cookies in HTTPS environments:

| Cookie | Path | Default lifetime | Use |
| --- | --- | --- | --- |
| `gh_access` | `/` | 10 minutes | JWT access credential |
| `gh_refresh` | `/api/v1/auth` | 7 days | Opaque rotating refresh credential |

The API never returns either raw token in JSON and browser code must not copy credentials into local/session storage. Browser `fetch` must use same-origin or `credentials: 'include'` during the allowed localhost development origin.

Unsafe cookie-authenticated requests are same-origin only. The current prototype validates the request origin boundary; production requires the complete CSRF controls and tests described in [SECURITY.md](SECURITY.md). Do not call the API cross-origin with credentials.

Rate limits are fixed-window prototype defaults: authentication 10/minute per remote IP, bystander/webhook 30/minute per remote IP, and global 180/minute per authenticated user or IP. Limits are configurable implementation details, not capacity guarantees.

## Authentication endpoints

| Method | Path | Auth | Request / result |
| --- | --- | --- | --- |
| `POST` | `/api/v1/auth/register` | Anonymous | `RegisterRequest`; creates Identity user/profile and cookies; `201 AuthUserResponse` |
| `POST` | `/api/v1/auth/login` | Anonymous | `LoginRequest`; cookies; `200 AuthUserResponse` |
| `POST` | `/api/v1/auth/refresh` | Refresh cookie | Rotates refresh family; `200 { accessExpiresAtUtc }`; reuse rejects/revokes family |
| `POST` | `/api/v1/auth/logout` | Access cookie | Revokes supplied/current refresh credential and clears cookies; `204` |
| `GET` | `/api/v1/auth/me` | Access cookie | Minimal current user identity/language |

Requests:

```json
{
  "email": "person@example.test",
  "password": "supply-through-a-secure-input",
  "preferredLanguage": "hi"
}
```

```json
{
  "email": "person@example.test",
  "password": "supply-through-a-secure-input"
}
```

Passwords require at least 12 characters with upper/lowercase, digit, and non-alphanumeric characters. Five failed attempts lock a new account for the configured 15-minute prototype period. Responses avoid revealing invalid-password versus unknown-user state.

## Profile and readiness

| Method | Path | Auth | Result |
| --- | --- | --- | --- |
| `GET` | `/api/v1/profile` | User | `ProfileResponse` or `404` |
| `PUT` | `/api/v1/profile` | User | Validates/replaces the caller's profile graph; `ProfileResponse` |
| `GET` | `/api/v1/readiness` | User | Deterministic score/check list; no AI calculation |

Profile update shape:

```json
{
  "fullName": "Fictional Person",
  "dateOfBirth": "1962-04-15",
  "bloodGroup": null,
  "preferredLanguage": "hi",
  "responseMode": "both",
  "insuranceDetails": null,
  "doctorContact": null,
  "allergyStatusCompleted": true,
  "medicationStatusCompleted": true,
  "locationPermissionReviewed": true,
  "reviewed": true,
  "contacts": [
    {
      "name": "Fictional Contact",
      "relationship": "family",
      "phoneNumber": "+910000000000",
      "isVerified": false
    }
  ],
  "allergies": [{ "name": "self-reported example" }],
  "conditions": [],
  "medications": [],
  "procedures": [],
  "preferredHospital": null,
  "sharing": {
    "shareName": true,
    "shareApproximateAge": true,
    "shareAllergies": true,
    "shareConditions": false,
    "shareMedications": false,
    "shareEmergencyContact": true,
    "reviewed": true
  }
}
```

Medical fields remain self-reported. The bystander projection is built server-side from explicit sharing flags; insurance/doctor/private records are not in that contract.

## Emergency sessions

### Create and read

| Method | Path | Auth | Request / result |
| --- | --- | --- | --- |
| `POST` | `/api/v1/sessions/` | Optional in prototype | `CreateSessionRequest`; `201 SessionResponse` |
| `GET` | `/api/v1/sessions/{sessionId}` | Owner/participant | Authoritative `SessionResponse` |

```json
{
  "patientRelationship": "family",
  "selectedCategory": "chest_pain",
  "typedLocation": "Fictional Jaipur-area location",
  "countryCode": "IN"
}
```

Supported relationships are `self`, `family`, `bystander`, and `unknown`. Categories are `chest_pain`, `breathing_difficulty`, `fall_or_injury`, `unconscious`, `seizure`, `heavy_bleeding`, `road_accident`, `allergic_reaction`, `child_emergency`, `other`, and `unknown`.

Authenticated creation snapshots the caller's current profile and adds the owner participant. Anonymous creation is accepted by the current route, but subsequent session reads/mutations require owner/participant authorization. Therefore anonymous continuation needs a separate scoped grant before this route can satisfy the complete bystander-start journey; an untrusted session UUID alone never authorizes access. The positive bystander E2E test is a release gate, not an assumed capability.

`SessionResponse` contains `id`, status/category/relationship, emergency number, UTC creation/update, original input/language, separate normalized transcript, structured incident facts, uncertainty, selected protocol/version/actions, tasks, ordered timeline, participants, locations, and the session concurrency token. Missing/unknown values remain explicit and are not diagnosed.

### Incident and voice

| Method | Path | Auth | Request / result |
| --- | --- | --- | --- |
| `POST` | `/api/v1/sessions/{sessionId}/incident` | Owner/participant | `SubmitIncidentRequest`; updated `SessionResponse` |
| `POST` | `/api/v1/sessions/{sessionId}/voice` | Owner/participant | Multipart transcription then incident pipeline; updated `SessionResponse` |
| `POST` | `/api/v1/sessions/{sessionId}/answers` | Owner/participant | Records one critical answer as timeline data |

```json
{
  "originalText": "मेरे पिताजी को अचानक सीने में दर्द और बहुत पसीना आ रहा है।",
  "selectedLanguage": "hi",
  "fallbackCategory": "chest_pain"
}
```

The voice request is `multipart/form-data` with one `audio` file and optional `languageHint`. Current maximum size is 5 MiB; allowlisted declared content types are `audio/webm`, `audio/wav`, `audio/x-wav`, `audio/mpeg`, `audio/mp4`, and `audio/ogg`. The product capture additionally enforces at most 30 seconds. Filenames are not trusted.

```text
audio=<binary recording>
languageHint=hi
```

AI/provider failure returns a bounded static/manual-category result where possible; it never hides the call action or implies an external action succeeded.

### Timeline and location

| Method | Path | Auth | Body |
| --- | --- | --- | --- |
| `POST` | `/api/v1/sessions/{sessionId}/timeline` | Owner/participant | `{ type, message, idempotencyKey }` |
| `POST` | `/api/v1/sessions/{sessionId}/locations` | Owner/participant | `{ latitude, longitude, description, consentProvided, idempotencyKey }` |

Idempotency keys are opaque, stable for one logical client command, and must not contain sensitive values. Replaying the same key returns authoritative state without a second timeline/location effect. Location rejects when `consentProvided` is false. Latitude/longitude are optional so typed description remains available after permission denial.

Only allowlisted noncritical timeline commands may be queued by the offline client. Emergency calls, location without current consent, sharing/token changes, invitations, notifications, and critical state transitions are never queued/retried automatically.

### Tasks

| Method | Path | Auth | Body / concurrency |
| --- | --- | --- | --- |
| `POST` | `/api/v1/sessions/{sessionId}/tasks` | Owner/participant | `{ taskCode, assignedParticipantId }` |
| `PATCH` | `/api/v1/sessions/{sessionId}/tasks/{taskId}` | Owner/participant | `{ status, assignedParticipantId, concurrencyToken }` |

Allowed task codes:

`stay-with-patient`, `call-emergency-services`, `bring-medical-records`, `bring-identification`, `unlock-entry`, `guide-responder`, `contact-hospital`, `care-for-dependants`.

Statuses serialize as `suggested`, `assigned`, `accepted`, `declined`, and `completed`. Unsupported task code/transition is rejected. A stale concurrency token returns `409`; refetch the session before presenting retry/reassignment. Duplicate critical tasks do not create a second task.

### Sharing, summaries, and close

| Method | Path | Auth | Result |
| --- | --- | --- | --- |
| `POST` | `/api/v1/sessions/{sessionId}/share-tokens` | Owner/participant | `{ lifetimeMinutes }`; returns raw token once, expiry, and relative path |
| `DELETE` | `/api/v1/sessions/{sessionId}/share-tokens/{tokenId}` | Owner/participant | Revoke; `204` |
| `POST` | `/api/v1/sessions/{sessionId}/summaries/{kind}` | Owner/participant | Creates `family`, `responder`, or `hospital-handover` summary |
| `POST` | `/api/v1/sessions/{sessionId}/close` | Owner only | `{ concurrencyToken }`; updated closed session |

Treat a returned share token like a password. Do not log, persist in analytics, or send to an unapproved third party. Only its hash is stored. Summary content is a source-labeled prototype with protocol version, uncertainty/confidence, and clinical-review disclaimer; it is not a diagnosis.

Close is optimistic-concurrency protected and idempotently remains closed. It does not revoke/undo an external action; token expiry/revocation and retention are separate controls.

## Anonymous bystander projection

| Method | Path | Auth | Result |
| --- | --- | --- | --- |
| `GET` | `/api/v1/bystander/{token}` | Scoped raw share token | `BystanderSessionResponse` |

The response includes only session ID/status/emergency number, approved incident facts/protocol, allowlisted profile projection, and latest location. Complete profile, insurance, private documents, raw audio, auth credentials, and unapproved fields are absent.

Token-bearing requests require `Referrer-Policy: no-referrer`, no third-party assets/analytics, TLS, log redaction, rate limiting, expiry, and revocation. Unknown/expired/revoked/tampered tokens should not be distinguishable to an attacker.

## Provider webhooks

| Method | Path | Auth | Result |
| --- | --- | --- | --- |
| `POST` | `/api/v1/webhooks/{provider}` | Signed headers | `202 WebhookResult` after verification/idempotent receipt |

Maximum raw body is 64 KiB. Required headers:

- `X-GoldenHour-Delivery-Id`: provider-unique, maximum 160 characters.
- `X-GoldenHour-Timestamp`: Unix seconds within `Webhook__AllowedClockSkewMinutes`.
- `X-GoldenHour-Signature`: lowercase/uppercase hex HMAC-SHA256, optionally prefixed `sha256=`.

Canonical signed bytes are UTF-8:

```text
<timestamp>.<exact raw request body>
```

The backend computes HMAC-SHA256 using `Webhook__SigningSecret` and compares in fixed time. It stores normalized receipt metadata/payload digest, not a complete raw body. The `(provider, deliveryId)` pair is replay/idempotency protected. Provider-specific production secrets and canonicalization rules need separate routes/configuration before enabling multiple real providers.

## SignalR

- Hub URL: `/hubs/emergency`
- Authentication: same `gh_access` browser cookie/JWT identity.
- Server group: derived internally as `emergency-session-{uuid-without-hyphens}`; clients cannot supply a group name.

Client-to-hub methods:

| Method | Arguments | Behavior |
| --- | --- | --- |
| `JoinSession` | `sessionId` UUID | Verifies owner/participant membership, joins, emits `AuthoritativeStateRequired` |
| `LeaveSession` | `sessionId` UUID | Leaves that derived group for the current connection |

Server events currently include:

| Event | Payload / client action |
| --- | --- |
| `AuthoritativeStateRequired` | `{ sessionId }`; fetch `GET /api/v1/sessions/{id}` |
| `SessionUpdated` | Lightweight status or session snapshot; always reconcile with server authority after reconnect |
| `TimelineAdded` | One ordered event; dedupe by event ID/sequence |
| `TaskUpdated` | One task snapshot; reconcile concurrency token/state |

The client enables automatic reconnect, displays an accessible connection banner, re-authenticates/rejoins after reconnect, and refetches authoritative state. When unavailable, poll REST at a bounded interval. A SignalR notification is not a durable commit or provider confirmation by itself.

## Health endpoints

| Method | Path | Auth | Contract |
| --- | --- | --- | --- |
| `GET` | `/health/live` | Anonymous | `200 { status: "healthy", utc }` if process responds; no dependency checks |
| `GET` | `/health/ready` | Anonymous | `200 { status: "ready", database: "available" }` or `503` safe unavailable state |

Health responses intentionally omit database/server names, connection strings, secrets, record counts, user data, exception text, and provider payloads. Provider availability must not make process liveness fail; it is represented separately as configured/degraded/unavailable where implemented.

## Minimal same-origin client example

```ts
const response = await fetch('/api/v1/sessions/', {
  method: 'POST',
  credentials: 'include',
  headers: {
    'Content-Type': 'application/json',
    'X-Correlation-ID': crypto.randomUUID(),
  },
  body: JSON.stringify({
    patientRelationship: 'family',
    selectedCategory: 'chest_pain',
    typedLocation: null,
    countryCode: 'IN',
  }),
});

if (!response.ok) {
  const problem = await response.json();
  // Announce a safe, plain-language failure and preserve the call action.
  throw new Error(problem.title ?? 'The session could not be created safely.');
}
```

Production client code must also carry the repository's CSRF/idempotency conventions, cancellation/timeout handling, accessible status announcement, and offline restrictions; this short example is not a replacement for the typed client.

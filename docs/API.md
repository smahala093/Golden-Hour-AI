# API contract

## Scope and discovery

The ASP.NET Core application exposes versioned JSON endpoints under `/api/v1`, a same-origin SignalR hub at `/hubs/emergency`, and unversioned operational health endpoints. In Development, OpenAPI is available at `/swagger/v1/swagger.json` with Swagger UI at `/swagger`; it is intentionally not exposed by the current Production pipeline.

This document describes the checked-in prototype contract. Generated OpenAPI and integration tests are the executable HTTP authority. It is not a clinical interface and no endpoint confirms emergency-service contact without explicit user/provider evidence.

## Conventions

- HTTPS is required outside localhost. Production browser calls are same-origin.
- JSON uses camel-case properties and snake-case enum strings, for example `chest_pain` and `hospital_handover`.
- Timestamps are UTC ISO 8601. IDs and concurrency tokens are UUIDs.
- Request/response media type is `application/json` except voice upload (`multipart/form-data`) and protocol audio (`audio/mpeg`).
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
| `429` | Rate limit exceeded |
| `500` / `503` | Safe unexpected failure or dependency not ready; no external action should be inferred |

## Public configuration

| Method | Path | Auth | Result |
| --- | --- | --- | --- |
| `GET` | `/api/v1/configuration` | Anonymous | Cacheable `{ emergencyNumber }` public bootstrap configuration |
| `GET` | `/api/v1/protocols?country=IN` | Anonymous | Cacheable reviewed demonstration-protocol catalogue for the requested country; defaults to `IN` |

The browser renders an immediate validated build-time/`112` fallback and then hydrates this endpoint without blocking the call action. The server accepts only a 2–16 character dial string containing digits with an optional leading `+`; the returned server value is authoritative once loaded.

## Browser authentication

Register/login responses set two `Secure`, `HttpOnly`, `SameSite=Strict` cookies in HTTPS environments:

| Cookie | Path | Default lifetime | Use |
| --- | --- | --- | --- |
| `gh_access` | `/` | 10 minutes | JWT access credential |
| `gh_refresh` | `/api/v1/auth` | 7 days | Opaque rotating refresh credential |

The API never returns either raw token in JSON and browser code must not copy credentials into local/session storage. Browser `fetch` must use same-origin or `credentials: 'include'` during the allowed localhost development origin.

Unsafe cookie-authenticated requests are same-origin only. The server enforces the configured origin through `Origin`/`Referer` and Fetch Metadata checks in addition to `SameSite=Strict` cookies; the precise supported topology and residual limitations are documented in [SECURITY.md](SECURITY.md). Do not call the API cross-origin with credentials.

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

Passwords require at least 12 characters with upper/lowercase, digit, and non-alphanumeric characters. Five failed attempts lock a new account for the configured 15-minute prototype period. Invalid, unknown, and locked login attempts use the same external unauthorized response; lock state is not exposed to account probes.

## Profile and readiness

| Method | Path | Auth | Result |
| --- | --- | --- | --- |
| `GET` | `/api/v1/profile` | User | `ProfileResponse` or `404` |
| `PUT` | `/api/v1/profile` | User | Validates/replaces the caller's profile graph; `ProfileResponse` |
| `POST` | `/api/v1/profile/contacts/{contactId}/verification` | User | Sends a short-lived code through the configured SMS provider and returns an HMAC-bound challenge. Development mock mode returns an explicitly labelled `developmentCode` and states that no SMS was delivered. |
| `POST` | `/api/v1/profile/contacts/{contactId}/verification/confirm` | User | `{ challenge, code }`; verifies owner, contact, current phone, expiry, and one-time nonce; marks the contact verified and returns `204` |
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

Medical fields remain self-reported. `contacts[].isVerified` is output state, not a client command: a profile update cannot promote it, and changing a phone clears prior verification. The bystander projection is built server-side from explicit sharing flags; the conditions permission also covers relevant procedures in responder/handover views. Insurance, doctor details, and private records are not in that contract.

## Emergency sessions

### Create and read

| Method | Path | Auth | Request / result |
| --- | --- | --- | --- |
| `GET` | `/api/v1/sessions?limit=20&beforeUtc=...` | Authenticated | Authorized-session history; `limit` defaults to 20 and is bounded to 1–100 |
| `POST` | `/api/v1/sessions/` | Optional | `CreateSessionRequest`; `201 SessionResponse` |
| `GET` | `/api/v1/sessions/{sessionId}` | Owner/participant | Authoritative `SessionResponse` |

```json
{
  "patientRelationship": "family",
  "selectedCategory": "chest_pain",
  "typedLocation": "Fictional Jaipur-area location",
  "countryCode": "IN",
  "useOwnerProfileForPatient": true
}
```

Supported relationships are `self`, `family`, `bystander`, and `unknown`. Categories are `chest_pain`, `breathing_difficulty`, `fall_or_injury`, `unconscious`, `seizure`, `heavy_bleeding`, `road_accident`, `allergic_reaction`, `child_emergency`, `other`, and `unknown`.

Every create request requires an opaque 16–80 character `Idempotency-Key`. Reusing the key with the same authenticated request returns the original session; reusing it with different input is rejected. Authenticated `self` creation captures an immutable, permission-projected emergency profile snapshot and adds the owner participant. An authenticated `family` request copies that stored profile only when the user explicitly sets `useOwnerProfileForPatient: true` after confirming that the profile belongs to the patient. Anonymous use of that flag is forbidden, and `bystander`/`unknown` cannot use it.

Anonymous creation returns a cryptographically random, one-time `anonymousAccessToken`. The browser keeps it only in memory and sends it in `X-Emergency-Access-Token` for that session's read, incident, voice, protocol-audio, answer, timeline, and location routes. Only its hash is stored, it expires after two hours, and the session UUID alone grants no access. Because the server cannot reconstruct a hash-only secret, replaying an anonymous create after an indeterminate/lost response returns `409`; the client must start a new command with a new key rather than assume access to the first session.

`SessionResponse` contains `id`, status/category/relationship, emergency number, UTC creation/update, original input/language, separate normalized transcript, structured incident facts, uncertainty, selected protocol/version/actions, a permission-projected immutable patient snapshot, provenance-labelled confirmed/unconfirmed observations, tasks, ordered timeline, participants, locations, and the session concurrency token. Missing/unknown values remain explicit and are not diagnosed.

### Incident and voice

| Method | Path | Auth | Request / result |
| --- | --- | --- | --- |
| `POST` | `/api/v1/sessions/{sessionId}/incident` | Owner/participant or matching anonymous session grant | `SubmitIncidentRequest`; updated `SessionResponse` |
| `POST` | `/api/v1/sessions/{sessionId}/voice` | Owner/participant or matching anonymous session grant | Multipart transcription then incident pipeline; updated `SessionResponse` |
| `POST` | `/api/v1/sessions/{sessionId}/protocol-audio` | Owner/participant or matching anonymous session grant | `audio/mpeg` generated only from the selected stored protocol |
| `POST` | `/api/v1/sessions/{sessionId}/answers` | Owner/participant or matching anonymous session grant | Applies one to three allowlisted critical answers and reselects the static protocol |

```json
{
  "originalText": "मेरे पिताजी को अचानक सीने में दर्द और बहुत पसीना आ रहा है।",
  "selectedLanguage": "hi",
  "fallbackCategory": "chest_pain"
}
```

The voice request is `multipart/form-data` with one `audio` file and optional bounded `languageHint`. Current maximum size is 5 MiB; allowlisted declared content types are `audio/webm`, `audio/wav`, `audio/x-wav`, `audio/mpeg`, `audio/mp4`, and `audio/ogg`. Declared type and container signature must agree, request/form limits apply before provider processing, and both browser capture and the post-transcription server check require a positive provider-reported duration no greater than 30 seconds. The bounded upload reaches the transcription provider before that duration metadata is available, so a production media preflight/decoder sandbox remains a cost-hardening gate. Filenames are not trusted. Incident, voice, and answer commands require an `Idempotency-Key`.

```text
audio=<binary recording>
languageHint=hi
```

AI/provider failure returns a bounded static/manual-category result where possible; it never hides the call action or implies an external action succeeded.

### Timeline and location

| Method | Path | Auth | Body |
| --- | --- | --- | --- |
| `POST` | `/api/v1/sessions/{sessionId}/timeline` | Owner/participant or matching anonymous session grant | `{ type, message, idempotencyKey }` |
| `POST` | `/api/v1/sessions/{sessionId}/locations` | Owner/participant or matching anonymous session grant | `{ latitude, longitude, description, consentProvided, idempotencyKey }` |

Idempotency keys are opaque, stable for one logical client command, and must not contain sensitive values. Replaying the same key returns authoritative state without a second timeline/location effect. Location rejects when `consentProvided` is false. Latitude/longitude are optional so typed description remains available after permission denial.

Only allowlisted noncritical timeline commands may be queued by the offline client. Emergency calls, location without current consent, sharing/token changes, invitations, notifications, and critical state transitions are never queued/retried automatically.

### Tasks

| Method | Path | Auth | Body / concurrency |
| --- | --- | --- | --- |
| `POST` | `/api/v1/sessions/{sessionId}/tasks` | Owner/caregiver | `{ taskCode, assignedParticipantId }` |
| `PATCH` | `/api/v1/sessions/{sessionId}/tasks/{taskId}` | Coordinator or assignee | `{ status, assignedParticipantId, concurrencyToken }` |

Allowed task codes:

`stay-with-patient`, `call-emergency-services`, `bring-medical-records`, `bring-identification`, `unlock-entry`, `guide-responder`, `contact-hospital`, `care-for-dependants`.

Task creation requires an opaque `Idempotency-Key` header containing 16–80 characters. A same-key replay returns the authoritative session without adding another task/timeline event. Statuses serialize as `suggested`, `assigned`, `accepted`, `declined`, and `completed`. An assignee must belong to the same session. Only the owner/caregiver may assign or reassign; an assigned participant may accept, decline, or complete their own task. Unsupported task code/transition is rejected. A stale concurrency token returns `409`; refetch the session before presenting retry/reassignment. Duplicate critical tasks do not create a second task.

### Sharing, summaries, and close

| Method | Path | Auth | Result |
| --- | --- | --- | --- |
| `POST` | `/api/v1/sessions/{sessionId}/share-tokens` | Owner | `{ lifetimeMinutes }`; returns raw token once, expiry, and fragment-only `/share#...` path |
| `DELETE` | `/api/v1/sessions/{sessionId}/share-tokens/{tokenId}` | Owner | Revoke; `204` |
| `POST` | `/api/v1/sessions/{sessionId}/participants/invitations` | Owner | One-time fragment-only participant invitation |
| `POST` | `/api/v1/sessions/{sessionId}/participants/join` | Authenticated invitee | Exchanges invitation token; does not acknowledge |
| `POST` | `/api/v1/sessions/{sessionId}/participants/{participantId}/acknowledge` | Joined participant | Explicitly acknowledges the session |
| `POST` | `/api/v1/sessions/{sessionId}/summaries/{kind}` | Owner/participant | Creates `family`, `responder`, or `hospital-handover` summary |
| `POST` | `/api/v1/sessions/{sessionId}/close` | Owner only | `{ concurrencyToken }`; updated closed session |

Treat a returned share or invitation token like a password. Do not log, persist in analytics, or send to an unapproved third party. Tokens are placed in URL fragments (which are not sent in HTTP requests), scrubbed from browser history before exchange, and stored only as hashes on the server. Share-link and participant-invitation commands require an `Idempotency-Key`, generate a new 32-byte random capability, and expose the raw fragment secret once. A same-key replay returns `409` rather than regenerating or retaining that secret; after an indeterminate response, create a fresh capability with a new key and revoke any earlier visible grant.

Each summary POST creates and persists a new English, server-generated snapshot and returns it immediately; there is not yet a summary list/latest GET endpoint. Content is allowlisted and differs deliberately by audience:

- Family: original report, confirmed/unconfirmed observations, coordination tasks, and protocol.
- Responder: permission-projected profile, latest location, reported observations, consciousness/breathing/start-time facts, and uncertainty.
- Hospital handover: chronology, original and confirmed/unconfirmed facts, permission-projected profile, protocol/languages, missing information, confidence, and disclaimer.

All kinds include the exact clinical-review notice and must not imply a diagnosis or unverified external action.

Close is optimistic-concurrency protected and an authorized replay remains closed without adding a second timeline event. A replay of the same terminal task state likewise returns the authoritative completed task without duplicating the transition. Close does not revoke/undo an external action; token expiry/revocation and retention are separate controls.

## Anonymous bystander projection

| Method | Path | Auth | Result |
| --- | --- | --- | --- |
| `GET` | `/api/v1/bystander` | `X-Emergency-Share-Token` | `BystanderSessionResponse` |
| `POST` | `/api/v1/bystander/observations` | Share-token header + `Idempotency-Key` | Bounded visible observations; `202` |
| `POST` | `/api/v1/bystander/location` | Share-token header + `Idempotency-Key` + explicit consent | Current coordinates; `202` |

The response includes only session ID, projected patient name/approximate age, category, latest location, explicitly shared allergies/conditions/medicines/contact, expiry, emergency number, and reviewed protocol. It does not include a session-status or general incident-facts field. Complete profile, insurance, private documents, raw audio, auth credentials, and unapproved fields are absent.

The authenticated and bystander location routes accept bounded typed description text and/or explicitly consented latitude/longitude coordinates. The web fallback can capture browser geolocation or accept decimal coordinates copied from a trusted map. No production visual-map, tile, reverse-geocoding, or routing adapter is active; a failed coordinate save leaves the typed landmark available and is announced to the user.

The raw secret never appears in an HTTP path or query. Token-bearing requests require `Referrer-Policy: no-referrer`, `Cache-Control: no-store, private`, no third-party assets/analytics, TLS, log redaction, rate limiting, expiry, and revocation. Unknown/expired/revoked/tampered tokens should not be distinguishable to an attacker.

## Outbound SMS gateway

When `Providers__UseMocks=true`, contact verification is development-only: no SMS is sent, and Development may return an explicitly labelled mock code. With mocks disabled, the backend requires the configured HTTPS HMAC gateway. It POSTs a bounded JSON body:

```json
{
  "requestId": "random-32-hex-character-request-id",
  "destination": "+910000000000",
  "templateCode": "contact-verification-code",
  "values": { "code": "123456" }
}
```

Required outbound headers are `X-GoldenHour-Timestamp` (Unix seconds), `X-GoldenHour-Request-Id`, and `X-GoldenHour-Signature: sha256=<lowercase HMAC-SHA256 hex>`. Canonical signed bytes are:

```text
<timestamp>.<requestId>.<exact raw JSON request body>
```

The gateway must return a bounded JSON object with exactly `messageId` and `status`; status is only `accepted` or `queued`. Those values mean the gateway accepted the request, not that a handset received it. Delivery may be recorded only from a separately verified callback. Endpoint, 2–30 second timeout, and 1–64 KiB response limit are configuration-bound; non-HTTPS/weak-secret/invalid response configuration fails closed.

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

The backend computes HMAC-SHA256 using `Webhook__SigningSecret` and compares in fixed time. It stores normalized receipt metadata/payload digest, not a complete raw body. The `(provider, deliveryId)` pair is replay/idempotency protected. The callback status allowlist is `queued`, `sent`, `delivered`, `failed`, `undelivered`, or `rejected`; this is distinct from the outbound gateway's immediate `accepted|queued` response. Provider-specific production secrets and canonicalization rules need separate routes/configuration before enabling multiple real providers.

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
| `ParticipantJoined` | Participant projection; refetch the authorized session snapshot |
| `ContactAcknowledged` | Participant acknowledgement projection; refetch authoritative state |

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
    'Idempotency-Key': crypto.randomUUID(),
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

Production client code must also carry the repository's same-origin mutation convention, cancellation/timeout handling, accessible status announcement, and offline restrictions; this short example is not a replacement for the typed client.

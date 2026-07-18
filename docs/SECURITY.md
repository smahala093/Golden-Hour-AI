# Security and privacy

## Scope

This document defines the security posture required for the Golden Hour AI prototype and its deployment assets. It is not a certification or proof of production readiness. Real-world use requires a jurisdiction-specific privacy assessment, threat model, penetration test, retention/deletion policy, clinical governance, and operating procedures.

Emergency descriptions, profile facts, audio, location, contact data, tokens, tasks, timelines, and summaries are sensitive. They must remain out of source code, URLs/query strings, analytics, ordinary logs, screenshots, test snapshots, and public issue reports.

## Trust boundaries

```mermaid
flowchart LR
    Untrusted["Untrusted browser, text, audio, filenames"]
    Edge["HTTPS + rate limits + size/type validation"]
    Auth["Authentication + CSRF + authorization"]
    App["Application commands + idempotency"]
    Data["PostgreSQL (active)"]
    AI["External AI provider boundary"]
    SMS["External HTTPS SMS gateway"]
    Callbacks["Signed provider webhooks"]
    Ops["Azure/GitHub operator boundary"]

    Untrusted --> Edge --> Auth --> App --> Data
    App -->|"minimum redacted fields"| AI
    App -->|"bounded template + HMAC"| SMS
    Callbacks -->|"HMAC, timestamp, replay check"| Edge
    Ops -->|"OIDC / managed identity / secret references"| App
```

Treat browser state, AI output, provider callbacks, filenames, realtime messages, and session IDs as attacker-controlled. Document ingestion is not implemented; any future document text must enter the same untrusted boundary. A network location, authenticated identity, or model response is not sufficient authorization.

## Data classification

| Class | Examples | Required handling |
| --- | --- | --- |
| Restricted | Auth/refresh/share tokens, signing secrets, provider keys, private documents, raw audio | Never log/cache/client-persist; encrypt in transit/at rest; strict role/resource access; short retention |
| Sensitive emergency | Profile snapshot, allergies/medicines/conditions, observations, location, timeline, summaries | Resource-scoped access; minimal projections; no URL/analytics/snapshot content; audited access |
| Operational metadata | Opaque IDs, status, timestamps, correlation ID, latency/outcome class | Minimize, retain by policy, ensure combinations do not reveal a person |
| Public reviewed content | Product shell, translations, protocol demonstration files | Integrity/version control; safe to PWA-cache; still label prototype/clinical-review status |

No production retention durations are invented here. Define lawful purpose, jurisdiction, retention, deletion, export, backup expiry, legal hold, data residency, and subject-right processes before collecting real data.

## Authentication and browser session

- ASP.NET Core Identity owns password hashing, lockout, normalized identifiers, and role membership.
- Access and refresh credentials are delivered in `Secure`, `HttpOnly`, `SameSite` cookies in shared HTTPS environments; never store JWT or refresh tokens in local/session storage, IndexedDB, Cache Storage, URLs, or JavaScript-readable cookies.
- Access lifetime is short. Refresh tokens are random, stored only as a strong hash, rotated on every use, tied to a user and token family, and revoked on logout or detected reuse.
- Reuse of an old refresh token revokes its family and produces a security audit event. Login and refresh failure responses do not reveal whether a user exists.
- Login/register/refresh endpoints use bounded per-IP/global limits, and Identity applies per-account failed-login lockout. Login/lockout responses avoid account enumeration. Registration currently returns ASP.NET Identity duplicate-identifier validation details, so a reviewed non-enumerating registration policy remains production hardening work. Account recovery is not implemented.
- Production signing keys are high entropy and supplied through a secret reference. The current HS256 token path validates issuer, audience, signature, expiry, and bounded clock skew. An operational signing-key rotation/overlap procedure remains a production release gate.

Cookie-authenticated unsafe methods are protected by the checked-in same-origin mutation boundary: `Origin`/`Referer` must match the configured public origin when present, cross-site `Sec-Fetch-Site` is rejected, and shared HTTPS cookies are `SameSite=Strict`. Integration tests cover missing/cross-site rejection and matching-origin success. This design does not currently issue a separate synchronizer antiforgery token; add one if the browser topology must support cross-site embedding, less restrictive cookie policy, or clients that cannot provide the enforced request metadata. CORS is denied by default in the same-origin deployment and never combines wildcard origins with credentials.

## Authorization

Authentication never substitutes for resource authorization. Every profile, session, task, timeline, location, summary, invitation, token, document, and hub operation evaluates the authenticated principal or validated anonymous grant against that exact resource and operation.

Roles (`User`, `Caregiver`, `Emergency participant`, `Administrator`) grant only coarse capability. Session ownership/participation and field-level sharing determine actual access. Administration does not imply routine access to private emergency content.

SignalR connections use the same identity, re-evaluate membership before joining a server-generated group name, and authorize each hub method. Clients cannot provide arbitrary group names or enumerate groups/sessions. Reconnect re-authenticates and refetches state.

## Anonymous bystander links

1. Generate at least 128 bits of randomness with a cryptographic RNG.
2. Return the raw token only once in a URL fragment, scrub the fragment before exchange, and send the secret only in the bounded `X-Emergency-Share-Token` header. Never put it in an HTTP path/query or write it to logs, analytics, referrers, or server storage.
3. Store only a keyed or memory-hard/cryptographic hash with session, scope, expiry, revocation, and creation metadata.
4. Compare hashes in constant time and return the same response shape for invalid, expired, revoked, or unknown tokens.
5. The implemented prototype rate-limits bystander attempts by remote IP and also applies the global authenticated-user-or-IP partition without locking out the emergency-call action. Token/session-aware partitions remain production hardening work.
6. Project fields through a server allowlist based on the profile snapshot and sharing preference. Insurance, full address, private documents, raw audio, auth tokens, and complete records are excluded by default.
7. Audit access metadata minimally. Anonymous writes are allowlisted, validated, consent-aware, scoped, and idempotent.

Use `Referrer-Policy: no-referrer`, `Cache-Control: no-store, private`, and no third-party resources on token-bearing pages. The UI provides explicit revoke and expiry state.

## Input and upload security

- Validate every DTO at the boundary and again enforce domain invariants; return RFC 7807 errors with correlation ID and no sensitive values.
- Use parameterized EF queries. Never construct SQL, shell commands, file paths, URLs, HTML, or log templates from untrusted strings.
- React escapes text by default; avoid `dangerouslySetInnerHTML`. Apply a restrictive Content Security Policy and encode any server-rendered value.
- Audio: a small byte limit, allowlisted MIME/container signatures, ignored filename, bounded transcriber, and no public blob access. The server requires the transcription provider to return a positive duration no greater than 30 seconds before incident processing. The provider currently sees the bounded upload before returning that metadata; a production media preflight/decoder sandbox remains a cost-hardening gate.
- Documents (future): reject path traversal, archives/bombs, active content, unsupported extensions/signatures, and prompt instructions. Store outside web root with randomized names and explicit content disposition.
- Normalize/validate Unicode without discarding the original. Protect database/log/UI limits from oversized grapheme sequences and control characters.

## API and state-change controls

- HTTPS only, HSTS outside local development, trusted forwarded-header configuration, secure response headers, and bounded request/body/form/time limits.
- Rate-limit by route risk using the implemented prototype partitions: authentication, bystander, and anonymous-start routes use remote IP; the global partition uses authenticated user ID or remote IP. A limited request returns `429`; an explicit safe `Retry-After` contract and session/token-aware partitions remain production hardening work.
- Critical commands carry an opaque idempotency key scoped to actor/resource/operation and apply state atomically. Ordinary duplicates return authoritative state without applying twice. Capability-issuing commands are the deliberate exception: anonymous grants, share links, and participant invitations persist only a hash and expose the random raw secret once, so a same-key replay returns `409` instead of retaining or deterministically regenerating the capability. After an indeterminate response, issue a fresh capability and revoke any earlier visible grant.
- Use optimistic concurrency/version checks for task/session transitions and return a conflict that prompts a state refetch.
- Durable state is committed before realtime notification. Selected external callback events also commit an outbox record in the same transaction; ordinary session mutations do not all create outbox records. Consumers and provider callbacks must remain idempotent.
- Emergency call initiated/connected and provider delivered states require explicit user/provider evidence; the application never infers success from a button click or queued request.

## Webhook security

Provider-specific endpoints must read a bounded raw body, locate a known provider/secret version, verify an HMAC signature in constant time, validate an absolute timestamp within the configured skew, and atomically reject a previously seen `(provider, deliveryId)` pair. The payload digest is retained as minimal audit metadata, not as the uniqueness key. Signature verification happens before parsing/trusting fields.

Return quickly after storing a minimal normalized delivery event/outbox job. Do not persist an entire raw payload by default. Invalid signature/timestamp/replay receive an appropriate non-revealing error; valid duplicates return the provider-required idempotent response without replaying state changes. The checked-in webhook signature is computed over the timestamp and exact raw body bytes before JSON parsing.

## AI and external providers

- OpenAI/provider keys are backend-only secrets and never use `VITE_` names.
- Send the minimum redacted fields; provider terms, region, training/retention settings, and data processing agreements require review.
- Treat model output as untrusted structured input and apply the controls in [AI_SAFETY.md](AI_SAFETY.md).
- The outbound SMS gateway's immediate status is only `accepted` or `queued`; neither means handset delivery. Signed callbacks accept the separate allowlist `queued`, `sent`, `delivered`, `failed`, `undelivered`, or `rejected`. A local mock is visibly non-production and does not imply external delivery.
- AI calls use bounded timeouts/retries and deterministic fallback. The current adapter does not implement a circuit breaker; provider health monitoring and circuit policy remain deployment gates.
- Real-provider SMS uses a configured HTTPS endpoint, a random request ID, timestamped HMAC over `timestamp.requestId.rawBody`, bounded templates/values, a strict response schema, and a bounded response body. `accepted`/`queued` means request acceptance only, never handset delivery; delivery requires a signed callback.

## PWA and client storage

The service worker precaches hashed shell assets and reviewed public translations/protocols only. It must not cache authenticated API responses, token-bearing bystander pages, audio, private documents, profile/session responses, or error bodies containing user input. Clear user-scoped local data on logout/revocation.

An opt-in offline card stores only explicitly approved minimal fields. The client encrypts it with AES-GCM using a generated non-extractable `CryptoKey` held in IndexedDB, uses a fresh random IV for each write, removes the key and ciphertext on logout/authentication loss, and deletes the legacy plaintext `localStorage` value. If WebCrypto or IndexedDB is unavailable, card persistence fails closed. This protects storage at rest from casual extraction; it is not an XSS boundary because compromised same-origin script could ask the browser to use the key. Queued updates are limited to fixed-text, noncritical, idempotent events; emergency calls, token/sharing operations, invites, and provider notifications are never queued/retried.

## Logging, telemetry, and audit

Use structured allowlisted fields: timestamp, severity, operation name, outcome class, opaque actor/resource ID, correlation/trace ID, duration, dependency name/status, HTTP status, and safe error code. Redact cookies, authorization/signature headers, URLs with tokens, connection strings, keys, emails, phone numbers, location, input/output bodies, filenames, prompt/model text, profile facts, and raw provider payloads.

Audit significant security and state events with UTC time, opaque actor/resource IDs, action, outcome, reason code, and correlation ID. Audit data itself is access-controlled, tamper-evident as operationally feasible, retained by policy, and does not duplicate sensitive content.

Current `AuditEvent` records provide UTC time, actor/resource/action, and optional safe metadata, but correlation IDs are not propagated consistently and outcome/reason are not normalized dedicated fields. Correlation propagation, normalized outcome/reason, tamper-evidence, access policy, and tested retention remain production gates.

Health endpoints expose no secrets, connection details, exception stacks, database names, or private counts. Liveness does not contact dependencies. The current readiness endpoint verifies the database only; provider-specific configured/degraded/unavailable reporting must be added with each real production adapter rather than inferred from provisioned Azure resources.

## Secrets and deployment

- `.env` is local-only and ignored. `.env.example` contains names/placeholders, not usable shared credentials.
- GitHub deployment uses OIDC federation with least-privilege Azure roles; avoid long-lived publish profiles/service-principal passwords.
- GitHub environments require approval for production and contain `POSTGRES_ADMIN_PASSWORD`, `JWT_SIGNING_KEY`, `WEBHOOK_SIGNING_SECRET`, conditional `SMS_GATEWAY_SIGNING_SECRET`/`OPENAI_API_KEY`, and Android signing secrets where used. The SMS endpoint is a non-secret environment variable; credentials must never be embedded in its URL.
- Azure managed identity is preferred for ACR, Key Vault, Storage, Service Bus, and telemetry. Where an app currently requires a connection string, inject it as a Container App secret and plan migration to identity-based SDK auth.
- No keystore, base64 keystore, `.env`, deployment output, certificate, token, or generated signed artifact is committed.
- Rotate secrets after suspected exposure; refresh/update the Container App revision and revoke old credentials. Do not print secure parameters from scripts.

## Threat register

| Threat | Primary controls | Verification |
| --- | --- | --- |
| Cross-user/profile/session access | Resource authorization and projection | Integration tests using two identities |
| Share token theft/guessing/tampering | Entropy, hash-only store, expiry/revoke, rate limit, no-referrer | Valid/expired/revoked/tampered/guessing tests |
| Refresh replay/session theft | Secure cookies, hash/rotation/family revoke, CSRF | Reuse/logout/CSRF tests |
| XSS/SQL/path traversal | Output encoding/CSP, parameterization, generated storage names | Adversarial boundary tests |
| Webhook forgery/replay | HMAC, timestamp, nonce/event uniqueness, idempotency | Invalid/replayed/duplicate callback tests |
| Prompt injection/unsafe model output | Isolation, strict schema, forbidden checks, deterministic allowlists/protocol | Direct-input/unsafe-output tests now; document-injection tests if document ingestion is added |
| Duplicate/concurrent state mutation | Idempotency, optimistic concurrency, unique constraints/transaction | Duplicate and two-writer tests |
| Sensitive telemetry/cache leak | Allowlisted logging, redaction tests, cache inspection | Captured log/cache assertions |
| Dependency outage | Bounded timeout/fallback and server authority | AI/DB and configured-provider checks now; Blob/Bus/Azure SignalR and deployed-provider outage exercises remain release gates |
| Supply-chain compromise | Lockfiles, pinned major runtime, dependency review, minimal image, checked-in CodeQL/SBOM/image scan and manual publish/sign workflow | CI restore/build/audit now; a successful `security-supply-chain.yml` run and retained evidence remain release gates |

## Security verification and reporting

Run the implemented automated negative tests in [TEST_PLAN.md](TEST_PLAN.md), dependency and focused secret checks, a manual browser storage/cache inspection, cookie/header review, and an authorization matrix before a shared demo. Captured-log redaction, inactive Blob/Bus/Azure SignalR outage behavior, and document-injection tests remain required when those surfaces are activated. The checked-in security workflow defines CodeQL, SBOM, container scan, attestation, and manual keyless-signing gates; only an observed successful run is evidence. An independent penetration test remains a release gate. Do not weaken tests to pass a gate.

Report a vulnerability privately to the repository owner with safe reproduction steps and no real medical data. For suspected secret exposure: revoke/rotate first, preserve minimal audit evidence, invalidate affected sessions/share links, assess logs/artifacts/caches, and notify affected parties according to an approved incident process. A production incident contact and disclosure SLA must be defined before launch.

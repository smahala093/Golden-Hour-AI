# Product requirements

## Product statement

Golden Hour AI helps a patient, family member, or bystander turn fragmented emergency information into coordinated practical action and a clearer handover. The product is designed for panic conditions and must remain partially useful when AI, speech, location, notifications, realtime transport, or the network is unavailable.

**Tagline:** Turn panic into coordinated action.

**Prototype label:** **Demonstration guidance requiring clinical review before production use.**

Golden Hour AI is not a doctor, diagnostic system, ambulance provider, or replacement for emergency services.

This document is the target product specification, not a claim that every external release gate is complete. Current implementation/evidence is tracked in [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md) and [TEST_PLAN.md](TEST_PLAN.md). In particular, model translation, an administrator provider-status experience, production maps/email/general notifications/Azure messaging/storage/telemetry adapters, real-provider delivery evidence, hosted deployment, and clinical/localization/privacy/security/manual-accessibility approval remain open.

## Goals

1. Make the configured emergency-call action immediately available without waiting for registration, AI, or network processing.
2. Capture original user-reported facts and preserve their source, language, confidence, confirmation state, and uncertainty.
3. Select actions only from a versioned, reviewed protocol catalogue and coordination-task allowlist.
4. Coordinate family/bystander tasks against one authoritative emergency session.
5. Generate concise family, responder, and hospital views with strict sharing projections.
6. Deliver a credible three-minute mock-mode demonstration with no external credentials.
7. Meet panic-oriented accessibility, offline, privacy, and failure-state requirements.

## Non-goals

- Diagnosis, triage certification, clinical decision support, medication dosage, or a treatment plan.
- Placing or confirming calls, dispatching an ambulance, choosing an available hospital, or guaranteeing arrival.
- Replacing local emergency services, clinicians, caregiver judgement, or an approved emergency plan.
- Storing complete longitudinal medical records or private documents in the PWA cache.
- Claiming universal language, country, accessibility, provider, or offline support.
- Production use before clinical, legal/privacy, accessibility, security, and operations review.

## Users and needs

| User | Need under stress | Required experience |
| --- | --- | --- |
| Patient | Ask for help with minimal interaction | One dominant emergency action, simple mode, large controls, call action always visible |
| Family/caregiver | Coordinate without duplicating work | Shared session, clear ownership/status, authoritative task board and timeline |
| Bystander | Act without an account while seeing minimal safe context | Short-lived limited link, call action, approved guidance, consented observation/location capture |
| Responder | Receive a concise factual brief | Permitted identity/location, confirmed observations, critical profile facts, explicit uncertainty |
| Hospital receiver | Understand chronology and provenance | Timeline, original report, confirmed/unconfirmed facts, actions reported, protocol version and disclaimer |
| Administrator | Operate without reading unnecessary sensitive data | Configuration, health, audit metadata, provider status, least-privilege access |

## Core journeys and acceptance criteria

### 1. Onboarding and readiness

The user can register, choose language and response mode, enter self-reported profile/contact/medical information, choose bystander sharing, and explicitly review it. Blood group remains optional and self-reported. Readiness is deterministic and explains which checks are complete; AI never calculates the score.

Acceptance:

- Forms expose clear labels, errors, review status, and provenance.
- At least two contacts and at least one verified contact contribute to readiness.
- Allergy/current-medicine status can be explicitly recorded as none/unknown, not inferred from an empty list.
- Insurance/address/private documents are not shared by default.
- Profile changes require resource authorization and create audit metadata without logging values.

### 2. Start an emergency

The initiator chooses self, family, or nearby person; selects a category; then speaks, types, answers quick questions, or skips AI. India defaults to `112` but the server-configured country number is authoritative.

Acceptance:

- The emergency button and call action are keyboard/screen-reader usable, high contrast, at least 44px, and reachable with one hand.
- Confirmation prevents accidental start without adding a long delay or hiding the call action.
- The call action is rendered before AI processing and is never queued or automatically retried.
- A session creation command has a stable idempotency key.

### 3. Panic-to-protocol pipeline

The system accepts text or at most 30 seconds of allowlisted audio, preserves the original, transcribes/detects language, creates an English normalized representation, extracts the strict incident contract, validates it, applies deterministic safety rules, selects a reviewed protocol, translates approved content, presents one action at a time, and updates summaries/timeline.

Acceptance:

- Extraction returns the documented schema and at most three follow-up questions.
- Diagnosis, dosage, treatment plans, unsupported clinical claims, and unconfirmed contact/delivery claims are rejected.
- Low confidence exposes the original input, marks uncertainty, asks confirmation, and falls back without blocking.
- Proper nouns, medicine names, and allergy names retain their original form.
- AI outage, timeout, refusal, or malformed content reaches a bounded static fallback without endless loading.

### 4. Reviewed protocol display

The prototype includes versioned static entries for unconscious/not breathing normally, unconscious/breathing, heavy external bleeding, seizure, suspected allergic reaction, chest pain, fall/injury, and unknown emergency.

Acceptance:

- Each file has version, review status, country, emergency-call instruction, do/do-not actions, escalation rule, source placeholder, and translation keys.
- Every display includes the prototype clinical-review label.
- Only allowlisted catalogue actions reach the UI; model text cannot add a new action.

### 5. Coordination room and task orchestration

One server-authoritative `EmergencySession` owns participants, observations, tasks, locations, timeline, summaries, profile snapshot, protocol version, confidence, and sharing permissions. Family tasks move through explicit accept/decline/reassign/complete transitions.

Acceptance:

- SignalR uses one authorized group per session and notifies participant/task/observation/location/call/departure/arrival/closure changes.
- Reconnect re-authenticates, rejoins, and refetches; duplicate timeline events are suppressed.
- REST polling remains available when SignalR is down.
- AI task suggestions are filtered through the allowed-task catalogue and duplicate critical tasks are prevented.
- Call connected, message delivered, patient departed/arrived, and session closed are recorded only on an authorized explicit command or verified provider callback.

### 6. Responder and hospital handover

The product generates distinct family, responder, and hospital projections. Each fact is labeled user-reported, profile, AI-extracted, confirmed, unconfirmed, or unknown.

Acceptance:

- Responder brief includes only permitted identity/age/location, observations, consciousness/breathing/start time, critical allergies/conditions/medicines/procedure/contact, and AI uncertainty.
- Hospital handover adds chronology, original description, actions reported, protocol version, languages, missing facts, confidence, and disclaimer.
- No summary states or implies a diagnosis or verified external action without its recorded source.

### 7. Bystander access

An anonymous user can open a short-lived link or QR code, call emergency services, see a limited projection and approved category guidance, report consciousness/breathing/bleeding, share location with consent, and contact an allowed family number.

Acceptance:

- Tokens have cryptographically random entropy; only a hash is persisted.
- Tokens expire, revoke, rate-limit, and audit access; comparison is timing-safe.
- Complete records, insurance, address, and private documents remain hidden by default.
- Every anonymous mutation is allowlisted, validated, idempotent where relevant, and scoped to its session.

### 8. Multilingual use

The system preserves original and normalized input, supports language switching, mixed-language input, and RTL layout. English and Hindi resources are complete. Marathi, Tamil, Telugu, Kannada, Bengali, Gujarati, Punjabi, and Urdu include extensible emergency-screen resources.

Acceptance:

- Language confidence is visible and low confidence asks the user to choose.
- Speech failure falls back to text and missing translation falls back to English with a notice.
- The UI never claims every language is supported.
- Urdu renders RTL; names and medical terms preserve the original text.

### 9. Offline and degraded use

The installable PWA caches only an allowlisted shell and public reviewed resources and offers an opt-in minimal emergency card.

Acceptance:

- Stale/offline state is announced visibly and accessibly.
- Noncritical timeline updates sync exactly once through idempotency; emergency calls never queue/retry.
- Auth tokens, documents, raw audio, and full medical records are absent from Cache Storage.
- Network/AI/SignalR/location/microphone/provider failures each expose a bounded next action.

## Interaction requirements

During an active emergency, each screen has one primary action, no more than three secondary actions, large plain text, high contrast, visible focus, no decorative animation/carousel/advertising, no hidden critical information, and no long paragraphs. Changing status uses appropriate live regions without repeatedly interrupting the call action.

Support keyboard-only operation, screen readers, reduced motion, dark/light/high-contrast modes, 200% zoom/reflow, RTL, coarse pointer, and minimum 44-by-44 CSS pixel touch targets. Simple mode contains only the emergency action, call action, speak/type capture, current task, and family connection state.

## Functional screens

Landing, registration, login, onboarding, emergency profile, contacts, privacy/sharing, dashboard, emergency start, voice/text capture, critical questions, action view, coordination room, task board, responder brief, hospital handover, bystander QR view, history, readiness, settings, language, accessibility, offline, not-found, unauthorized, and generic error boundary.

## Data, privacy, and retention requirements

- Use UTC, explicit provenance, optimistic concurrency, transactional outbox, idempotency, and indexes on token hashes/session/status/time.
- Collect and send only fields required for the current use case.
- Keep protected/sensitive content out of source, URLs, logs, analytics, telemetry properties, screenshots, and test snapshots.
- Provide explicit sharing and consent controls; keep a limited profile snapshot so later profile edits do not rewrite historical handovers.
- Define jurisdiction-specific retention/deletion/legal hold/export rules before production. The prototype must not invent them.

## Success criteria for the hackathon demo

Within three minutes, a presenter can use mock mode to log in, start a fictional family chest-pain emergency from Hindi input, see `112` immediately, observe preserved/normalized facts and a reviewed protocol, show a family task update in a second browser, and open a provenance-aware hospital handover. The presenter explicitly states that no emergency call, provider delivery, diagnosis, clinical approval, hosted deployment, or mobile artifact is being claimed.

## Release gates

The acceptance matrix in [IMPLEMENTATION_PLAN.md](IMPLEMENTATION_PLAN.md) and evidence matrix in [TEST_PLAN.md](TEST_PLAN.md) are mandatory. A file or workflow existing is not equivalent to a successful build, deployment, integration, accessibility review, APK/AAB generation, or test result.

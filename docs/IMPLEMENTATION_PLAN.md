# Implementation plan

## Objective

Deliver a deployment-ready hackathon prototype of Golden Hour AI: an accessible React PWA and ASP.NET Core 8 modular monolith that turns user-reported emergency information into deterministic, reviewed coordination steps and a clear hospital handover. The product always exposes the emergency-call action and treats AI as an uncertain extractor and summarizer, not a clinician.

## Delivery sequence

1. Establish repository conventions, safety boundaries, assumptions, and verification gates.
2. Build the domain model, deterministic readiness/task/protocol rules, EF Core persistence, Identity authentication, refresh rotation, resource authorization, audit/outbox support, and migrations.
3. Add versioned REST APIs, SignalR session groups, bystander tokens, signed webhooks, provider abstractions, deterministic mock providers, and a bounded OpenAI Responses API adapter with strict structured output.
4. Build the mobile-first React UI for onboarding, emergency capture/action/coordination/handover, bystander access, settings, multilingual/RTL use, simple mode, offline fallbacks, reconnection, and accessible failure states.
5. Add static versioned protocols, prompt resources, demo scenario/data, PWA caching, Docker/Azure/Bicep/GitHub Actions, optional Capacitor configuration, and complete documentation.
6. Run backend, frontend, integration, accessibility, offline, and end-to-end checks; build production artifacts; fix product failures; document environment-only gaps honestly.

## Assumptions

- India is the demo locale and `112` is the default configurable emergency number.
- No production credentials or Azure/OpenAI/provider accounts are assumed. Mock AI, speech, messaging, event bus, storage, clock, and notification behavior remains usable without them.
- PostgreSQL is the production/local-container database. Automated API tests may use an isolated test provider where that improves determinism; database-specific behavior is covered separately when a container is available.
- Demo passwords come from development-only configuration/environment variables and are never enabled in Production.
- Protocol text is prototype demonstration content, versioned and visibly marked as requiring clinical review.
- The web app is served by ASP.NET Core in production for one origin; Vite is used for local frontend development.
- Android configuration is included, but an APK/AAB is only reported if the necessary Android SDK/workloads and signing material are actually available.

## Principal risks and mitigations

| Risk | Mitigation |
| --- | --- |
| AI invents unsafe guidance | Strict schema, forbidden-content checks, confidence threshold, deterministic protocol catalogue, allowlisted task catalogue, visible uncertainty, static fallback. |
| Emergency action is delayed | `tel:` call action is rendered before AI work and never automatically retried or marked connected. |
| Sensitive data leaks | Field-level sharing projection, random token with only hash stored, expiry/revocation/rate limits, audit events, log redaction, no private-document caching. |
| Realtime updates are lost/duplicated | Server-authoritative state, idempotency keys, optimistic concurrency, ordered timeline, reconnect/rejoin/refetch, REST polling fallback. |
| Offline state becomes unsafe or stale | Cache only shell/translations/reviewed protocols and opt-in minimum card; label staleness; queue only noncritical idempotent events. |
| Prototype guidance is mistaken for clinical approval | Persistent disclaimer in product, protocol metadata, summaries, documentation, and demo script. |
| External services or credentials are absent | Provider abstractions and deterministic mocks; health status distinguishes configured, degraded, and unavailable dependencies. |
| Hackathon scope exceeds available runtime/tooling | Prioritize a coherent vertical demo path plus testable safety/security primitives; document any unverified optional artifact without fabricating success. |

## Verification gates

- Backend compiles on .NET 8, migrations are discoverable, and unit/integration tests pass.
- Frontend lint, strict typecheck, unit/accessibility tests, PWA build, and key Playwright journeys pass.
- Production build serves the SPA and health endpoints; mock chest-pain demo works without external keys.
- Repository secret scan and configuration review find no committed credentials.
- CI, Azure Bicep/deployment, Docker Compose, and rollback documentation agree with the actual paths and commands.

## Acceptance traceability

This matrix records the evidence that must exist before an area is called complete. A path is not, by itself, proof that its checks passed; results belong in `docs/TEST_PLAN.md` and CI artifacts.

| Area | Acceptance criteria | Primary evidence | Verification gate |
| --- | --- | --- | --- |
| Safety-critical emergency flow | Call action renders independently of AI; strict extraction is bounded; deterministic reviewed protocol is selected; uncertainty and fallback stay visible; external success requires provider or user confirmation | `apps/api/Protocols/`, API application/domain tests, emergency UI tests, `docs/AI_SAFETY.md` | Positive chest-pain and low-confidence journeys plus timeout, malformed, diagnosis, dosage, and prompt-injection negatives pass |
| Authentication, sharing, and security | Identity hashing/lockout, short-lived browser session with rotation/revocation, resource authorization, hashed/expiring/revocable bystander tokens, webhook anti-replay, rate limits, audit/redaction | API auth/share/webhook implementations and integration tests; `docs/SECURITY.md` | Cross-user, token guessing/tampering/reuse, CSRF, replay, injection, private-document, and sensitive-log tests fail safely |
| Realtime and offline | One authorized SignalR group per session; reconnect rejoins and refetches authoritative state; REST polling fallback; only noncritical idempotent updates queue; calls and private documents never queue/cache | Hub/client tests, PWA configuration, Playwright offline/reconnect journeys, `docs/ARCHITECTURE.md` | Duplicate suppression, reconnect, browser refresh, offline start, restart, and sync-exactly-once checks pass |
| Multilingual and accessibility | Complete English/Hindi resources, basic listed-language emergency resources, RTL and mixed-language behavior, preserved names/medicines, keyboard/focus/live regions/reduced motion/high contrast/200% zoom/44px targets | Web resources and component tests; axe/Playwright evidence; manual checklist in `docs/TEST_PLAN.md` | Language switch/fallback/confidence and automated accessibility tests pass; manual screen-reader, zoom, RTL, and contrast checks are recorded |
| Automated test layers and coverage | Domain, application, API/database/auth/SignalR/AI/webhook tests; web unit/a11y/offline tests; desktop/mobile Playwright; meaningful coverage for critical services | `tests/`, `apps/web` tests, CI reports | All configured commands pass from clean install; Cobertura/LCOV and HTML reports are published; critical domain/application line coverage is at least 80% or the shortfall is reported honestly |
| Azure, CI, PWA, and Android | Multi-stage same-origin image, PostgreSQL Compose, Bicep resources, migration/health/smoke/rollback, CI/deploy workflows, installable PWA; debug APK and signed AAB workflows require real tooling/secrets | `Dockerfile`, `docker-compose.yml`, `infrastructure/`, `.github/workflows/`, PWA/Capacitor config, `android/README.md` | Docker/CI/Bicep checks pass; deployed health/smoke checks are observed; APK/AAB paths are reported only after successful artifact creation |
| Documentation and demo | Public README, architecture/product/API/safety/security/test/deploy docs, exact local/cloud/mobile commands, seeded fictional scenario, timed three-minute demo, no fabricated URL/integration/result | `README.md`, `docs/`, `.env.example` | A clean-checkout rehearsal follows the documented commands and demo script; every claim is reconciled with observed output and known limitations |

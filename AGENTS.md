# Golden Hour AI Engineering Guide

## Architecture boundaries

- `apps/api` is the ASP.NET Core 8 modular monolith. Domain rules must not depend on HTTP, EF Core, cloud SDKs, or provider implementations.
- Application services coordinate use cases through interfaces. Infrastructure implements persistence, OpenAI, messaging, storage, notifications, and clocks.
- `apps/web` is the React/TypeScript client. It consumes versioned REST endpoints and the SignalR hub; it must not contain medical decision logic.
- Reviewed emergency protocols in `apps/api/Protocols` are the only source of emergency actions. AI may extract facts and select a protocol, never invent clinical instructions.
- Durable state is authoritative on the server. Offline browser queues are limited to noncritical, idempotent timeline updates.

## Coding standards

- Enable nullable reference types, analyzers, TypeScript strict mode, ESLint, and deterministic formatting.
- Use UTC timestamps, cancellation tokens, dependency injection, RFC 7807 errors, correlation IDs, and explicit validation at trust boundaries.
- Prefer small cohesive modules and immutable contracts. Keep secrets and protected health information out of source, logs, URLs, analytics, and test snapshots.
- All changing status text must be announced accessibly. Support keyboard-only use, visible focus, reduced motion, 200% zoom, RTL, high contrast, and minimum 44px touch targets.

## Build commands

- Full backend: `dotnet build GoldenHourAI.sln`
- Web install: `npm ci --prefix apps/web`
- Web production build: `npm run build --prefix apps/web`
- Containers: `docker compose up --build`

## Test commands

- Backend: `dotnet test GoldenHourAI.sln --collect:"XPlat Code Coverage"`
- Web unit/accessibility: `npm test --prefix apps/web -- --run`
- Web lint/typecheck: `npm run lint --prefix apps/web && npm run typecheck --prefix apps/web`
- End to end: `npm run test:e2e --prefix tests/e2e`

## Security rules

- Never commit credentials, production passwords, signing keys, tokens, private medical data, or provider payloads.
- Browser authentication uses Secure, HttpOnly, SameSite cookies; never store JWTs or refresh tokens in local storage.
- Enforce resource-level authorization, rate limits, idempotency, token hashing, short expiries, revocation, webhook signatures/timestamps/replay protection, safe upload limits, and audit events.
- Treat user text, filenames, uploads, AI output, webhooks, and client state as untrusted. AI output cannot execute privileged operations.

## Medical safety restrictions

- Golden Hour AI is not a doctor, diagnostic system, ambulance provider, or replacement for emergency services.
- Never diagnose, guarantee responder arrival, invent dosage or treatment, recommend medication outside a stored clinician-approved plan, hide uncertainty, or claim an external action succeeded without provider/user confirmation.
- The emergency-call action must remain immediately available and must never wait for AI.
- Label every prototype protocol: **Demonstration guidance requiring clinical review before production use.**

## Definition of done

- Builds, linting, type checks, meaningful positive/negative tests, accessibility checks, migrations, mock mode, PWA assets, Docker, CI, Azure IaC, and documentation are present and verified.
- Failures are visible and safe; no result, deployment, integration, notification, APK, AAB, or coverage figure is fabricated.
- Never weaken, skip, or delete a test merely to make CI pass. Fix the product or document a genuine environment limitation.


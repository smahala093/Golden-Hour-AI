# Test plan and evidence

## Evidence policy

This plan separates required coverage from observed results. A checked-in test, workflow, or report path is not proof that a command passed. Never fabricate a test result, coverage percentage, screenshot, external delivery, deployment, APK, or AAB. Never weaken, skip, or delete a test to make CI pass.

At the time this document was created, final clean-checkout verification had not yet been completed. Record the exact command, UTC time, environment/tool versions, exit code, report/artifact path, and genuine limitation in the verification log below after each run.

## Quality gates

| Gate | Command | Expected evidence |
| --- | --- | --- |
| Backend restore/build | `dotnet build GoldenHourAI.sln --configuration Release` | Exit 0 with analyzers and nullable warnings treated as errors |
| Backend tests/coverage | `dotnet test GoldenHourAI.sln --configuration Release --collect:"XPlat Code Coverage" --results-directory TestResults` | xUnit/TRX console result and Cobertura files under `TestResults/` |
| Web install | `npm ci --prefix apps/web` | Lockfile-resolved clean install exits 0 |
| Web lint | `npm run lint --prefix apps/web` | ESLint exits 0 with zero warnings |
| Web strict typecheck | `npm run typecheck --prefix apps/web` | TypeScript project build exits 0 |
| Web unit/a11y | `npm test --prefix apps/web -- --run --coverage` | Vitest result and HTML/JSON/LCOV under `apps/web/coverage/` |
| PWA production build | `npm run build --prefix apps/web` | Vite exits 0; install assets/service worker under `apps/web/dist/` |
| End-to-end install | `npm ci --prefix tests/e2e` | Locked install exits 0 |
| Playwright Chromium | `npm exec --prefix tests/e2e -- playwright install chromium` then `npm run test:e2e --prefix tests/e2e` | Desktop/mobile results under `tests/e2e/playwright-report/` and screenshots/traces only on configured cases |
| Container config | `docker compose config --quiet` | Exit 0; no resolved secret printed or committed |
| Container build/start | `docker compose up --build --detach` | Healthy PostgreSQL and app containers; follow with health/smoke scripts |
| Health/smoke | `./infrastructure/scripts/health-check.ps1 -BaseUrl http://localhost:8080` and `./infrastructure/scripts/smoke-test.ps1 -BaseUrl http://localhost:8080` | Liveness/readiness and safe public contract checks exit 0 |
| Bicep validation | `az bicep build --file infrastructure/bicep/main.bicep` plus `az deployment group validate ...` | Local compile and subscription validation exit 0 |
| Secret/dependency/container review | CI scanners plus manual configuration inspection | Findings triaged; no credentials or sensitive fixtures in repository/artifacts |

Run Node 22 in CI/local builds because the configured Vite toolchain does not support older Node releases. Use .NET SDK 8 for the release gate even if a later SDK can compile the target.

## Test layers

### Domain

Fast tests with no HTTP/database/cloud dependencies cover deterministic readiness scoring, emergency/task/session transitions, idempotency identity, protocol selection, allowlisted tasks, provenance, confirmation/unknown semantics, summary projection, concurrency decisions, and forbidden AI content. Exercise positive and invalid transition paths.

### Application services

Use mocked interfaces and a controllable UTC clock to verify orchestration order, transaction/outbox boundaries, resource authorization requirements, AI fallback/timeouts, provider status reconciliation, token lifecycle, duplicate commands, and minimal sharing projections. Assert both returned state and absence of unauthorized side effects.

### API/integration

`WebApplicationFactory` exercises model validation, Problem Details/correlation IDs, Identity/auth cookies, CSRF, refresh rotation/reuse, role/resource policies, rate limits, file constraints, EF behavior, webhooks, health endpoints, OpenAPI, and SignalR group authorization. PostgreSQL-specific indexes/concurrency/transactions require a real isolated PostgreSQL instance in CI or a clearly recorded environment limitation; an in-memory provider is not sufficient evidence for database semantics.

### Web component/integration

Testing Library and Vitest cover accessible names/roles, focus/error behavior, emergency action persistence, uncertainty, form validation, language/RTL switching, simple mode, connection banners/live regions, permission denial, offline card/queue policy, cache exclusions, error boundary, and keyboard behavior. Use `axe` or equivalent automated checks as a supplement to semantic assertions, not as the whole accessibility review.

### End-to-end

Playwright runs deterministic mock mode in desktop Chromium and a mobile viewport. It covers user and bystander journeys, two browser contexts for realtime coordination, refresh/reconnect, offline/degraded behavior, PWA install resources, screenshots for the demo journey, and storage/cache inspection. Do not put demo passwords or entered medical data in retained traces/screenshots for public runs.

## Positive scenario matrix

| IDs | Scenarios | Main layer/evidence |
| --- | --- | --- |
| P01–P02 | Register/complete profile; add two contacts including one verified | API integration + onboarding components/E2E |
| P03–P05 | Start self, family, and anonymous bystander emergency | Domain/application + API/E2E |
| P06–P08 | Hindi text, mocked Hindi audio, and Hinglish preserving names/medicines | AI contracts + web language/E2E |
| P09–P12 | Strict schema, low-confidence confirmation, chest-pain protocol, task allowlist filtering | Application/provider contract tests |
| P13–P16 | Participant valid-link join, SignalR task broadcast, task accept/complete, ordered timeline | API/SignalR integration + two-context E2E |
| P17–P19 | Limited responder brief, provenance-aware handover, limited QR projection | Domain projection + authorization integration |
| P20–P22 | Cached protocol offline, queued noncritical update syncs once, reconnect restores state | Web unit/service-worker + E2E |
| P23–P25 | AI outage/static fallback, location denial/manual entry, microphone denial/text | Application + component/E2E |
| P26–P27 | Keyboard operation and screen-reader labels/status | Component/axe + manual AT review |
| P28–P30 | Refresh rotation, share link revocation, safe session close | Auth/session integration + E2E |

For each row, verify the emergency-call action remains available and no unconfirmed external success or diagnosis appears.

## Negative and security scenario matrix

| IDs | Scenarios | Safe assertion |
| --- | --- | --- |
| N01–N06 | Invalid/locked/expired/reused auth; unauthorized profile/session | Non-enumerating 401/403; no resource data or mutation; token family revoked on reuse |
| N07–N10 | Expired/revoked/tampered share token; repeated guessing | Same limited failure response; rate limit/audit; call action remains available |
| N11–N12 | Invalid webhook signature and replay | Reject before state change; valid duplicate is idempotent |
| N13–N16 | Duplicate timeline/task, disconnect during update, transaction failure | Exactly-once result/no false completion; refetch/fallback; atomic rollback |
| N17–N25 | AI timeout/rate limit/malformed/refusal/enum/diagnosis/dosage and prompt injection in text/document | Bounded static fallback; forbidden content/effects absent; call action visible |
| N26–N30 | Unsupported/oversized/empty/corrupt audio; unsupported/low-confidence language | Bounded validation/fallback; no untrusted filename processing; language choice/text path |
| N31–N37 | Translation/location/notification/SMS/bus/PostgreSQL/blob failure | Explicit degraded/unavailable state; no false delivery; authoritative state not corrupted |
| N38–N40 | Offline while starting, concurrent task writers, deleted-data access | Call/manual path; one winner plus conflict/refetch; no deleted/private projection |
| N41–N45 | XSS, SQL injection, path traversal, CSRF, excessive rate | Payload treated as data/rejected; no script/query/file effect; 403/429 safely |
| N46–N50 | Sensitive log leak, SignalR enumeration, anonymous private docs, browser refresh, app restart | Captured logs clean; membership denied; no document; state recovered from server |

The required assertions include what must **not** happen. A generic error without a safe next action is not a pass.

## AI contract and adversarial cases

- Exact schema, required fields, no additional fields, confidence range, enum set, size limits, and maximum three questions.
- Original Hindi retained byte-for-byte; normalized English is separate; proper nouns, allergies, and medicine names preserve original spans.
- Reject any string containing a diagnosis, dosage, generated treatment action, unsupported clinical claim, or false service/provider success.
- Time-controlled provider timeout, bounded retry/backoff, 429, refusal, truncated/malformed JSON, HTML error, additional/missing property, unsupported enum, and contradictory output.
- User/document prompt attempts to override instructions, exfiltrate secrets, invoke tools, change sharing, join sessions, or mark actions successful have no privileged effect.
- Translation failure or structural alteration falls back to approved source/English content and announces the limitation.

## Accessibility manual matrix

Automated rules cannot validate all panic-condition accessibility. Record browser/OS/assistive technology and findings for:

- Keyboard-only traversal: no trap, logical order, skip/navigation, modal restoration, call action reachable, visible focus never obscured.
- NVDA + Chrome (Windows) or equivalent: page/heading/landmark structure, emergency button name, field hints/errors, live connection/task/status announcements, no repeated interruption.
- 200% and 400% zoom/reflow at 320 CSS px: no clipped action/text or two-dimensional scrolling for core content.
- Windows high contrast/forced colors and both themes: controls/status/uncertainty are not color-only and retain visible boundaries/focus.
- Reduced motion: active emergency has no decorative animation, auto-scrolling, parallax, or unsafe time-limited interaction.
- Coarse pointer/one-hand use: all targets at least 44 by 44 CSS px with adequate spacing.
- Hindi Unicode, Urdu RTL, and mixed-direction names/medicine strings remain readable and do not reverse call numbers.
- Voice/microphone and geolocation permission denial gives contextual explanation and immediate text/manual alternatives.

## PWA/offline inspection

1. Build the production PWA and inspect the generated manifest/service worker.
2. Verify name, icons/maskable icon, standalone display, scope/start URL, theme/background, and successful install criteria in Chromium.
3. Inspect Cache Storage after authenticated and bystander journeys. Only hashed shell, reviewed protocol, and translation resources may appear.
4. Confirm tokens, API profile/session responses, token-bearing pages, documents, audio, and errors containing input are absent.
5. Offline: reload shell, show stale state, open opt-in minimum card, access reviewed protocol, and keep `tel:` call action usable.
6. Queue one allowlisted noncritical update, reconnect twice, and assert a single server timeline entry.
7. Assert calls, invites, share changes, notification requests, and critical transitions are never queued/retried.

## Deployment and recovery tests

- `docker compose config --quiet`, clean image build, healthy PostgreSQL/API, same-origin SPA deep-link fallback, `/health/live`, `/health/ready`, OpenAPI in its intended environment, and mock chest-pain smoke.
- Bicep compile/lint/what-if/validate; resource names/regions/SKUs/identities/roles/private access reviewed. A successful template compile is not a successful Azure deployment.
- Deployment workflow uses OIDC and environment approval, applies migrations as a distinct controlled step, checks health/smoke, and retains the prior healthy Container App revision.
- Rollback shifts traffic to a known healthy prior revision and does not automatically reverse a forward-only database migration. Test rollback compatibility with the current schema.
- Restore a disposable PostgreSQL backup/point-in-time target before production. Record actual RTO/RPO instead of assuming Azure defaults meet needs.
- Android debug/release workflows run only when tooling/secrets exist. Inspect manifest permissions: no background location; microphone/location purpose is contextual. Verify package ID/version and signing certificate before Play internal testing.

## Coverage policy

Critical Domain and Application services target at least 80% line coverage with meaningful branch and negative behavior assertions. Report Domain/Application separately from generated migrations, DTO boilerplate, UI styling, and provider wiring. Frontend coverage is diagnostic; critical emergency, auth/sharing, queue/reconnect, safety, and accessibility behaviors require explicit tests even if an aggregate percentage is high.

Merge coverage by project only when paths and source roots are normalized. Publish machine-readable Cobertura/LCOV and human-readable HTML. Never round up, infer, or copy a prior run's percentage.

## Verification log

Replace `Pending` only after observing the command. Keep failed/blocked rows and explain the environment limitation.

| UTC time | Environment | Command/gate | Result | Evidence / limitation |
| --- | --- | --- | --- | --- |
| Pending | Pending | Backend build/tests/coverage | Not yet recorded | Final verification required |
| Pending | Pending | Web lint/type/test/build/coverage | Not yet recorded | Final verification required |
| Pending | Pending | Playwright desktop/mobile | Not yet recorded | Final verification required |
| Pending | Pending | Docker Compose + health/smoke | Not yet recorded | Final verification required |
| Pending | Pending | Bicep build/validate/what-if | Not yet recorded | Requires Azure CLI/subscription for server-side validation |
| Pending | Pending | Android debug APK / release AAB | Not generated | Requires Android toolchain; release additionally requires owner signing secrets |

CI run URLs and hosted/deployed URLs must be added only after those external operations genuinely succeed.

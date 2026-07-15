# Golden Hour AI

> **Turn panic into coordinated action.**

Golden Hour AI is an accessible emergency coordination and information-handover prototype. It helps a patient, family member, or bystander capture what happened, keep an emergency-call action immediately available, coordinate practical family tasks, and prepare a clearly sourced responder brief and hospital handover.

> **Safety notice:** Golden Hour AI is not a doctor, diagnostic system, ambulance provider, or replacement for emergency services. It does not confirm that a call, message, responder, or hospital action succeeded. In India, use the prominent **Call 112** action for emergency help. Every included protocol is **Demonstration guidance requiring clinical review before production use.**

This repository is a hackathon prototype, not a clinically approved or production-certified medical device.

## Why it exists

Emergencies create an information and coordination problem at exactly the moment people have the least attention to spare. Important facts can be lost, relatives duplicate tasks, and responders receive fragmented handovers. Golden Hour AI keeps the emergency call independent of AI, turns reported facts into a deterministic coordination flow, and preserves provenance and uncertainty throughout the handover.

The key differentiator is the boundary between AI and safety-critical behavior: AI may extract, translate, and summarize; reviewed static protocols and backend rules decide which actions can be displayed. AI output cannot contact services, assign privileges, invent clinical instructions, or mark an external action successful.

## Current scope

- Mobile-first React/TypeScript PWA with English and Hindi resources, extensible language support, keyboard operation, reduced-motion behavior, and simple mode.
- ASP.NET Core 8 modular monolith with versioned REST APIs, SignalR, EF Core/PostgreSQL, Identity-based authentication, audit/outbox primitives, and RFC 7807 errors.
- Deterministic mock providers and fictional Jaipur-area demo data for a credential-free demonstration.
- Backend-only OpenAI adapter behind configuration, strict structured extraction, bounded retries/timeouts, and deterministic fallback.
- Multi-stage container build, PostgreSQL Compose environment, Azure Bicep, CI/deployment workflows, and optional Capacitor Android packaging.

Implementation evidence and known gaps are tracked in [the test plan](docs/TEST_PLAN.md) and [implementation plan](docs/IMPLEMENTATION_PLAN.md). No hosted URL, APK, AAB, provider delivery, or test result is claimed unless it was actually produced.

## Architecture

```mermaid
flowchart LR
    Browser["React PWA / Capacitor shell"]
    Api["ASP.NET Core 8 modular monolith"]
    Rules["Deterministic safety rules"]
    Protocols["Versioned reviewed protocols"]
    Db[(PostgreSQL)]
    Hub["SignalR session groups"]
    Providers["Provider interfaces"]
    Mock["Deterministic mocks"]
    OpenAI["OpenAI Responses / speech APIs"]
    Azure["Blob / Service Bus / Azure SignalR"]

    Browser -->|"same-origin REST"| Api
    Browser <-->|"realtime events"| Hub
    Hub --- Api
    Api --> Rules --> Protocols
    Api --> Db
    Api --> Providers
    Providers --> Mock
    Providers -. "configured production mode" .-> OpenAI
    Providers -. "configured production mode" .-> Azure
```

The domain and application layers do not depend on HTTP, EF Core, cloud SDKs, or provider implementations. Durable server state remains authoritative; offline storage is restricted to the app shell, reviewed protocol resources, translations, an opt-in minimal card, and noncritical idempotent updates.

See [Architecture](docs/ARCHITECTURE.md), [AI safety](docs/AI_SAFETY.md), [Security](docs/SECURITY.md), and [API](docs/API.md) for the detailed contracts and diagrams.

## Technology

| Layer | Stack |
| --- | --- |
| Web | React, TypeScript, Vite, React Router, TanStack Query, i18next, SignalR client, Vitest, Testing Library, Playwright |
| API | ASP.NET Core 8, EF Core, PostgreSQL, Identity, JWT/cookie auth, SignalR, FluentValidation, OpenAPI |
| AI | OpenAI Responses API and speech-to-text behind backend interfaces; deterministic mocks by default |
| Delivery | Docker, Docker Compose, Azure Container Apps, PostgreSQL Flexible Server, Bicep, GitHub Actions |
| Mobile | PWA first; optional Capacitor wrapper using package `ai.goldenhour.app` |

## Screenshots

The browser journeys are designed to emit screenshots under `tests/e2e/test-results/` when the Playwright demo tests run. That directory is intentionally ignored because generated results can contain user-entered data. This repository does not claim a screenshot artifact until that test has been run; use the [three-minute demo](docs/DEMO_SCRIPT.md) to reproduce the current flow locally.

## Prerequisites

- [.NET SDK 8](https://dotnet.microsoft.com/download/dotnet/8.0)
- Node.js 22 and npm 10+
- PostgreSQL 16, or Docker Desktop / Docker Engine with Compose v2
- Optional: Azure CLI with the Bicep extension for deployment
- Optional: JDK 21 and Android SDK for Android packaging

## Fastest local start: Docker Compose

From the repository root:

```powershell
Copy-Item .env.example .env
# The copied values are public, local-only demo values. Replace them before any shared use.
docker compose up --build
```

Open `http://localhost:8080`. The container serves the built PWA and API from one origin. PostgreSQL is exposed on `localhost:5432` by default. To stop without deleting the database volume:

```powershell
docker compose down
```

`docker compose down --volumes` permanently removes the local database and should only be used when that data is no longer needed.

## Local development without containers

1. Start PostgreSQL and create the configured database, or use the development in-memory provider.
2. Restore and start the API:

   ```powershell
   dotnet restore GoldenHourAI.sln
   dotnet run --project apps/api/GoldenHour.Api.csproj
   ```

3. In a second terminal, install and start the web client:

   ```powershell
   npm ci --prefix apps/web
   npm run dev --prefix apps/web
   ```

4. Open `http://localhost:5173`. Vite proxies `/api` and `/hubs` to `http://localhost:8080`.

The development settings use the in-memory database and deterministic mock providers unless overridden. To use PostgreSQL, set `Database__UseInMemory=false` and `ConnectionStrings__GoldenHour`, then apply migrations:

```powershell
dotnet tool restore
dotnet ef database update --project apps/api/GoldenHour.Api.csproj --startup-project apps/api/GoldenHour.Api.csproj
```

If the repository does not yet contain a tool manifest or migrations, use the container startup migration path documented in [Deployment](docs/DEPLOYMENT.md); do not create an unreviewed production migration at deploy time.

## Configuration

.NET nested settings use double underscores in environment variable names.

| Variable | Required | Purpose |
| --- | --- | --- |
| `ConnectionStrings__GoldenHour` | PostgreSQL mode | Npgsql connection string; treat as a secret |
| `Database__UseInMemory` | No | Development/test-only database switch |
| `Database__MigrateOnStartup` | No | Local/container convenience; keep disabled for concurrent production rollouts unless explicitly controlled |
| `Authentication__Jwt__SigningKey` | Yes outside local development | High-entropy signing key, at least 32 characters |
| `Authentication__Jwt__Issuer` / `Authentication__Jwt__Audience` | Shared environments | Token issuer/audience that must exactly match validation policy |
| `Authentication__AccessTokenMinutes` / `Authentication__RefreshTokenDays` | No | Short browser credential lifetimes; defaults 10 minutes / 7 days |
| `Seed__DemoPassword` | Development demo only | Seeds `demo@goldenhour.ai` and `family@goldenhour.ai`; never enable demo seeding in production |
| `Providers__UseMocks` | No | `true` uses deterministic local providers |
| `OpenAI__ApiKey` | Real AI mode only | Backend-only OpenAI key; never prefix with `VITE_` |
| `OpenAI__Model` | No | Configured structured-output model |
| `OpenAI__BaseUrl` / `OpenAI__TimeoutSeconds` | No | Backend provider endpoint and bounded request timeout |
| `Webhook__SigningSecret` | Webhooks enabled | Provider callback HMAC secret |
| `Webhook__AllowedClockSkewMinutes` | No | Maximum signed-callback timestamp skew; default 5 minutes |
| `Emergency__DefaultNumber` | No | Country-configurable emergency number; local default is `112` |
| `Emergency__AiConfidenceThreshold` | No | Product uncertainty threshold; not a clinical probability |
| `PublicAppUrl` | Shared environments | Canonical HTTPS origin used for short links |
| `Azure__Storage__ConnectionString` | Azure storage provider | Blob provider connection string or managed-identity configuration |
| `Azure__SignalR__ConnectionString` | Azure SignalR mode | Server-side Azure SignalR connection |
| `Azure__ServiceBus__ConnectionString` | Azure event bus mode | Service Bus connection string |
| `APPLICATIONINSIGHTS_CONNECTION_STRING` | Azure telemetry | Application Insights connection string; never log private medical content |
| `VITE_API_BASE_URL` | Dev only | API origin; production defaults to same origin |
| `VITE_SIGNALR_HUB_URL` | Dev only | Hub URL; production defaults to `/hubs/emergency` |
| `VITE_MOCK_MODE` | No | Client demo/failure-state toggle, not an authorization boundary |
| `VITE_EMERGENCY_NUMBER` | No | Display/call fallback; server configuration remains authoritative |

The complete local template is [.env.example](.env.example). Production secrets belong in GitHub environment secrets and Azure secret stores, never source control or `VITE_*` values.

## Demo accounts and mock scenario

Development seeding creates fictional accounts only when a nonempty `Seed__DemoPassword` is supplied:

- `demo@goldenhour.ai`
- `family@goldenhour.ai`

For the documented local demo, copy `.env.example` and use its intentionally public local-only password `GoldenHour-Demo-Only-2026!` for both accounts. You may change `DEMO_PASSWORD`, but the replacement must satisfy the configured Identity policy. The same fallback appears in the checked-in ASP.NET Development settings; it must not be used in any shared environment.

The stable scenario uses fictional patient Raj Kumar and the Hindi report:

> मेरे पिताजी को अचानक सीने में दर्द और बहुत पसीना आ रहा है। उन्हें बोलने में भी परेशानी हो रही है।

Mock mode should preserve the original text, extract bounded facts, show uncertainty, select the versioned chest-pain demonstration protocol, and keep the call action visible. Follow [docs/DEMO_SCRIPT.md](docs/DEMO_SCRIPT.md) rather than improvising clinical claims.

## OpenAI mode

Mock mode is the default and is the recommended deterministic hackathon path. To exercise the real backend provider locally:

```powershell
$env:Providers__UseMocks='false'
$env:OpenAI__ApiKey='<set-in-your-shell-or-secret-store>'
$env:OpenAI__Model='gpt-4.1-mini'
dotnet run --project apps/api/GoldenHour.Api.csproj
```

Do not put the key in `.env.example`, browser code, logs, screenshots, or issue reports. Real AI output still passes strict schema validation, forbidden-content filtering, deterministic protocol selection, and the same static fallback. Provider configuration never turns AI text into a privileged command.

## Build and test

```powershell
# Backend build and tests
dotnet build GoldenHourAI.sln --configuration Release
dotnet test GoldenHourAI.sln --configuration Release --collect:"XPlat Code Coverage"

# Frontend quality gates and production PWA
npm ci --prefix apps/web
npm run lint --prefix apps/web
npm run typecheck --prefix apps/web
npm test --prefix apps/web -- --run
npm run build --prefix apps/web

# End-to-end, after its package install and browsers are available
npm ci --prefix tests/e2e
npm exec --prefix tests/e2e -- playwright install --with-deps chromium
npm run test:e2e --prefix tests/e2e
```

The PWA build output is `apps/web/dist/`. Coverage and test reports are generated locally and are not evidence of a passing run unless the commands complete successfully. See [Test plan](docs/TEST_PLAN.md) for the required positive, negative, accessibility, offline, and security evidence.

## Azure deployment

The Azure target is one Container App that serves the PWA and API from the same origin, plus PostgreSQL Flexible Server, Blob Storage, Service Bus, Azure SignalR, Application Insights/Log Analytics, ACR, and a Key Vault boundary. No Azure resources or public URL are included in this repository.

After Azure CLI login and secret setup:

```powershell
az login
az account set --subscription '<subscription-id>'
./infrastructure/scripts/deploy.ps1 `
  -SubscriptionId '<subscription-id>' `
  -ResourceGroup 'rg-goldenhour-dev' `
  -Location 'centralindia' `
  -EnvironmentName 'dev' `
  -PostgresAdminPassword (Read-Host 'PostgreSQL password' -AsSecureString) `
  -JwtSigningKey (Read-Host 'JWT signing key' -AsSecureString) `
  -WebhookSigningSecret (Read-Host 'Webhook signing secret' -AsSecureString)
```

The script performs a Bicep validation/deployment, builds the image in ACR, updates the Container App, and invokes explicit health/smoke checks. Database migration and rollback are separate, auditable operations. Read [Deployment](docs/DEPLOYMENT.md) before using shared infrastructure.

## Android packaging

The web URL/PWA is the primary deliverable. Capacitor uses application ID `ai.goldenhour.app` and reads `apps/web/dist`.

Local debug build, when JDK 21 and an Android SDK are installed:

```powershell
npm ci --prefix apps/web
npm run build --prefix apps/web
Push-Location apps/web
npx cap add android    # first generation only
npx cap sync android
./android/gradlew.bat -p android assembleDebug
Pop-Location
```

The debug APK is produced by Gradle under `apps/web/android/app/build/outputs/apk/debug/` only after a successful build. The `android-debug.yml` workflow uploads it as a workflow artifact; this README does not claim that artifact exists.

Release AAB builds require the repository owner to supply an Android keystore and signing secrets. No keystore is committed. See [android/README.md](android/README.md) and the `android-release.yml` workflow for the exact secrets and Play Console internal-testing steps.

## Privacy and security

- Treat profile data, emergency text/audio, locations, tokens, and summaries as sensitive.
- Browser authentication uses Secure, HttpOnly, SameSite cookies; tokens are not stored in local storage.
- Bystander links expose a server-side allowlisted projection through short-lived, hashed, revocable, rate-limited tokens.
- Private documents and provider payloads are excluded from PWA caches, analytics, logs, and test snapshots.
- AI prompts and untrusted uploads cannot invoke privileged operations.
- Deployment secrets are supplied at deploy time and redacted from command output.

This prototype still requires a jurisdiction-specific privacy, retention, threat-model, penetration-test, accessibility, and clinical review before real-world use. Report security issues privately to the repository owner; do not include sensitive data in a public issue.

## Documentation

- [Product requirements](docs/PRODUCT_REQUIREMENTS.md)
- [Architecture](docs/ARCHITECTURE.md)
- [AI safety](docs/AI_SAFETY.md)
- [Security](docs/SECURITY.md)
- [API](docs/API.md)
- [Test plan](docs/TEST_PLAN.md)
- [Three-minute demo](docs/DEMO_SCRIPT.md)
- [Deployment and rollback](docs/DEPLOYMENT.md)
- [Implementation plan and traceability](docs/IMPLEMENTATION_PLAN.md)

## Known prototype limitations

- No public Azure URL, external-provider delivery, APK, AAB, or Play release has been produced by the repository alone.
- Protocol content is demonstration-only and has not received the independent clinical/localization review required for real use.
- Azure Bicep still requires subscription-side validation, regional SKU/quota review, cost approval, a real deployment rehearsal, backup/restore proof, and security/accessibility testing against the deployed revision.
- Blob, Service Bus, SignalR Service, notification, and map resources/adapters must be treated as unavailable or mock unless their configured production implementation and health/delivery evidence are observed.
- Final clean-checkout test, coverage, Playwright screenshot, and Android artifact results belong in [the verification log](docs/TEST_PLAN.md); pending rows are not passes.

## Roadmap

1. Independent clinical review and signed, country-specific protocol publishing.
2. Privacy impact assessment, retention/deletion policy, penetration testing, and formal accessibility audit.
3. Real provider certification with delivery reconciliation and operational runbooks.
4. Regional disaster recovery, encrypted backup/restore exercises, and audited key rotation.
5. Carefully evaluated additional languages, speech models, low-bandwidth behavior, and device coverage.
6. Store-distributed Android/iOS shells only after the PWA, privacy disclosures, permission prompts, and signing process pass review.

## License

Code is available under the [MIT License](LICENSE). The license does not grant clinical approval, medical-device certification, or permission to use third-party medical content.

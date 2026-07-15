# Three-minute demo script

## What this demo proves

The mock-mode demo shows an accessible, multilingual emergency coordination flow using fictional data: immediate access to India's `112` call action, bounded AI-style fact extraction, deterministic selection of a demonstration protocol, family task coordination, realtime state, and a provenance-aware handover.

It does **not** place a call, diagnose, dispatch help, deliver a message, contact a hospital, prove clinical approval, or exercise a real OpenAI/provider integration.

Every protocol shown is **Demonstration guidance requiring clinical review before production use.**

## Preflight (before the timer)

1. Copy `.env.example` to `.env`, set a development-only `DEMO_PASSWORD`, and keep `USE_MOCK_PROVIDERS=true`.
2. Start the app with `docker compose up --build --detach`.
3. Run:

   ```powershell
   ./infrastructure/scripts/health-check.ps1 -BaseUrl http://localhost:8080
   ./infrastructure/scripts/smoke-test.ps1 -BaseUrl http://localhost:8080
   ```

4. Open two clean browser contexts at `http://localhost:8080`: owner and family. Keep browser zoom at 100% for the timed layout; demonstrate zoom/keyboard separately.
5. Log the owner in as `demo@goldenhour.ai` and the family context as `family@goldenhour.ai`, using the value supplied through `Seed__DemoPassword`/`DEMO_PASSWORD`.
6. Confirm the fictional Raj Kumar demo profile is visible, Hindi is available, the emergency number reads `112`, and the UI indicates mock mode where designed.
7. Do not proceed if the call action, disclaimer, protocol review label, or degraded-state controls are absent. Do not use a real person's details.

If the app or realtime connection is unhealthy, demonstrate the documented degraded/static path rather than stating that a hidden integration works.

## Timed presentation

### 0:00–0:20 — Frame the problem

On the owner home screen:

> “In an emergency, families have facts, calls, and practical tasks scattered across people. Golden Hour AI keeps the emergency call first, then turns reported information into a coordinated, clearly sourced handover.”

Point to the dominant emergency control and use `Tab` once or twice to show visible focus. Say:

> “This is a coordination prototype—not a doctor, dispatcher, or replacement for emergency services.”

### 0:20–0:45 — Start help without waiting for AI

Activate **Start emergency**, choose **Help a family member**, then **Chest pain**.

Point to **Call 112** before entering any description:

> “The call action is already available. It never waits for AI, and pressing it is not recorded as a connected call unless the user explicitly confirms that.”

Do not actually place a call during the demo.

### 0:45–1:15 — Preserve Hindi and show uncertainty

Choose text input and paste:

> मेरे पिताजी को अचानक सीने में दर्द और बहुत पसीना आ रहा है। उन्हें बोलने में भी परेशानी हो रही है।

Submit it. Show the original Hindi and the separate normalized facts. Point out language/confidence and any confirmation prompt:

> “Mock extraction preserves the original report, labels uncertainty, and asks at most three critical factual questions. It cannot diagnose, invent dosage, or report that help is coming.”

If asked, answer only facts explicitly defined by the fictional scenario. Do not improvise symptoms or clinical instructions.

### 1:15–1:40 — Reviewed protocol, not generated treatment

Show the chest-pain action view. Point to protocol ID/version, review status, emergency call action, one-at-a-time layout, and the clinical-review label:

> “The model does not write this guidance. Backend safety rules select a versioned static protocol. If AI times out or fails validation, the same category-based fallback remains available.”

Avoid reading the whole protocol aloud. Never describe the demonstration content as medical advice.

### 1:40–2:10 — Coordinate the family

Open the task board and assign/choose one allowlisted practical task such as **Bring identification and insurance documents** or **Guide the responder to the location**. In the family browser, accept and complete it.

Return to the owner view and show the update/timeline:

> “The backend validates every task against an allowed catalogue, commits it once, then SignalR notifies authorized session participants. The server snapshot—not a browser message—is authoritative.”

If SignalR is disconnected, point out the accessible connection banner and REST polling/refetch behavior. Do not hide the failure or pretend the update was realtime.

### 2:10–2:38 — Show minimum sharing and handover provenance

Briefly show the QR/share view without reading the raw token. Point out its expiry/revoke control and limited projection:

> “Anonymous access uses a short-lived token whose hash—not the raw token—is stored. It exposes only approved emergency fields; insurance, address, private documents, and complete records are hidden by default.”

Open **Hospital handover** and point to the original report, chronological events, confirmed/unconfirmed labels, profile source, missing information, protocol version, language, confidence, and disclaimer.

### 2:38–3:00 — Close with limits and resilience

> “This prototype makes uncertainty and failure visible. AI outage uses the manual category and static protocol; microphone and location denial fall back to text; realtime failure polls the server; only noncritical idempotent updates may queue offline. Calls are never queued or automatically retried.”

Finish on the persistent call action/disclaimer:

> “Golden Hour AI turns panic into coordinated action while keeping clinical guidance, external success, and user-reported facts clearly separated.”

## Optional accessibility/degraded encore (outside three minutes)

- Switch to **Simple mode** and show only the emergency action, call, capture, active task, and family status.
- Switch English to Hindi, then Urdu to show RTL while the emergency number remains readable.
- Enable 200% zoom, reduced motion, or Windows forced colors and repeat keyboard navigation.
- Deny microphone/location permissions and show immediate text/manual-location alternatives.
- Stop the network in DevTools, reload the shell, show the stale/offline notice and reviewed protocol, then reconnect and verify one queued noncritical event syncs once.
- Disable the mock AI provider or trigger its documented failure fixture and show a bounded static fallback—never an endless spinner.

## Presenter safety checklist

- Use only fictional demo data and a local-only demo password.
- Never say “diagnosed,” “ambulance dispatched,” “hospital notified,” “message delivered,” or “call connected” without the exact verified evidence—which this mock demo does not provide.
- Never claim the displayed protocol is clinically approved.
- Never expose a share/refresh token, cookie, secret, raw provider payload, or private field on a projected screen.
- If a step fails, describe the observed failure and safe fallback. Do not skip forward and claim success.
- Report a hosted URL, test result, screenshot, APK, or AAB only if that artifact was actually produced and inspected.

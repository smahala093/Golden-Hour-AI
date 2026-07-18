# AI safety

## Safety position

Golden Hour AI uses a language model as an uncertain incident extractor and optional coordination-task-code suggester. It is not a clinician, dispatcher, or source of emergency actions. The configured emergency-call action is available before and throughout AI work. Only deterministic backend rules and versioned files in `apps/api/Protocols` can select an action shown as protocol guidance. Family, responder, and hospital summaries are currently assembled deterministically by the server; model translation and model-authored summaries are not active.

All prototype content is labeled: **Demonstration guidance requiring clinical review before production use.**

## Allowed and prohibited responsibilities

| AI may, only for an enabled and reviewed operation | AI must never |
| --- | --- |
| Detect likely input language with confidence | Diagnose or state that a diagnosis is likely/certain |
| Preserve and normalize reported facts | Invent an observation, medication, dosage, treatment plan, or clinical claim |
| Extract the strict incident contract | Mark a call/message/notification/responder as successful |
| Identify missing factual questions, maximum three | Delay, obscure, or gate the emergency-call action |
| Suggest IDs from an allowlisted coordination-task catalogue | Assign permissions, call providers, write arbitrary state, or execute model text |
| Translate already approved content while preserving names (future-gated; inactive) | Present generated text as reviewed clinical guidance |
| Draft family/responder/hospital summaries with provenance (future-gated; inactive) | Hide uncertainty, transform unknown into no, or merge confirmed/unconfirmed facts |

## Panic-to-protocol pipeline

```mermaid
flowchart TD
    Call["Emergency call action rendered"]
    Input["Typed text or <=30 s allowlisted audio"]
    Preserve["Preserve original bytes/text and language"]
    Transcribe["Bounded transcription"]
    Minimize["Minimize/redact context; isolate untrusted text"]
    Model["Versioned operation-specific prompt + strict schema"]
    Parse{"JSON parses and schema is exact?"}
    Reject{"Forbidden content, >3 questions, unsupported enum, or unsafe fields?"}
    Confidence{"Confidence >= threshold?"}
    Confirm["Show original + uncertainty; ask user to confirm facts"]
    Manual["Manual category / AI-unavailable fallback"]
    Select["Deterministic reviewed protocol lookup"]
    Localize["Use reviewed resource or labelled English fallback"]
    Output["Display one action + source/protocol version/disclaimer"]
    Audit["Record operation metadata without sensitive prompt"]

    Call --- Input
    Input --> Preserve --> Transcribe --> Minimize --> Model --> Parse
    Parse -- No --> Manual
    Parse -- Yes --> Reject
    Reject -- Yes --> Manual
    Reject -- No --> Confidence
    Confidence -- No --> Confirm --> Select
    Confidence -- Yes --> Select
    Manual --> Select
    Select --> Localize --> Output
    Model -.-> Audit
    Parse -.-> Audit
    Output -.-> Audit
```

No branch waits indefinitely. Timeout, refusal, rate limit, provider outage, unexpected content type, parse failure, schema failure, or policy failure terminates the model path and selects the manual/static path.

## Extraction contract

The model candidate must contain exactly the agreed fields and enums:

```json
{
  "detectedLanguage": "string",
  "languageConfidence": 0.0,
  "incidentCategory": "string",
  "patientRelationship": "self|family|bystander|unknown",
  "observations": ["string"],
  "reportedSymptomStartTime": "string|null",
  "isConscious": "yes|no|unknown",
  "isBreathingNormally": "yes|no|unknown",
  "isHeavyBleedingReported": "yes|no|unknown",
  "locationDescription": "string|null",
  "urgencyClassification": "emergency|urgent|unknown",
  "criticalMissingQuestions": [
    {
      "id": "string",
      "question": "string",
      "answerType": "yes_no|single_choice|time|text"
    }
  ],
  "handoverFacts": ["string"],
  "uncertainties": ["string"],
  "confidence": 0.0
}
```

Validation requirements:

- Reject additional/missing properties, non-finite or out-of-range confidence, unsupported categories/enums, overlong values/arrays, control characters, and more than three questions.
- Treat empty, contradictory, or ambiguous states as unknown/uncertain; never coerce unknown to no.
- Run forbidden-content checks over every model string, not just summary fields.
- Do not display a model-provided action. Map the validated category/facts to an internal protocol ID and load its actions from the repository.
- Map suggested task IDs through the backend catalogue and authorization/state rules; ignore model-supplied labels or commands.
- Verify any translated structure against the source IDs and immutable medical/proper-noun spans.

## Prompt and tool boundaries

Versioned prompt resources exist for incident extraction, translation, responder summary, hospital handover, and coordination-task suggestion. The active provider path uses the incident-extraction and coordination-task-suggestion prompts. Suggested codes are treated as untrusted and reduced through the backend task allowlist; summaries are generated deterministically from labelled fields. Translation and AI-authored summary prompts remain inactive until structural preservation, clinical/localization review, and adversarial tests prove that approved instructions and proper nouns cannot be altered. Direct user text is delimited as untrusted data and cannot override system policy or become an executable instruction.

The provider receives only the minimum fields required for the active operation. Document upload/AI document processing and model tools are not implemented or enabled. Profile fields and prior messages are not sent to the active model operations. If tools or document processing are added later, they require a separately reviewed allowlisted dispatcher, resource authorization, argument validation, idempotency, prompt-injection tests, and new privacy review. The model never holds credentials and never calls notification, sharing, token, or session-state operations directly.

## Confidence and confirmation

The configured threshold is a product safety signal, not a medical probability. Below threshold—or when language confidence is insufficient—the UI:

1. Keeps the emergency-call action present.
2. Shows the original transcript.
3. Labels the interpretation uncertain.
4. Presents extracted facts for confirmation/correction.
5. Asks no more than three critical factual questions.
6. Uses the manually selected category and reviewed static protocol in parallel.

The UI never presents a confidence number as certainty, severity, survival probability, or clinical accuracy.

## Provider resilience

| Failure | Safe behavior | Required evidence |
| --- | --- | --- |
| API key absent / mock mode | Deterministic mock extraction or manual category; visible mock indicator where appropriate | Mock scenario contract tests |
| Timeout / network / 429 | The safe provider request is attempted at most three times with bounded exponential delay; then static fallback. Application mutations are not retried by model text. Jitter is a production hardening item. | Provider timeout/rate-limit response tests |
| Refusal | Preserve original, record refusal metadata, static fallback | Provider refusal test |
| Malformed JSON / unsupported enum | Reject the entire candidate; never partially apply | Strict contract tests |
| Diagnosis, dosage, treatment, success claim | Policy rejection and static fallback; no unsafe string reaches protocol UI | Adversarial output tests |
| Prompt injection | Treat direct input as data or reject it; no tool/state/secret effect | Direct-input forbidden-content tests; document tests remain a release gate if uploads are added |
| Translation unavailable | Display approved English/static content with a language notice; no model translation is currently invoked | Web fallback assertions; structural translation evaluation remains a release gate |
| Speech failure / low-confidence language | Fall back to editable text/language chooser | Audio/language tests |

Provider retry attempts have no direct application-state privilege. Session, timeline, capability, and task mutations use their own server-side idempotency/concurrency rules; an AI response cannot replay them.

## Provenance in summaries

Every handover fact carries a source class and confirmation state:

- Original user report.
- Self-reported emergency profile snapshot.
- AI-extracted candidate.
- User/bystander-confirmed observation.
- Reported family action.
- Unknown or missing.

Summaries must not merge source classes into a fluent narrative that implies verification. Current summary content retains the relevant original description, source labels, protocol/uncertainty fields, and missing facts for each summary kind. AI operation/provider/model/latency/token metadata is stored in server-side `AiOperation` records for the two active model operations; it is not currently exposed in the summary UI.

## Logging and audit

Persist operation type, provider, configured model, outcome class, latency, and token usage when the provider supplies it. Mock operations intentionally leave token counts null. Prompt/template version and retry count are not currently stored as separate fields. Do not log input text/audio, model output, profile values, bystander/share tokens, authorization cookies, raw provider payloads, or secret-bearing headers. Use opaque operation/session IDs and correlation IDs.

Audit policy decisions such as fallback, forbidden-output rejection, fact confirmation, protocol selection, and privileged state transitions without duplicating sensitive values. Telemetry sampling and exporter configuration must preserve these exclusions.

## Safety evaluation evidence and release gates

The checked-in deterministic tests cover:

- Hindi chest-pain demo input retention.
- Low confidence plus unknown/ambiguous consciousness or breathing.
- Provider timeout, rate limit, refusal, malformed JSON, missing/additional fields, unsupported enum, excessive questions, and overlong values.
- Model-generated diagnosis, dosage, treatment plan, unsupported clinical claim, and false call/provider success.
- Direct-text attempts to inject actions or false external-success claims.

The following remain production release gates rather than claimed checked-in evidence:

- Uploaded-document prompt injection, because document ingestion is not implemented.
- Model translation preservation for medicine/allergy/proper nouns and protocol structure, because model translation is inactive.
- Unsupported-language and contradictory-report behavior beyond the currently checked-in cases.
- Clinician/localization evaluation of every protocol and supported locale.
- Deployed-provider drift, refusal, latency, fallback, and privacy monitoring.

Tests assert both the absence of prohibited content/effects and the presence of a bounded call/static-fallback path. Passing a small evaluation set is not clinical validation.

## Change control

Prompt, schema, threshold, protocol, model, policy, and translation changes are safety-relevant. A change must include:

1. A versioned diff and named owner.
2. Positive, negative, adversarial, multilingual, accessibility, and fallback regression results.
3. Review that protocols remain repository-authored and visibly labeled.
4. Privacy review of any newly transmitted field.
5. Rollback target and compatibility assessment for active sessions.

Before production, obtain independent clinical review of every protocol and locale, formalize model evaluation thresholds, monitor drift/refusals/fallbacks without collecting sensitive prompts, and establish a safety incident response process.

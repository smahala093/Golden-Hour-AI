import { z } from 'zod';
import { DEMO_SHARE_TOKEN, demoProfile, demoReadiness, demoSession, emptySession } from './demoData';
import { incidentExtractionSchema, type EmergencyProfile, type EmergencyProtocol, type EmergencySession, type ReadinessResult, type TaskStatus } from './types';

const API_BASE = (import.meta.env.VITE_API_BASE_URL as string | undefined)?.replace(/\/$/, '') ?? '';
export const MOCK_MODE = import.meta.env.VITE_MOCK_MODE === 'true';

export class ApiError extends Error {
  constructor(
    message: string,
    public readonly status: number,
    public readonly correlationId?: string,
  ) {
    super(message);
    this.name = 'ApiError';
  }
}

export interface ShareTokenResult { token: string; tokenId: string; expiresAtUtc: string }
export interface BystanderView {
  sessionId: string;
  patientName: string | null;
  approximateAge: number | null;
  category: string;
  location: string;
  allergies: string[];
  conditions: string[];
  medicines: string[];
  emergencyContact: { name: string; relationship: string; phone: string } | null;
  emergencyNumber: string;
  protocol: EmergencyProtocol | null;
}

async function request(path: string, init?: RequestInit): Promise<unknown> {
  const response = await fetch(`${API_BASE}${path}`, {
    ...init,
    signal: init?.signal ?? AbortSignal.timeout(15_000),
    credentials: 'include',
    headers: { Accept: 'application/json', ...(init?.body ? { 'Content-Type': 'application/json' } : {}), ...init?.headers },
  });
  if (!response.ok) {
    let message = `Request failed with status ${response.status}`;
    let correlationId: string | undefined;
    try {
      const problem = (await response.json()) as { detail?: string; title?: string; correlationId?: string };
      message = problem.detail ?? problem.title ?? message;
      correlationId = problem.correlationId;
    } catch {
      // RFC 7807 was unavailable; keep the safe generic message.
    }
    throw new ApiError(message, response.status, correlationId);
  }
  if (response.status === 204) return null;
  const contentType = response.headers.get('content-type') ?? '';
  return contentType.includes('json') ? response.json() : null;
}

const backendSessionSchema = z.object({
  id: z.string(),
  status: z.string().optional(),
  selectedCategory: z.string().optional(),
  patientRelationship: z.string().optional(),
  emergencyNumber: z.string().optional(),
  concurrencyToken: z.string().optional(),
  anonymousAccessToken: z.string().nullable().optional(),
  createdAtUtc: z.string().optional(),
  updatedAtUtc: z.string().optional(),
  originalInput: z.string().nullable().optional(),
  originalLanguage: z.string().nullable().optional(),
  normalizedTranscript: z.string().nullable().optional(),
  incidentFacts: z.unknown().nullable().optional(),
  interpretationUncertain: z.boolean().optional(),
  protocol: z.object({
    id: z.string(),
    version: z.string(),
    reviewStatus: z.string(),
    notice: z.string(),
    emergencyCallInstruction: z.string(),
    doActions: z.array(z.union([z.string(), z.object({ id: z.string().optional(), title: z.string(), detail: z.string().optional() })])),
    doNotActions: z.array(z.string()),
    escalationRule: z.string(),
  }).nullable().optional(),
  tasks: z.array(z.object({
    id: z.string(),
    code: z.string().optional(),
    title: z.string(),
    isCritical: z.boolean().optional(),
    status: z.string(),
    assignedParticipantId: z.string().nullable().optional(),
    acceptedAtUtc: z.string().nullable().optional(),
    completedAtUtc: z.string().nullable().optional(),
    concurrencyToken: z.string().optional(),
  })).optional(),
  timeline: z.array(z.object({ id: z.string(), sequence: z.number().optional(), type: z.string().optional(), message: z.string(), createdAtUtc: z.string() })).optional(),
  participants: z.array(z.unknown()).optional(),
  locations: z.array(z.unknown()).optional(),
}).passthrough();

const backendProfileSchema = z.object({
  id: z.string(), fullName: z.string(), dateOfBirth: z.string().nullable(), bloodGroup: z.string().nullable(), preferredLanguage: z.string(), responseMode: z.string(),
  contacts: z.array(z.object({ id: z.string(), name: z.string(), relationship: z.string(), phoneNumber: z.string(), isVerified: z.boolean() }).passthrough()),
  allergies: z.array(z.object({ name: z.string() }).passthrough()), conditions: z.array(z.object({ name: z.string() }).passthrough()), medications: z.array(z.object({ name: z.string() }).passthrough()),
  procedures: z.array(z.object({ name: z.string(), year: z.number().nullable().optional() }).passthrough()),
  preferredHospital: z.object({ name: z.string(), phoneNumber: z.string().nullable().optional() }).nullable(),
  sharingPreference: z.object({ shareName: z.boolean(), shareApproximateAge: z.boolean(), shareAllergies: z.boolean(), shareConditions: z.boolean(), shareMedications: z.boolean(), shareEmergencyContact: z.boolean() }).nullable(),
  reviewedAtUtc: z.string().nullable(), insuranceDetails: z.string().nullable().optional(), doctorContact: z.string().nullable().optional(), locationPermissionReviewed: z.boolean().optional(),
}).passthrough();

const backendExtractionSchema = z.object({
  detectedLanguage: z.string(), languageConfidence: z.number(), incidentCategory: z.string(), patientRelationship: z.string(), observations: z.array(z.string()), reportedSymptomStartTime: z.string().nullable(), isConscious: z.string(), isBreathingNormally: z.string(), isHeavyBleedingReported: z.string(), locationDescription: z.string().nullable(), urgencyClassification: z.string(), criticalMissingQuestions: z.array(z.object({ id: z.string(), question: z.string(), answerType: z.string() }).passthrough()).max(3), handoverFacts: z.array(z.string()), uncertainties: z.array(z.string()), confidence: z.number(),
}).passthrough();

function mapProfile(payload: unknown): EmergencyProfile {
  const raw = backendProfileSchema.parse(payload);
  const sharing = raw.sharingPreference;
  const shareFields = [sharing?.shareName ? 'name' : null, sharing?.shareApproximateAge ? 'approximateAge' : null, sharing?.shareAllergies ? 'allergies' : null, sharing?.shareConditions ? 'conditions' : null, sharing?.shareMedications ? 'medicines' : null, sharing?.shareEmergencyContact ? 'emergencyContact' : null].filter((field): field is string => field !== null);
  const responseMode = raw.responseMode.toLowerCase();
  return { name: raw.fullName, dateOfBirth: raw.dateOfBirth ?? '', preferredLanguage: raw.preferredLanguage, responseMode: responseMode === 'audio' || responseMode === 'both' ? responseMode : 'text', bloodGroup: raw.bloodGroup ?? undefined, allergies: raw.allergies.map((item) => item.name), conditions: raw.conditions.map((item) => item.name), medicines: raw.medications.map((item) => item.name), procedures: raw.procedures.map((item) => `${item.name}${item.year ? `, ${item.year}` : ''}`), preferredHospital: raw.preferredHospital?.name ?? '', doctor: raw.doctorContact ?? '', insurance: raw.insuranceDetails ?? '', contacts: raw.contacts.map((contact) => ({ id: contact.id, name: contact.name, relationship: contact.relationship, phone: contact.phoneNumber, verified: contact.isVerified })), shareFields, reviewedAt: raw.reviewedAtUtc ?? new Date(0).toISOString(), locationPermissionReviewed: raw.locationPermissionReviewed ?? false, qrGenerated: false };
}

function profileRequest(profile: EmergencyProfile): unknown {
  const procedurePattern = /^(.*?)(?:,\s*(\d{4}))?$/;
  return { fullName: profile.name, dateOfBirth: profile.dateOfBirth || null, bloodGroup: profile.bloodGroup || null, preferredLanguage: profile.preferredLanguage, responseMode: profile.responseMode, insuranceDetails: profile.insurance || null, doctorContact: profile.doctor || null, allergyStatusCompleted: true, medicationStatusCompleted: true, locationPermissionReviewed: profile.locationPermissionReviewed, reviewed: true, contacts: profile.contacts.map((contact) => ({ name: contact.name, relationship: contact.relationship, phoneNumber: contact.phone, isVerified: contact.verified })), allergies: profile.allergies.map((name) => ({ name })), conditions: profile.conditions.map((name) => ({ name })), medications: profile.medicines.map((name) => ({ name })), procedures: profile.procedures.map((value) => { const match = procedurePattern.exec(value); return { name: match?.[1]?.trim() || value, year: match?.[2] ? Number(match[2]) : null }; }), preferredHospital: profile.preferredHospital ? { name: profile.preferredHospital, phoneNumber: null } : null, sharing: { shareName: profile.shareFields.includes('name'), shareApproximateAge: profile.shareFields.includes('approximateAge'), shareAllergies: profile.shareFields.includes('allergies'), shareConditions: profile.shareFields.includes('conditions'), shareMedications: profile.shareFields.includes('medicines'), shareEmergencyContact: profile.shareFields.includes('emergencyContact'), reviewed: true } };
}

function categoryToWire(category: EmergencySession['category']): string {
  return { 'chest-pain': 'chest_pain', 'breathing-difficulty': 'breathing_difficulty', 'fall-injury': 'fall_or_injury', unconscious: 'unconscious', seizure: 'seizure', 'heavy-bleeding': 'heavy_bleeding', 'road-accident': 'road_accident', 'allergic-reaction': 'allergic_reaction', 'child-emergency': 'child_emergency', unknown: 'unknown' }[category];
}

function normalizeCategory(value?: string): EmergencySession['category'] {
  const map: Record<string, EmergencySession['category']> = {
    chestpain: 'chest-pain', chest_pain: 'chest-pain', 'chest-pain': 'chest-pain',
    breathingdifficulty: 'breathing-difficulty', breathing_difficulty: 'breathing-difficulty', 'breathing-difficulty': 'breathing-difficulty',
    fallorinjury: 'fall-injury', fall_injury: 'fall-injury', fall_or_injury: 'fall-injury', 'fall-injury': 'fall-injury',
    unconscious: 'unconscious', seizure: 'seizure',
    heavybleeding: 'heavy-bleeding', heavy_bleeding: 'heavy-bleeding', 'heavy-bleeding': 'heavy-bleeding',
    roadaccident: 'road-accident', road_accident: 'road-accident', 'road-accident': 'road-accident',
    allergicreaction: 'allergic-reaction', allergic_reaction: 'allergic-reaction', 'allergic-reaction': 'allergic-reaction',
    childemergency: 'child-emergency', child_emergency: 'child-emergency', 'child-emergency': 'child-emergency',
    unknown: 'unknown', other: 'unknown',
  };
  return map[(value ?? 'unknown').toLowerCase()] ?? 'unknown';
}

function mapStatus(value?: string): EmergencySession['status'] {
  const normalized = value?.toLowerCase();
  if (normalized === 'departed') return 'departed';
  if (normalized === 'athospital' || normalized === 'at-hospital' || normalized === 'arrivedathospital' || normalized === 'arrived_at_hospital') return 'at-hospital';
  if (normalized === 'closed') return 'closed';
  return 'active';
}

function mapTaskStatus(value: string): TaskStatus {
  const normalized = value.toLowerCase();
  if (normalized === 'accepted' || normalized === 'declined' || normalized === 'completed') return normalized;
  return 'open';
}

function mapProtocol(raw: z.infer<typeof backendSessionSchema>['protocol']): EmergencyProtocol | undefined {
  if (!raw) return undefined;
  return {
    id: raw.id,
    title: raw.id.replaceAll('-', ' '),
    version: raw.version,
    reviewStatus: raw.reviewStatus,
    country: 'IN',
    emergencyCallInstruction: raw.emergencyCallInstruction,
    doActions: raw.doActions.map((action, index) => typeof action === 'string'
      ? { id: `action-${index + 1}`, title: action, detail: '' }
      : { id: action.id ?? `action-${index + 1}`, title: action.title, detail: action.detail ?? '' }),
    doNotActions: raw.doNotActions,
    escalationRule: raw.escalationRule,
    source: 'Reviewed server protocol catalogue',
    disclaimer: raw.notice,
  };
}

function mapSession(payload: unknown): EmergencySession {
  const raw = backendSessionSchema.parse(payload);
  const backendExtraction = backendExtractionSchema.safeParse(raw.incidentFacts);
  const relationshipValue = raw.patientRelationship?.toLowerCase();
  const relationship: EmergencySession['relationship'] = relationshipValue === 'self' || relationshipValue === 'family' || relationshipValue === 'bystander' ? relationshipValue : 'unknown';
  const triState = (value: string): 'yes' | 'no' | 'unknown' => value.toLowerCase() === 'yes' ? 'yes' : value.toLowerCase() === 'no' ? 'no' : 'unknown';
  const extraction = backendExtraction.success ? incidentExtractionSchema.parse({
    ...backendExtraction.data,
    incidentCategory: normalizeCategory(backendExtraction.data.incidentCategory),
    patientRelationship: relationship,
    isConscious: triState(backendExtraction.data.isConscious),
    isBreathingNormally: triState(backendExtraction.data.isBreathingNormally),
    isHeavyBleedingReported: triState(backendExtraction.data.isHeavyBleedingReported),
    urgencyClassification: ['emergency', 'urgent'].includes(backendExtraction.data.urgencyClassification.toLowerCase()) ? backendExtraction.data.urgencyClassification.toLowerCase() : 'unknown',
    criticalMissingQuestions: backendExtraction.data.criticalMissingQuestions.map((question) => ({ ...question, answerType: ['yes_no', 'single_choice', 'time', 'text'].includes(question.answerType.toLowerCase()) ? question.answerType.toLowerCase() : 'text' })),
  }) : { ...emptySession.extraction, detectedLanguage: raw.originalLanguage ?? 'unknown', incidentCategory: normalizeCategory(raw.selectedCategory), patientRelationship: relationship, uncertainties: ['The server did not provide a valid incident interpretation.'] };
  const firstLocation = raw.locations?.[0];
  const location = typeof firstLocation === 'string'
    ? firstLocation
    : firstLocation && typeof firstLocation === 'object' && 'description' in firstLocation
      ? String((firstLocation as { description?: unknown }).description ?? '')
      : extraction.locationDescription ?? '';

  const participantOptions = (raw.participants ?? []).flatMap((participant) => {
    if (!participant || typeof participant !== 'object' || !('id' in participant) || !('displayName' in participant)) return [];
    return [{ id: String((participant as { id: unknown }).id), label: String((participant as { displayName: unknown }).displayName) }];
  });
  const participantName = (id: string | null | undefined) => participantOptions.find((participant) => participant.id === id)?.label ?? (id ? 'Assigned participant' : 'Unassigned');
  return {
    id: raw.id,
    owner: demoSession.owner,
    patient: demoSession.patient,
    relationship: extraction.patientRelationship,
    category: normalizeCategory(raw.selectedCategory),
    status: mapStatus(raw.status),
    createdAt: raw.createdAtUtc ?? new Date().toISOString(),
    updatedAt: raw.updatedAtUtc ?? new Date().toISOString(),
    location,
    originalInput: raw.originalInput ?? '',
    normalizedInput: raw.normalizedTranscript ?? '',
    extraction,
    timeline: (raw.timeline ?? []).sort((a, b) => (a.sequence ?? 0) - (b.sequence ?? 0)).map((event) => ({ id: event.id, at: event.createdAtUtc, title: event.type?.replaceAll('-', ' ') ?? 'Update', detail: event.message, source: 'confirmed' })),
    tasks: (raw.tasks ?? []).map((task) => ({ id: task.id, title: task.title, assignee: participantName(task.assignedParticipantId), assignedParticipantId: task.assignedParticipantId ?? undefined, status: mapTaskStatus(task.status), critical: task.isCritical ?? false, updatedAt: task.completedAtUtc ?? task.acceptedAtUtc ?? raw.updatedAtUtc ?? new Date().toISOString(), concurrencyToken: task.concurrencyToken ?? '' })),
    participants: participantOptions.map((participant) => participant.label),
    participantOptions,
    protocolVersion: raw.protocol ? `${raw.protocol.id}/${raw.protocol.version}` : demoSession.protocolVersion,
    protocol: mapProtocol(raw.protocol),
    sharingFields: demoProfile.shareFields,
    emergencyNumber: raw.emergencyNumber ?? '112',
    concurrencyToken: raw.concurrencyToken ?? '',
  };
}

async function withMockFallback<T>(operation: () => Promise<T>, fallback: T): Promise<T> {
  if (MOCK_MODE) return Promise.resolve(fallback);
  return operation();
}

export const api = {
  login(email: string, password: string): Promise<unknown> {
    return withMockFallback(() => request('/api/v1/auth/login', { method: 'POST', body: JSON.stringify({ email, password }) }), { authenticated: true, demo: true });
  },
  register(name: string, email: string, password: string): Promise<unknown> {
    return withMockFallback(() => request('/api/v1/auth/register', { method: 'POST', body: JSON.stringify({ name, email, password }) }), { authenticated: true, demo: true });
  },
  logout(): Promise<unknown> {
    return withMockFallback(() => request('/api/v1/auth/logout', { method: 'POST' }), null);
  },
  getCurrentUser(): Promise<unknown> {
    return withMockFallback(() => request('/api/v1/auth/me'), { authenticated: true, demo: true });
  },
  getProfile(): Promise<EmergencyProfile> {
    return withMockFallback(async () => mapProfile(await request('/api/v1/profile')), demoProfile);
  },
  updateProfile(profile: EmergencyProfile): Promise<EmergencyProfile> {
    return withMockFallback(async () => mapProfile(await request('/api/v1/profile', { method: 'PUT', body: JSON.stringify(profileRequest(profile)) })), profile);
  },
  getReadiness(): Promise<ReadinessResult> {
    return withMockFallback(async () => {
      const result = z.object({ score: z.number().min(0).max(100), checks: z.array(z.object({ code: z.string(), label: z.string(), complete: z.boolean(), weight: z.number() })) }).parse(await request('/api/v1/readiness'));
      return { score: result.score, completed: result.checks.filter((check) => check.complete).map((check) => check.label), improvements: result.checks.filter((check) => !check.complete).map((check) => check.label) };
    }, demoReadiness);
  },
  createSession(category: EmergencySession['category'], relationship: EmergencySession['relationship']): Promise<EmergencySession> {
    const fallback = { ...demoSession, category, relationship, extraction: { ...demoSession.extraction, incidentCategory: category, patientRelationship: relationship } };
    return withMockFallback(async () => mapSession(await request('/api/v1/sessions', { method: 'POST', headers: { 'Idempotency-Key': crypto.randomUUID() }, body: JSON.stringify({ selectedCategory: categoryToWire(category), patientRelationship: relationship, typedLocation: null, countryCode: 'IN' }) })), fallback);
  },
  createAnonymousSession(category: EmergencySession['category'], typedLocation: string): Promise<{ session: EmergencySession; accessToken: string }> {
    if (MOCK_MODE) return Promise.resolve({ session: { ...demoSession, relationship: 'bystander', category }, accessToken: 'demo-anonymous-access' });
    return request('/api/v1/sessions', { method: 'POST', headers: { 'Idempotency-Key': crypto.randomUUID() }, body: JSON.stringify({ selectedCategory: categoryToWire(category), patientRelationship: 'bystander', typedLocation: typedLocation || null, countryCode: 'IN' }) }).then((payload) => {
      const raw = backendSessionSchema.parse(payload);
      if (!raw.anonymousAccessToken) throw new ApiError('Temporary emergency access was not issued.', 500);
      return { session: mapSession(payload), accessToken: raw.anonymousAccessToken };
    });
  },
  submitAnonymousIncident(id: string, accessToken: string, input: string, category: EmergencySession['category']): Promise<EmergencySession> {
    if (MOCK_MODE) return Promise.resolve({ ...demoSession, relationship: 'bystander', category, originalInput: input, extraction: { ...demoSession.extraction, patientRelationship: 'bystander', incidentCategory: category } });
    return request(`/api/v1/sessions/${encodeURIComponent(id)}/incident`, { method: 'POST', headers: { 'X-Emergency-Access-Token': accessToken, 'Idempotency-Key': crypto.randomUUID() }, body: JSON.stringify({ originalText: input, selectedLanguage: /[\u0900-\u097f]/u.test(input) ? 'hi' : null, fallbackCategory: categoryToWire(category) }) }).then(mapSession);
  },
  getSession(id: string): Promise<EmergencySession> {
    if (MOCK_MODE) return id === demoSession.id ? Promise.resolve(demoSession) : Promise.reject(new ApiError('Emergency session not found.', 404));
    return request(`/api/v1/sessions/${encodeURIComponent(id)}`).then(mapSession);
  },
  submitIncident(id: string, input: string, location: string): Promise<EmergencySession> {
    const fallback = { ...demoSession, id, originalInput: input || demoSession.originalInput, location: location || demoSession.location };
    return withMockFallback(async () => {
      await request(`/api/v1/sessions/${encodeURIComponent(id)}/incident`, { method: 'POST', headers: { 'Idempotency-Key': crypto.randomUUID() }, body: JSON.stringify({ originalText: input, selectedLanguage: /[\u0900-\u097f]/u.test(input) ? 'hi' : null, fallbackCategory: categoryToWire(fallback.category) }) });
      if (location) {
        const idempotencyKey = crypto.randomUUID();
        await request(`/api/v1/sessions/${encodeURIComponent(id)}/locations`, { method: 'POST', headers: { 'Idempotency-Key': idempotencyKey }, body: JSON.stringify({ latitude: null, longitude: null, description: location, consentProvided: true, idempotencyKey }) });
      }
      return mapSession(await request(`/api/v1/sessions/${encodeURIComponent(id)}`));
    }, fallback);
  },
  answerQuestions(id: string, answers: Record<string, string>): Promise<EmergencySession> {
    return withMockFallback(async () => {
      for (const [questionId, answer] of Object.entries(answers).slice(0, 3)) {
        await request(`/api/v1/sessions/${encodeURIComponent(id)}/answers`, { method: 'POST', headers: { 'Idempotency-Key': crypto.randomUUID() }, body: JSON.stringify({ questionId, answer }) });
      }
      return mapSession(await request(`/api/v1/sessions/${encodeURIComponent(id)}`));
    }, demoSession);
  },
  uploadVoice(id: string, recording: Blob): Promise<EmergencySession> {
    if (MOCK_MODE) return Promise.resolve(demoSession);
    if (!recording.type.startsWith('audio/') || recording.size === 0 || recording.size > 5 * 1024 * 1024) {
      return Promise.reject(new ApiError('The recording format or size is not supported.', 400));
    }
    const form = new FormData();
    form.append('audio', recording, 'incident-recording.webm');
    return fetch(`${API_BASE}/api/v1/sessions/${encodeURIComponent(id)}/voice`, {
      method: 'POST',
      credentials: 'include',
      headers: { Accept: 'application/json', 'Idempotency-Key': crypto.randomUUID() },
      body: form,
      signal: AbortSignal.timeout(15_000),
    }).then(async (response) => {
      if (!response.ok) throw new ApiError('The recording could not be uploaded. Type what happened instead.', response.status);
      return mapSession(await response.json());
});

  },
  addTimeline(id: string, type: 'call-connected' | 'patient-departed' | 'patient-arrived' | 'observation', message: string): Promise<void> {
    return withMockFallback(async () => {
      const idempotencyKey = crypto.randomUUID();
      await request(`/api/v1/sessions/${encodeURIComponent(id)}/timeline`, {
        method: 'POST',
        headers: { 'Idempotency-Key': idempotencyKey },
        body: JSON.stringify({ type, message, idempotencyKey, ...(type === 'call-connected' ? { userConfirmed: true } : {}) }),
      });
    }, undefined);
  },
  updateTask(sessionId: string, taskId: string, status: TaskStatus | 'assigned', assignedParticipantId: string | undefined, concurrencyToken: string): Promise<EmergencySession> {
    const uiStatus: TaskStatus = status === 'assigned' ? 'open' : status;
    const fallback = { ...demoSession, tasks: demoSession.tasks.map((task) => task.id === taskId ? { ...task, status: uiStatus, assignedParticipantId, assignee: demoSession.participantOptions?.find((participant) => participant.id === assignedParticipantId)?.label ?? task.assignee, updatedAt: new Date().toISOString() } : task) };
    return withMockFallback(async () => {
      await request(`/api/v1/sessions/${encodeURIComponent(sessionId)}/tasks/${encodeURIComponent(taskId)}`, { method: 'PATCH', headers: { 'Idempotency-Key': crypto.randomUUID() }, body: JSON.stringify({ status, assignedParticipantId: assignedParticipantId ?? null, concurrencyToken }) });
      return mapSession(await request(`/api/v1/sessions/${encodeURIComponent(sessionId)}`));
    }, fallback);
  },
  createShareToken(id: string): Promise<ShareTokenResult> {
    const fallback = { token: DEMO_SHARE_TOKEN, tokenId: 'demo-token-id', expiresAtUtc: new Date(Date.now() + 30 * 60_000).toISOString() };
    return withMockFallback(async () => {
      const result = z.object({ id: z.string(), token: z.string().min(20), expiresAtUtc: z.string(), path: z.string() }).parse(await request(`/api/v1/sessions/${encodeURIComponent(id)}/share-tokens`, { method: 'POST', headers: { 'Idempotency-Key': crypto.randomUUID() }, body: JSON.stringify({ lifetimeMinutes: 30 }) }));
      return { token: result.token, tokenId: result.id, expiresAtUtc: result.expiresAtUtc };
    }, fallback);
  },
  revokeShareToken(id: string, tokenId: string): Promise<void> {
    return withMockFallback(async () => { await request(`/api/v1/sessions/${encodeURIComponent(id)}/share-tokens/${encodeURIComponent(tokenId)}`, { method: 'DELETE' }); }, undefined);
  },
  getBystander(token: string): Promise<BystanderView> {
    if (MOCK_MODE) {
      if (token !== DEMO_SHARE_TOKEN) return Promise.reject(new ApiError('This emergency link is invalid, expired, or revoked.', 410));
      return Promise.resolve({ sessionId: demoSession.id, patientName: demoProfile.name, approximateAge: 64, category: demoSession.category, location: demoSession.location, allergies: demoProfile.allergies, conditions: demoProfile.conditions, medicines: demoProfile.medicines, emergencyContact: { name: demoProfile.contacts[0]?.name ?? '', relationship: demoProfile.contacts[0]?.relationship ?? '', phone: demoProfile.contacts[0]?.phone ?? '' }, emergencyNumber: '112', protocol: null });
    }
    return request(`/api/v1/bystander/${encodeURIComponent(token)}`).then((payload) => {
      const result = z.object({
        sessionId: z.string(), emergencyNumber: z.string(),
        incidentFacts: z.object({ incidentCategory: z.string() }).passthrough().nullable(),
        protocol: backendSessionSchema.shape.protocol,
        profile: z.object({ name: z.string().nullable(), approximateAge: z.number().nullable(), allergies: z.array(z.string()), conditions: z.array(z.string()), medications: z.array(z.string()), emergencyContact: z.object({ name: z.string(), relationship: z.string(), phoneNumber: z.string() }).passthrough().nullable() }),
        latestLocation: z.object({ description: z.string().nullable(), latitude: z.number().nullable(), longitude: z.number().nullable() }).passthrough().nullable(),
      }).parse(payload);
      const contact = result.profile.emergencyContact;
      return { sessionId: result.sessionId, patientName: result.profile.name, approximateAge: result.profile.approximateAge, category: normalizeCategory(result.incidentFacts?.incidentCategory), location: result.latestLocation?.description ?? '', allergies: result.profile.allergies, conditions: result.profile.conditions, medicines: result.profile.medications, emergencyContact: contact ? { name: contact.name, relationship: contact.relationship, phone: contact.phoneNumber } : null, emergencyNumber: result.emergencyNumber, protocol: mapProtocol(result.protocol) ?? null };
    });
  },
  reportBystanderObservation(token: string, observation: { conscious?: string; breathingNormally?: string; severeBleeding?: string }): Promise<void> {
    return withMockFallback(async () => { await request(`/api/v1/bystander/${encodeURIComponent(token)}/observations`, { method: 'POST', headers: { 'Idempotency-Key': crypto.randomUUID() }, body: JSON.stringify(observation) }); }, undefined);
  },
  reportBystanderLocation(token: string, latitude: number, longitude: number): Promise<void> {
    return withMockFallback(async () => { await request(`/api/v1/bystander/${encodeURIComponent(token)}/location`, { method: 'POST', headers: { 'Idempotency-Key': crypto.randomUUID() }, body: JSON.stringify({ latitude, longitude, consentConfirmed: true }) }); }, undefined);
  },
  closeSession(id: string, concurrencyToken: string): Promise<void> {
    return withMockFallback(async () => { await request(`/api/v1/sessions/${encodeURIComponent(id)}/close`, { method: 'POST', headers: { 'Idempotency-Key': crypto.randomUUID() }, body: JSON.stringify({ concurrencyToken }) }); }, undefined);
  },
};

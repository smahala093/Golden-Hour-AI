import { z } from 'zod';
import { DEMO_SHARE_TOKEN, demoProfile, demoReadiness, demoSession, emptySession } from './demoData';
import { incidentExtractionSchema, type EmergencyParticipant, type EmergencyProfile, type EmergencyProtocol, type EmergencySession, type ParticipantRole, type ReadinessResult, type TaskStatus } from './types';

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

export interface ShareTokenResult { token: string; tokenId: string; expiresAtUtc: string; path: string }
export interface ParticipantInviteResult { participantId: string; token: string; expiresAtUtc: string; path: string }
export interface ContactVerificationChallenge { challenge: string; expiresAtUtc: string; status: string; developmentCode?: string }
export type EmergencySummaryKind = 'family' | 'responder' | 'hospital-handover';
export interface EmergencySummaryResult { id: string; kind: EmergencySummaryKind; content: string; language: string; protocolVersion: string; createdAtUtc: string }
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
  expiresAtUtc: string;
}

let refreshInFlight: Promise<boolean> | null = null;
let authExpiredSignalled = false;

function signalAuthExpired(): void {
  if (authExpiredSignalled || typeof window === 'undefined') return;
  authExpiredSignalled = true;
  window.dispatchEvent(new Event('gh-auth-expired'));
}

async function refreshAccess(): Promise<boolean> {
  if (!refreshInFlight) {
    refreshInFlight = fetch(`${API_BASE}/api/v1/auth/refresh`, { method: 'POST', credentials: 'include', headers: { Accept: 'application/json' }, signal: AbortSignal.timeout(10_000) })
      .then((response) => response.ok)
      .catch(() => false)
      .finally(() => { refreshInFlight = null; });
  }
  return refreshInFlight;
}

async function request(path: string, init?: RequestInit, allowRefresh = true): Promise<unknown> {
  const hasJsonBody = Boolean(init?.body) && !(typeof FormData !== 'undefined' && init?.body instanceof FormData);
  const authPath = path.startsWith('/api/v1/auth/login') || path.startsWith('/api/v1/auth/register') || path.startsWith('/api/v1/auth/refresh') || path.startsWith('/api/v1/auth/logout');
  const response = await fetch(`${API_BASE}${path}`, {
    ...init,
    signal: init?.signal ?? AbortSignal.timeout(15_000),
    credentials: 'include',
    headers: { Accept: 'application/json', ...(hasJsonBody ? { 'Content-Type': 'application/json' } : {}), ...init?.headers },
  });
  if (response.status === 401 && !authPath) {
    if (allowRefresh && await refreshAccess()) return request(path, init, false);
    signalAuthExpired();
  }
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
  if (response.ok && (path.startsWith('/api/v1/auth/login') || path.startsWith('/api/v1/auth/register'))) authExpiredSignalled = false;
  if (response.status === 204) return null;
  const contentType = response.headers.get('content-type') ?? '';
  return contentType.includes('json') ? response.json() : null;
}

async function requestAudio(path: string, allowRefresh = true): Promise<Blob> {
  const response = await fetch(`${API_BASE}${path}`, {
    method: 'POST', credentials: 'include', headers: { Accept: 'audio/mpeg' }, signal: AbortSignal.timeout(20_000),
  });
  if (response.status === 401 && allowRefresh && await refreshAccess()) return requestAudio(path, false);
  if (!response.ok) {
    let message = `Request failed with status ${response.status}`;
    let correlationId: string | undefined;
    try {
      const parsed = z.object({ detail: z.string().optional(), title: z.string().optional(), correlationId: z.string().optional() }).passthrough().safeParse(await response.json());
      if (parsed.success) {
        message = parsed.data.detail ?? parsed.data.title ?? message;
        correlationId = parsed.data.correlationId;
      }
    } catch {
      // Keep the safe generic error when the server did not return RFC 7807 JSON.
    }
    throw new ApiError(message, response.status, correlationId);
  }
  const contentType = response.headers.get('content-type')?.split(';')[0]?.trim().toLowerCase();
  if (contentType !== 'audio/mpeg') throw new ApiError('The protocol audio response had an unsupported format.', 502);
  const audio = await response.blob();
  if (audio.size === 0 || audio.size > 10 * 1024 * 1024) throw new ApiError('The protocol audio response had an invalid size.', 502);
  return audio;
}

const participantSchema = z.object({
  id: z.string(),
  displayName: z.string(),
  role: z.enum(['owner', 'family', 'bystander', 'caregiver']),
  // The API omits null properties, so an unacknowledged participant has no
  // acknowledgedAtUtc member on the wire. Normalize that valid representation
  // back to the explicit null used by the client model.
  acknowledgedAtUtc: z.string().nullable().default(null),
}).passthrough();

const patientSnapshotSchema = z.object({
  fullName: z.string().nullable().default(null),
  approximateAge: z.number().nullable().default(null),
  allergies: z.array(z.string()),
  conditions: z.array(z.string()),
  medications: z.array(z.string()),
  procedures: z.array(z.object({ name: z.string(), year: z.number().nullable().default(null) }).passthrough()),
  emergencyContact: z.object({ name: z.string(), relationship: z.string(), phoneNumber: z.string() }).nullable().default(null),
  capturedAtUtc: z.string(),
  source: z.literal('profile_snapshot'),
}).passthrough();

const observationSchema = z.object({
  id: z.string(), kind: z.string(), value: z.string(), source: z.string(), isConfirmed: z.boolean(), createdAtUtc: z.string(),
}).passthrough();

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
  patientSnapshot: patientSnapshotSchema.nullable().optional(),
  observations: z.array(observationSchema).optional(),
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
  timeline: z.array(z.object({ id: z.string(), sequence: z.number().optional(), type: z.string().optional(), message: z.string(), source: z.string().optional(), createdAtUtc: z.string() })).optional(),
  participants: z.array(participantSchema).optional(),
  locations: z.array(z.unknown()).optional(),
}).passthrough();

const backendProfileSchema = z.object({
  id: z.string(), fullName: z.string(), dateOfBirth: z.string().nullable(), bloodGroup: z.string().nullable(), preferredLanguage: z.string(), responseMode: z.string(),
  contacts: z.array(z.object({ id: z.string(), name: z.string(), relationship: z.string(), phoneNumber: z.string(), isVerified: z.boolean() }).passthrough()),
  allergies: z.array(z.object({ name: z.string() }).passthrough()), conditions: z.array(z.object({ name: z.string() }).passthrough()), medications: z.array(z.object({ name: z.string() }).passthrough()),
  procedures: z.array(z.object({ name: z.string(), year: z.number().nullable().optional() }).passthrough()),
  preferredHospital: z.object({ name: z.string(), phoneNumber: z.string().nullable().optional() }).nullable(),
  sharingPreference: z.object({ shareName: z.boolean(), shareApproximateAge: z.boolean(), shareAllergies: z.boolean(), shareConditions: z.boolean(), shareMedications: z.boolean(), shareEmergencyContact: z.boolean(), reviewedAtUtc: z.string().nullable().optional() }).nullable(),
  reviewedAtUtc: z.string().nullable(), insuranceDetails: z.string().nullable().optional(), doctorContact: z.string().nullable().optional(), allergyStatusCompleted: z.boolean().optional(), medicationStatusCompleted: z.boolean().optional(), locationPermissionReviewed: z.boolean().optional(),
}).passthrough();

const backendExtractionSchema = z.object({
  detectedLanguage: z.string(), languageConfidence: z.number(), incidentCategory: z.string(), patientRelationship: z.string(), observations: z.array(z.string()), reportedSymptomStartTime: z.string().nullable(), isConscious: z.string(), isBreathingNormally: z.string(), isHeavyBleedingReported: z.string(), locationDescription: z.string().nullable(), urgencyClassification: z.string(), criticalMissingQuestions: z.array(z.object({ id: z.string(), question: z.string(), answerType: z.string() }).passthrough()).max(3), handoverFacts: z.array(z.string()), uncertainties: z.array(z.string()), confidence: z.number(),
}).passthrough();

function mapProfile(payload: unknown): EmergencyProfile {
  const raw = backendProfileSchema.parse(payload);
  const sharing = raw.sharingPreference;
  const shareFields = [sharing?.shareName ? 'name' : null, sharing?.shareApproximateAge ? 'approximateAge' : null, sharing?.shareAllergies ? 'allergies' : null, sharing?.shareConditions ? 'conditions' : null, sharing?.shareMedications ? 'medicines' : null, sharing?.shareEmergencyContact ? 'emergencyContact' : null].filter((field): field is string => field !== null);
  const responseMode = raw.responseMode.toLowerCase();
  return { name: raw.fullName, dateOfBirth: raw.dateOfBirth ?? '', preferredLanguage: raw.preferredLanguage, responseMode: responseMode === 'audio' || responseMode === 'both' ? responseMode : 'text', bloodGroup: raw.bloodGroup ?? undefined, allergies: raw.allergies.map((item) => item.name), conditions: raw.conditions.map((item) => item.name), medicines: raw.medications.map((item) => item.name), procedures: raw.procedures.map((item) => `${item.name}${item.year ? `, ${item.year}` : ''}`), preferredHospital: raw.preferredHospital?.name ?? '', doctor: raw.doctorContact ?? '', insurance: raw.insuranceDetails ?? '', contacts: raw.contacts.map((contact) => ({ id: contact.id, name: contact.name, relationship: contact.relationship, phone: contact.phoneNumber, verified: contact.isVerified })), shareFields, reviewedAt: raw.reviewedAtUtc ?? '', allergyStatusCompleted: raw.allergyStatusCompleted ?? false, medicationStatusCompleted: raw.medicationStatusCompleted ?? false, sharingReviewed: raw.sharingPreference?.reviewedAtUtc != null, locationPermissionReviewed: raw.locationPermissionReviewed ?? false, qrGenerated: false };
}

function profileRequest(profile: EmergencyProfile, markReviewed = false): unknown {
  const procedurePattern = /^(.*?)(?:,\s*(\d{4}))?$/;
  return { fullName: profile.name, dateOfBirth: profile.dateOfBirth || null, bloodGroup: profile.bloodGroup || null, preferredLanguage: profile.preferredLanguage, responseMode: profile.responseMode, insuranceDetails: profile.insurance || null, doctorContact: profile.doctor || null, allergyStatusCompleted: profile.allergyStatusCompleted, medicationStatusCompleted: profile.medicationStatusCompleted, locationPermissionReviewed: profile.locationPermissionReviewed, reviewed: markReviewed, contacts: profile.contacts.map((contact) => ({ name: contact.name, relationship: contact.relationship, phoneNumber: contact.phone, isVerified: contact.verified })), allergies: profile.allergies.map((name) => ({ name })), conditions: profile.conditions.map((name) => ({ name })), medications: profile.medicines.map((name) => ({ name })), procedures: profile.procedures.map((value) => { const match = procedurePattern.exec(value); return { name: match?.[1]?.trim() || value, year: match?.[2] ? Number(match[2]) : null }; }), preferredHospital: profile.preferredHospital ? { name: profile.preferredHospital, phoneNumber: null } : null, sharing: { shareName: profile.shareFields.includes('name'), shareApproximateAge: profile.shareFields.includes('approximateAge'), shareAllergies: profile.shareFields.includes('allergies'), shareConditions: profile.shareFields.includes('conditions'), shareMedications: profile.shareFields.includes('medicines'), shareEmergencyContact: profile.shareFields.includes('emergencyContact'), reviewed: profile.sharingReviewed } };
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
  const normalizedExtraction = backendExtraction.success ? incidentExtractionSchema.safeParse({
    ...backendExtraction.data,
    incidentCategory: normalizeCategory(backendExtraction.data.incidentCategory),
    patientRelationship: relationship,
    isConscious: triState(backendExtraction.data.isConscious),
    isBreathingNormally: triState(backendExtraction.data.isBreathingNormally),
    isHeavyBleedingReported: triState(backendExtraction.data.isHeavyBleedingReported),
    urgencyClassification: ['emergency', 'urgent'].includes(backendExtraction.data.urgencyClassification.toLowerCase()) ? backendExtraction.data.urgencyClassification.toLowerCase() : 'unknown',
    criticalMissingQuestions: backendExtraction.data.criticalMissingQuestions.map((question) => ({ ...question, answerType: ['yes_no', 'single_choice', 'time', 'text'].includes(question.answerType.toLowerCase()) ? question.answerType.toLowerCase() : 'text' })),
  }) : null;
  const extraction = normalizedExtraction?.success ? normalizedExtraction.data : { ...emptySession.extraction, detectedLanguage: raw.originalLanguage ?? 'unknown', incidentCategory: normalizeCategory(raw.selectedCategory), patientRelationship: relationship, uncertainties: ['The incident interpretation was invalid and was not used. Review the preserved original report.'] };
  const latestLocation = [...(raw.locations ?? [])].sort((left, right) => {
    const timestamp = (value: unknown) => value && typeof value === 'object' && 'createdAtUtc' in value && typeof value.createdAtUtc === 'string' ? Date.parse(value.createdAtUtc) : 0;
    return timestamp(right) - timestamp(left);
  })[0];
  const candidateDescription = latestLocation && typeof latestLocation === 'object' && 'description' in latestLocation ? (latestLocation as { description?: unknown }).description : undefined;
  const location = typeof latestLocation === 'string'
    ? latestLocation
    : typeof candidateDescription === 'string'
      ? candidateDescription
      : extraction.locationDescription ?? '';

  const participantDetails = raw.participants ?? [];
  const participantOptions = participantDetails.map((participant) => ({ id: participant.id, label: participant.displayName }));
  const participantName = (id: string | null | undefined) => participantOptions.find((participant) => participant.id === id)?.label ?? '';
  const owner = participantDetails.find((participant) => participant.role === 'owner');
  const patientSnapshot = raw.patientSnapshot ?? null;
  const sharingFields = [
    patientSnapshot?.fullName ? 'name' : null,
    patientSnapshot?.approximateAge != null ? 'approximateAge' : null,
    patientSnapshot && patientSnapshot.allergies.length > 0 ? 'allergies' : null,
    patientSnapshot && patientSnapshot.conditions.length > 0 ? 'conditions' : null,
    patientSnapshot && patientSnapshot.medications.length > 0 ? 'medicines' : null,
    patientSnapshot?.emergencyContact ? 'emergencyContact' : null,
  ].filter((field): field is string => field !== null);
  const timelineSource = (source: string | undefined): EmergencySession['timeline'][number]['source'] =>
    source === 'user-reported' || source === 'profile' || source === 'ai-extracted' || source === 'confirmed' || source === 'system' ? source : 'unknown';
  return {
    id: raw.id,
    owner: owner?.displayName ?? (MOCK_MODE ? demoSession.owner : ''),
    patient: patientSnapshot?.fullName ?? (MOCK_MODE ? demoSession.patient : ''),
    relationship: extraction.patientRelationship,
    category: normalizeCategory(raw.selectedCategory),
    status: mapStatus(raw.status),
    createdAt: raw.createdAtUtc ?? new Date().toISOString(),
    updatedAt: raw.updatedAtUtc ?? new Date().toISOString(),
    location,
    originalInput: raw.originalInput ?? '',
    normalizedInput: raw.normalizedTranscript ?? '',
    interpretationUncertain: !normalizedExtraction?.success || (raw.interpretationUncertain ?? true),
    extraction,
    timeline: (raw.timeline ?? []).sort((a, b) => (a.sequence ?? 0) - (b.sequence ?? 0)).map((event) => ({ id: event.id, at: event.createdAtUtc, title: event.type?.replaceAll('-', ' ') ?? 'Update', detail: event.message, source: timelineSource(event.source) })),
    tasks: (raw.tasks ?? []).map((task) => ({ id: task.id, title: task.title, assignee: participantName(task.assignedParticipantId), assignedParticipantId: task.assignedParticipantId ?? undefined, status: mapTaskStatus(task.status), critical: task.isCritical ?? false, updatedAt: task.completedAtUtc ?? task.acceptedAtUtc ?? raw.updatedAtUtc ?? new Date().toISOString(), concurrencyToken: task.concurrencyToken ?? '' })),
    participants: participantOptions.map((participant) => participant.label),
    participantOptions,
    participantDetails,
    patientSnapshot,
    observations: raw.observations ?? [],
    protocolVersion: raw.protocol ? `${raw.protocol.id}/${raw.protocol.version}` : '',
    protocol: mapProtocol(raw.protocol),
    sharingFields: MOCK_MODE ? demoProfile.shareFields : sharingFields,
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
  updateProfile(profile: EmergencyProfile, options: { markReviewed?: boolean } = {}): Promise<EmergencyProfile> {
    return withMockFallback(async () => mapProfile(await request('/api/v1/profile', { method: 'PUT', body: JSON.stringify(profileRequest(profile, options.markReviewed === true)) })), profile);
  },
  requestContactVerification(contactId: string): Promise<ContactVerificationChallenge> {
    const fallback = { challenge: crypto.randomUUID(), expiresAtUtc: new Date(Date.now() + 5 * 60_000).toISOString(), status: 'mock-no-delivery', developmentCode: '123456' };
    return withMockFallback(async () => z.object({ challenge: z.string().min(1), expiresAtUtc: z.string(), status: z.string(), developmentCode: z.string().optional() }).parse(await request(`/api/v1/profile/contacts/${encodeURIComponent(contactId)}/verification`, { method: 'POST' })), fallback);
  },
  confirmContactVerification(contactId: string, challenge: string, code: string): Promise<void> {
    return withMockFallback(async () => { await request(`/api/v1/profile/contacts/${encodeURIComponent(contactId)}/verification/confirm`, { method: 'POST', body: JSON.stringify({ challenge, code }) }); }, undefined);
  },
  getReadiness(): Promise<ReadinessResult> {
    return withMockFallback(async () => {
      const result = z.object({ score: z.number().min(0).max(100), checks: z.array(z.object({ code: z.string(), label: z.string(), complete: z.boolean(), weight: z.number() })) }).parse(await request('/api/v1/readiness'));
      return { score: result.score, completed: result.checks.filter((check) => check.complete).map((check) => check.label), improvements: result.checks.filter((check) => !check.complete).map((check) => check.label) };
    }, demoReadiness);
  },
  createSession(category: EmergencySession['category'], relationship: EmergencySession['relationship'], useOwnerProfileForPatient = false): Promise<EmergencySession> {
    const includeOwnerProfile = relationship === 'self' || (relationship === 'family' && useOwnerProfileForPatient);
    const fallback = { ...demoSession, category, relationship, patient: includeOwnerProfile ? demoSession.patient : '', patientSnapshot: includeOwnerProfile ? demoSession.patientSnapshot : null, sharingFields: includeOwnerProfile ? demoSession.sharingFields : [], extraction: { ...demoSession.extraction, incidentCategory: category, patientRelationship: relationship } };
    return withMockFallback(async () => mapSession(await request('/api/v1/sessions', { method: 'POST', headers: { 'Idempotency-Key': crypto.randomUUID() }, body: JSON.stringify({ selectedCategory: categoryToWire(category), patientRelationship: relationship, typedLocation: null, countryCode: 'IN', ...(relationship === 'family' && useOwnerProfileForPatient ? { useOwnerProfileForPatient: true } : {}) }) })), fallback);
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
  getProtocolAudio(id: string): Promise<Blob> {
    return requestAudio(`/api/v1/sessions/${encodeURIComponent(id)}/protocol-audio`);
  },
  listSessions(): Promise<EmergencySession[]> {
    if (MOCK_MODE) return Promise.resolve([demoSession]);
    return request('/api/v1/sessions?limit=20').then((payload) => z.array(backendSessionSchema).parse(payload).map(mapSession));
  },
  submitIncident(id: string, input: string, location: string, skipAi = false, selectedCategory: EmergencySession['category'] = 'unknown'): Promise<EmergencySession> {
    const fallback = { ...demoSession, id, category: selectedCategory, originalInput: input || demoSession.originalInput, location: location || demoSession.location, extraction: { ...demoSession.extraction, incidentCategory: selectedCategory } };
    return withMockFallback(async () => {
      await request(`/api/v1/sessions/${encodeURIComponent(id)}/incident`, { method: 'POST', headers: { 'Idempotency-Key': crypto.randomUUID() }, body: JSON.stringify({ originalText: input, selectedLanguage: /[\u0900-\u097f]/u.test(input) ? 'hi' : null, fallbackCategory: null, skipAi }) });
      if (location) {
        const idempotencyKey = crypto.randomUUID();
        await request(`/api/v1/sessions/${encodeURIComponent(id)}/locations`, { method: 'POST', headers: { 'Idempotency-Key': idempotencyKey }, body: JSON.stringify({ latitude: null, longitude: null, description: location, consentProvided: true, idempotencyKey }) });
      }
      return mapSession(await request(`/api/v1/sessions/${encodeURIComponent(id)}`));
    }, fallback);
  },
  answerQuestions(id: string, answers: Record<string, string>): Promise<EmergencySession> {
    return withMockFallback(async () => {
      const boundedAnswers = Object.fromEntries(Object.entries(answers).slice(0, 3));
      await request(`/api/v1/sessions/${encodeURIComponent(id)}/answers`, { method: 'POST', headers: { 'Idempotency-Key': crypto.randomUUID() }, body: JSON.stringify({ answers: boundedAnswers }) });
      return mapSession(await request(`/api/v1/sessions/${encodeURIComponent(id)}`));
    }, demoSession);
  },
  generateSummary(id: string, kind: EmergencySummaryKind): Promise<EmergencySummaryResult> {
    const fallback = { id: crypto.randomUUID(), kind, content: `Summary type: ${kind} (server-generated from allowlisted fields)\nDemonstration guidance requiring clinical review before production use.\nNo external action is confirmed by this summary.`, language: 'en', protocolVersion: demoSession.protocolVersion || 'none', createdAtUtc: new Date().toISOString() };
    return withMockFallback(async () => {
      const parsed = z.object({ id: z.string(), kind: z.enum(['family', 'responder', 'hospital_handover']), content: z.string().min(1).max(100_000), language: z.string(), protocolVersion: z.string(), createdAtUtc: z.string() }).passthrough().parse(await request(`/api/v1/sessions/${encodeURIComponent(id)}/summaries/${kind}`, { method: 'POST' }));
      return { ...parsed, kind: parsed.kind === 'hospital_handover' ? 'hospital-handover' : parsed.kind };
    }, fallback);
  },
  uploadVoice(id: string, recording: Blob): Promise<EmergencySession> {
    if (MOCK_MODE) return Promise.resolve(demoSession);
    if (!recording.type.startsWith('audio/') || recording.size === 0 || recording.size > 5 * 1024 * 1024) {
      return Promise.reject(new ApiError('The recording format or size is not supported.', 400));
    }
    const form = new FormData();
    form.append('audio', recording, 'incident-recording.webm');
    return request(`/api/v1/sessions/${encodeURIComponent(id)}/voice`, {
      method: 'POST',
      headers: { Accept: 'application/json', 'Idempotency-Key': crypto.randomUUID() },
      body: form,
    }).then(mapSession).catch((error: unknown) => {
      if (error instanceof ApiError) throw new ApiError('The recording could not be uploaded. Type what happened instead.', error.status, error.correlationId);
      throw error;
    });
  },
  updateLocation(id: string, description: string): Promise<EmergencySession> {
    const fallback = { ...demoSession, id, location: description };
    return withMockFallback(async () => {
      const idempotencyKey = crypto.randomUUID();
      return mapSession(await request(`/api/v1/sessions/${encodeURIComponent(id)}/locations`, { method: 'POST', headers: { 'Idempotency-Key': idempotencyKey }, body: JSON.stringify({ latitude: null, longitude: null, description, consentProvided: true, idempotencyKey }) }));
    }, fallback);
  },
  updateLocationCoordinates(id: string, latitude: number, longitude: number, typedDescription = ''): Promise<EmergencySession> {
    if (!Number.isFinite(latitude) || latitude < -90 || latitude > 90 || !Number.isFinite(longitude) || longitude < -180 || longitude > 180) return Promise.reject(new ApiError('Map-pin coordinates are outside the allowed range.', 400));
    const description = typedDescription.trim().slice(0, 300) || `Map pin: ${latitude.toFixed(5)}, ${longitude.toFixed(5)}`;
    const fallback = { ...demoSession, id, location: description };
    return withMockFallback(async () => {
      const idempotencyKey = crypto.randomUUID();
      return mapSession(await request(`/api/v1/sessions/${encodeURIComponent(id)}/locations`, { method: 'POST', headers: { 'Idempotency-Key': idempotencyKey }, body: JSON.stringify({ latitude, longitude, description, consentProvided: true, idempotencyKey }) }));
    }, fallback);
  },
  addTimeline(id: string, type: 'call-initiated' | 'call-connected' | 'patient-departed' | 'patient-arrived' | 'observation' | 'note', message: string, stableIdempotencyKey?: string): Promise<void> {
    return withMockFallback(async () => {
      const idempotencyKey = stableIdempotencyKey ?? crypto.randomUUID();
      await request(`/api/v1/sessions/${encodeURIComponent(id)}/timeline`, {
        method: 'POST',
        keepalive: type === 'call-initiated',
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
    const fallback = { token: DEMO_SHARE_TOKEN, tokenId: 'demo-token-id', expiresAtUtc: new Date(Date.now() + 30 * 60_000).toISOString(), path: `/share#${DEMO_SHARE_TOKEN}` };
    return withMockFallback(async () => {
      const result = z.object({ id: z.string(), token: z.string().min(20), expiresAtUtc: z.string(), path: z.string() }).parse(await request(`/api/v1/sessions/${encodeURIComponent(id)}/share-tokens`, { method: 'POST', headers: { 'Idempotency-Key': crypto.randomUUID() }, body: JSON.stringify({ lifetimeMinutes: 30 }) }));
      if (result.path !== `/share#${result.token}`) throw new ApiError('The server returned an unsafe emergency sharing path.', 500);
      return { token: result.token, tokenId: result.id, expiresAtUtc: result.expiresAtUtc, path: result.path };
    }, fallback);
  },
  revokeShareToken(id: string, tokenId: string): Promise<void> {
    return withMockFallback(async () => { await request(`/api/v1/sessions/${encodeURIComponent(id)}/share-tokens/${encodeURIComponent(tokenId)}`, { method: 'DELETE' }); }, undefined);
  },
  inviteParticipant(id: string, displayName: string, role: Exclude<ParticipantRole, 'owner'>): Promise<ParticipantInviteResult> {
    if (MOCK_MODE) return Promise.resolve({ participantId: crypto.randomUUID(), token: 'demo-participant-invitation-token', expiresAtUtc: new Date(Date.now() + 30 * 60_000).toISOString(), path: `/emergency/${encodeURIComponent(id)}/join#demo-participant-invitation-token` });
    return request(`/api/v1/sessions/${encodeURIComponent(id)}/participants/invitations`, { method: 'POST', headers: { 'Idempotency-Key': crypto.randomUUID() }, body: JSON.stringify({ displayName, role, lifetimeMinutes: 30 }) }).then((payload) => {
      const result = z.object({ participantId: z.string(), token: z.string(), expiresAtUtc: z.string(), path: z.string() }).parse(payload);
      if (result.path !== `/emergency/${id}/join#${result.token}`) throw new ApiError('The server returned an unsafe participant invitation path.', 500);
      return result;
    });
  },
  joinParticipant(id: string, token: string): Promise<EmergencyParticipant> {
    if (MOCK_MODE) return Promise.resolve({ id: 'demo-joined-participant', displayName: 'Demo participant', role: 'family', acknowledgedAtUtc: null });
    return request(`/api/v1/sessions/${encodeURIComponent(id)}/participants/join`, { method: 'POST', body: JSON.stringify({ token }) }).then((payload) => participantSchema.parse(payload));
  },
  acknowledgeParticipant(id: string, participantId: string): Promise<EmergencyParticipant> {
    if (MOCK_MODE) return Promise.resolve({ id: participantId, displayName: 'Demo participant', role: 'family', acknowledgedAtUtc: new Date().toISOString() });
    return request(`/api/v1/sessions/${encodeURIComponent(id)}/participants/${encodeURIComponent(participantId)}/acknowledge`, { method: 'POST' }).then((payload) => participantSchema.parse(payload));
  },
  getBystander(token: string): Promise<BystanderView> {
    if (MOCK_MODE) {
      if (token !== DEMO_SHARE_TOKEN) return Promise.reject(new ApiError('This emergency link is invalid, expired, or revoked.', 410));
      return Promise.resolve({ sessionId: demoSession.id, patientName: demoProfile.name, approximateAge: 64, category: demoSession.category, location: demoSession.location, allergies: demoProfile.allergies, conditions: demoProfile.conditions, medicines: demoProfile.medicines, emergencyContact: { name: demoProfile.contacts[0]?.name ?? '', relationship: demoProfile.contacts[0]?.relationship ?? '', phone: demoProfile.contacts[0]?.phone ?? '' }, emergencyNumber: '112', protocol: null, expiresAtUtc: new Date(Date.now() + 30 * 60_000).toISOString() });
    }
    return request('/api/v1/bystander', { headers: { 'X-Emergency-Share-Token': token } }).then((payload) => {
      const result = z.object({ sessionId: z.string(), patientName: z.string().nullable().default(null), approximateAge: z.number().nullable().default(null), category: z.string(), location: z.string().nullable().default(null), allergies: z.array(z.string()), conditions: z.array(z.string()), medicines: z.array(z.string()), emergencyContact: z.object({ name: z.string(), relationship: z.string(), phone: z.string() }).nullable().default(null), expiresAtUtc: z.string(), emergencyNumber: z.string(), protocol: backendSessionSchema.shape.protocol.nullable().default(null) }).parse(payload);
      return { ...result, category: normalizeCategory(result.category), location: result.location ?? '', protocol: mapProtocol(result.protocol) ?? null };
    });
  },
  reportBystanderObservation(token: string, observation: { conscious?: string; breathingNormally?: string; severeBleeding?: string }): Promise<void> {
    return withMockFallback(async () => { await request('/api/v1/bystander/observations', { method: 'POST', headers: { 'X-Emergency-Share-Token': token, 'Idempotency-Key': crypto.randomUUID() }, body: JSON.stringify(observation) }); }, undefined);
  },
  reportBystanderLocation(token: string, latitude: number, longitude: number): Promise<void> {
    return withMockFallback(async () => { await request('/api/v1/bystander/location', { method: 'POST', headers: { 'X-Emergency-Share-Token': token, 'Idempotency-Key': crypto.randomUUID() }, body: JSON.stringify({ latitude, longitude, consentConfirmed: true }) }); }, undefined);
  },
  closeSession(id: string, concurrencyToken: string): Promise<void> {
    return withMockFallback(async () => { await request(`/api/v1/sessions/${encodeURIComponent(id)}/close`, { method: 'POST', headers: { 'Idempotency-Key': crypto.randomUUID() }, body: JSON.stringify({ concurrencyToken }) }); }, undefined);
  },
};

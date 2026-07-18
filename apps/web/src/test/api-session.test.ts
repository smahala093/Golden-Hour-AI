import { afterEach, describe, expect, it, vi } from 'vitest';
import { api } from '../api';
import { demoProfile } from '../demoData';

function requestJson(init: RequestInit): unknown {
  if (typeof init.body !== 'string') throw new Error('Expected a JSON request body.');
  return JSON.parse(init.body) as unknown;
}

afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe('authoritative session API mapping', () => {
  it('accepts omitted null participant fields from a successful create response', async () => {
    const response = {
      id: 'a5e94753-b1c2-45be-a54f-776fbde2667a',
      status: 'active',
      selectedCategory: 'chest_pain',
      patientRelationship: 'family',
      emergencyNumber: '112',
      createdAtUtc: '2026-07-18T10:00:00.000Z',
      updatedAtUtc: '2026-07-18T10:00:00.000Z',
      interpretationUncertain: true,
      observations: [],
      tasks: [],
      timeline: [{
        id: 'c60607e0-dddb-4b51-b234-faf244f158da',
        sequence: 1,
        type: 'session-created',
        message: 'Emergency session started.',
        createdAtUtc: '2026-07-18T10:00:00.000Z',
      }],
      participants: [{
        id: 'db2aed86-a153-4d3f-8d94-b31d22fdd9a1',
        displayName: 'Session owner',
        role: 'owner',
      }],
      locations: [],
      concurrencyToken: '6e143852-398f-40d1-ab87-cc6b6a361f04',
    };
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify(response), {
      status: 201,
      headers: { 'content-type': 'application/json' },
    })));

    const session = await api.createSession('chest-pain', 'family');

    expect(session.id).toBe(response.id);
    expect(session.category).toBe('chest-pain');
    expect(session.participantDetails).toEqual([{
      id: response.participants[0]!.id,
      displayName: 'Session owner',
      role: 'owner',
      acknowledgedAtUtc: null,
    }]);
  });

  it('fails closed to the preserved report and manual category when AI facts cross the backend boundary', async () => {
    const response = {
      id: 'fd03403a-7da1-4e83-a10e-591789f5605b',
      status: 'active',
      selectedCategory: 'chest_pain',
      patientRelationship: 'family',
      originalInput: 'Preserve this exact user report.',
      originalLanguage: 'English',
      interpretationUncertain: false,
      incidentFacts: {
        detectedLanguage: 'English', languageConfidence: 0.9, incidentCategory: 'road_accident', patientRelationship: 'family',
        observations: Array.from({ length: 13 }, (_, index) => `Observation ${index + 1}`),
        reportedSymptomStartTime: 'not-an-rfc3339-time', isConscious: 'yes', isBreathingNormally: 'unknown', isHeavyBleedingReported: 'no',
        locationDescription: 'Test landmark', urgencyClassification: 'emergency', criticalMissingQuestions: [], handoverFacts: [], uncertainties: [], confidence: 0.9,
      },
    };
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify(response), { status: 200, headers: { 'content-type': 'application/json' } })));

    const session = await api.getSession(response.id);

    expect(session.originalInput).toBe('Preserve this exact user report.');
    expect(session.category).toBe('chest-pain');
    expect(session.extraction.incidentCategory).toBe('chest-pain');
    expect(session.extraction.observations).toEqual([]);
    expect(session.extraction.uncertainties).toContain('The incident interpretation was invalid and was not used. Review the preserved original report.');
    expect(session.interpretationUncertain).toBe(true);
  });

  it('submits the bounded critical-answer dictionary atomically', async () => {
    const sessionId = 'a5e94753-b1c2-45be-a54f-776fbde2667a';
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response(null, { status: 204 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ id: sessionId }), { status: 200, headers: { 'content-type': 'application/json' } }));
    vi.stubGlobal('fetch', fetchMock);

    await api.answerQuestions(sessionId, { conscious: 'yes', breathing: 'no', 'heavy-bleeding': 'unknown', ignored: 'yes' });

    expect(fetchMock).toHaveBeenCalledTimes(2);
    const request = fetchMock.mock.calls[0]?.[1] as RequestInit;
    expect(requestJson(request)).toEqual({ answers: { conscious: 'yes', breathing: 'no', 'heavy-bleeding': 'unknown' } });
    const headers = request.headers as Record<string, string>;
    expect(headers['Idempotency-Key']).toMatch(/^[0-9a-f-]{36}$/i);
  });

  it('saves bounded map-pin coordinates while retaining the typed landmark', async () => {
    const sessionId = 'a5e94753-b1c2-45be-a54f-776fbde2667a';
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({ id: sessionId, locations: [{ description: 'Typed Jaipur landmark', createdAtUtc: '2026-07-18T10:00:00.000Z' }] }), { status: 200, headers: { 'content-type': 'application/json' } }));
    vi.stubGlobal('fetch', fetchMock);

    const session = await api.updateLocationCoordinates(sessionId, 26.9124, 75.7873, '  Typed Jaipur landmark  ');

    const request = fetchMock.mock.calls[0]?.[1] as RequestInit;
    expect(requestJson(request)).toEqual(expect.objectContaining({ latitude: 26.9124, longitude: 75.7873, description: 'Typed Jaipur landmark', consentProvided: true }));
    expect(session.location).toBe('Typed Jaipur landmark');
    await expect(api.updateLocationCoordinates(sessionId, 91, 75.7873)).rejects.toMatchObject({ status: 400 });
  });

  it('normalizes the server hospital summary kind to the route kind', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ id: crypto.randomUUID(), kind: 'hospital_handover', content: 'Authoritative server handover', language: 'en', protocolVersion: 'reviewed-v1', createdAtUtc: '2026-07-18T10:00:00.000Z' }), { status: 200, headers: { 'content-type': 'application/json' } })));

    const summary = await api.generateSummary('a5e94753-b1c2-45be-a54f-776fbde2667a', 'hospital-handover');

    expect(summary.kind).toBe('hospital-handover');
    expect(summary.content).toBe('Authoritative server handover');
  });

  it('sends owner-profile consent only for an explicit family-patient choice', async () => {
    const fetchMock = vi.fn().mockImplementation(() => Promise.resolve(new Response(JSON.stringify({ id: crypto.randomUUID() }), { status: 201, headers: { 'content-type': 'application/json' } })));
    vi.stubGlobal('fetch', fetchMock);

    await api.createSession('chest-pain', 'family', true);
    await api.createSession('chest-pain', 'family', false);

    const consented = requestJson(fetchMock.mock.calls[0]?.[1] as RequestInit) as Record<string, unknown>;
    const notConsented = requestJson(fetchMock.mock.calls[1]?.[1] as RequestInit) as Record<string, unknown>;
    expect(consented.useOwnerProfileForPatient).toBe(true);
    expect(notConsented).not.toHaveProperty('useOwnerProfileForPatient');
  });

  it('normalizes omitted nullable fields in the bystander projection', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({
      sessionId: 'a5e94753-b1c2-45be-a54f-776fbde2667a',
      category: 'chest_pain',
      allergies: [], conditions: [], medicines: [],
      expiresAtUtc: '2026-07-18T10:30:00.000Z', emergencyNumber: '112',
    }), { status: 200, headers: { 'content-type': 'application/json' } })));

    const projection = await api.getBystander('safe-test-token');

    expect(projection).toEqual(expect.objectContaining({ patientName: null, approximateAge: null, location: '', emergencyContact: null, protocol: null }));
  });

  it('does not refresh the full-profile review date unless explicitly requested', async () => {
    const backendProfile = {
      id: '74c41e38-ec14-4639-935e-261d71b22113', fullName: demoProfile.name, dateOfBirth: demoProfile.dateOfBirth,
      bloodGroup: demoProfile.bloodGroup, preferredLanguage: demoProfile.preferredLanguage, responseMode: demoProfile.responseMode,
      contacts: [], allergies: [], conditions: [], medications: [], procedures: [], preferredHospital: null,
      sharingPreference: null, reviewedAtUtc: demoProfile.reviewedAt, allergyStatusCompleted: true, medicationStatusCompleted: true, locationPermissionReviewed: true,
    };
    const fetchMock = vi.fn().mockImplementation(() => Promise.resolve(new Response(JSON.stringify(backendProfile), { status: 200, headers: { 'content-type': 'application/json' } })));
    vi.stubGlobal('fetch', fetchMock);

    await api.updateProfile(demoProfile);
    await api.updateProfile(demoProfile, { markReviewed: true });

    const ordinaryBody = requestJson(fetchMock.mock.calls[0]?.[1] as RequestInit) as { reviewed?: unknown };
    const reviewedBody = requestJson(fetchMock.mock.calls[1]?.[1] as RequestInit) as { reviewed?: unknown };
    expect(ordinaryBody.reviewed).toBe(false);
    expect(reviewedBody.reviewed).toBe(true);
  });

  it('emits one auth-expired signal after refresh failures', async () => {
    const unauthorized = () => new Response(JSON.stringify({ title: 'Unauthorized' }), { status: 401, headers: { 'content-type': 'application/problem+json' } });
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response(null, { status: 204 }))
      .mockResolvedValueOnce(unauthorized()).mockResolvedValueOnce(unauthorized())
      .mockResolvedValueOnce(unauthorized()).mockResolvedValueOnce(unauthorized());
    vi.stubGlobal('fetch', fetchMock);
    await api.login('demo@example.test', 'safe-test-password');
    const expired = vi.fn();
    window.addEventListener('gh-auth-expired', expired);
    try {
      await expect(api.getSession('first')).rejects.toBeDefined();
      await expect(api.getSession('second')).rejects.toBeDefined();
      expect(expired).toHaveBeenCalledTimes(1);
    } finally {
      window.removeEventListener('gh-auth-expired', expired);
    }
  });
});

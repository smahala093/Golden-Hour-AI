import type { EmergencyProfile, EmergencySession, IncidentExtraction, Preferences, ReadinessResult } from './types';

export const DEMO_SESSION_ID = 'GH-260715-JPR4';
export const DEMO_SHARE_TOKEN = 'demo-safe-share';

export const hindiDemoInput =
  'मेरे पिताजी को अचानक सीने में दर्द और बहुत पसीना आ रहा है। उन्हें बोलने में भी परेशानी हो रही है।';

export const demoProfile: EmergencyProfile = {
  name: 'Raj Kumar',
  dateOfBirth: '1962-03-12',
  preferredLanguage: 'hi',
  responseMode: 'both',
  bloodGroup: 'B+',
  allergies: ['Penicillin (self-reported)'],
  conditions: ['Hypertension (self-reported)', 'Type 2 diabetes (self-reported)'],
  medicines: ['Metformin — demonstration profile entry'],
  procedures: ['Angioplasty, 2024 (self-reported)'],
  preferredHospital: 'Suryodaya Community Hospital (fictional)',
  doctor: 'Dr. A. Mehta · demo contact',
  insurance: 'Demo policy — hidden from bystanders',
  contacts: [
    { id: 'contact-1', name: 'Asha Kumar', relationship: 'Daughter', phone: '+91 ••••• 41012', verified: true },
    { id: 'contact-2', name: 'Vikram Kumar', relationship: 'Son', phone: '+91 ••••• 79320', verified: false },
  ],
  shareFields: ['name', 'approximateAge', 'allergies', 'conditions', 'medicines', 'emergencyContact'],
  reviewedAt: '2026-07-05T09:30:00.000Z',
  locationPermissionReviewed: true,
  qrGenerated: true,
};

export const demoExtraction: IncidentExtraction = {
  detectedLanguage: 'Hindi',
  languageConfidence: 0.98,
  incidentCategory: 'chest-pain',
  patientRelationship: 'family',
  observations: ['Sudden chest pain', 'Heavy sweating', 'Difficulty speaking'],
  reportedSymptomStartTime: '2026-07-15T17:17:00.000Z',
  isConscious: 'yes',
  isBreathingNormally: 'unknown',
  isHeavyBleedingReported: 'no',
  locationDescription: 'Near Civil Lines, Jaipur (fictional demo location)',
  urgencyClassification: 'emergency',
  criticalMissingQuestions: [
    { id: 'breathing', question: 'Is he breathing normally?', answerType: 'yes_no' },
    { id: 'conscious', question: 'Is he awake and responding?', answerType: 'yes_no' },
  ],
  handoverFacts: ['Reported sudden chest pain', 'Heavy sweating reported', 'Speaking is difficult'],
  uncertainties: ['Breathing status has not yet been confirmed'],
  confidence: 0.86,
};

export const demoSession: EmergencySession = {
  id: DEMO_SESSION_ID,
  owner: 'Asha Kumar',
  patient: 'Raj Kumar',
  relationship: 'family',
  category: 'chest-pain',
  status: 'active',
  createdAt: '2026-07-15T17:18:00.000Z',
  updatedAt: '2026-07-15T17:21:00.000Z',
  location: 'Near Civil Lines, Jaipur (fictional demo location)',
  originalInput: hindiDemoInput,
  normalizedInput: 'My father suddenly has chest pain, heavy sweating, and difficulty speaking.',
  extraction: demoExtraction,
  participants: ['Asha · with patient', 'Vikram · connected remotely'],
  protocolVersion: 'chest-pain/demo-2026.07',
  sharingFields: demoProfile.shareFields,
  tasks: [
    { id: 'task-call', title: 'Call emergency services', assignee: 'Asha', assignedParticipantId: 'participant-asha', status: 'accepted', critical: true, updatedAt: '2026-07-15T17:18:20.000Z', concurrencyToken: 'demo-task-call-v1' },
    { id: 'task-door', title: 'Unlock the gate and guide responders', assignee: 'Vikram', assignedParticipantId: 'participant-vikram', status: 'open', critical: false, updatedAt: '2026-07-15T17:19:00.000Z', concurrencyToken: 'demo-task-door-v1' },
    { id: 'task-records', title: 'Bring identification and medical records', assignee: 'Vikram', assignedParticipantId: 'participant-vikram', status: 'completed', critical: false, updatedAt: '2026-07-15T17:20:00.000Z', concurrencyToken: 'demo-task-records-v1' },
  ],
  timeline: [
    { id: 'event-1', at: '2026-07-15T17:18:00.000Z', title: 'Emergency started', detail: 'Asha reported an emergency for a family member.', source: 'confirmed' },
    { id: 'event-2', at: '2026-07-15T17:18:12.000Z', title: 'Situation captured', detail: 'Original Hindi description preserved.', source: 'user-reported' },
    { id: 'event-3', at: '2026-07-15T17:18:20.000Z', title: 'Call marked initiated', detail: 'The user opened the phone dialler. Connection is not confirmed.', source: 'confirmed' },
    { id: 'event-4', at: '2026-07-15T17:19:00.000Z', title: 'Chest-pain protocol selected', detail: 'Static demonstration protocol demo-2026.07.', source: 'ai-extracted' },
    { id: 'event-5', at: '2026-07-15T17:20:00.000Z', title: 'Family joined', detail: 'Vikram acknowledged the request.', source: 'confirmed' },
  ],
  participantOptions: [{ id: 'participant-asha', label: 'Asha · with patient' }, { id: 'participant-vikram', label: 'Vikram · connected remotely' }],
  emergencyNumber: '112',
  concurrencyToken: 'demo-session-v1',
};

export const demoReadiness: ReadinessResult = {
  score: 78,
  completed: ['Two emergency contacts added', 'A verified contact is available', 'Allergy status completed', 'Preferred language selected', 'Emergency sharing reviewed'],
  improvements: ['Review the profile again this month', 'Confirm the second contact', 'Review location permission'],
};

export const defaultPreferences: Preferences = {
  theme: 'system',
  simpleMode: false,
  reducedMotion: false,
  highContrast: false,
  largeText: false,
  language: 'en',
  offlineCardEnabled: false,
};

export const emptyProfile: EmergencyProfile = {
  name: '', dateOfBirth: '', preferredLanguage: 'en', responseMode: 'text', allergies: [], conditions: [], medicines: [], procedures: [], preferredHospital: '', doctor: '', insurance: '', contacts: [], shareFields: [], reviewedAt: new Date(0).toISOString(), locationPermissionReviewed: false, qrGenerated: false,
};

export const emptySession: EmergencySession = {
  id: '', owner: '', patient: '', relationship: 'unknown', category: 'unknown', status: 'active', createdAt: new Date(0).toISOString(), updatedAt: new Date(0).toISOString(), location: '', originalInput: '', normalizedInput: '',
  extraction: { detectedLanguage: 'unknown', languageConfidence: 0, incidentCategory: 'unknown', patientRelationship: 'unknown', observations: [], reportedSymptomStartTime: null, isConscious: 'unknown', isBreathingNormally: 'unknown', isHeavyBleedingReported: 'unknown', locationDescription: null, urgencyClassification: 'unknown', criticalMissingQuestions: [], handoverFacts: [], uncertainties: ['No incident interpretation is available.'], confidence: 0 },
  timeline: [], tasks: [], participants: [], participantOptions: [], protocolVersion: '', sharingFields: [], emergencyNumber: '112', concurrencyToken: '',
};

export const categoryLabels: Record<EmergencySession['category'], string> = {
  'chest-pain': 'Chest pain',
  'breathing-difficulty': 'Breathing difficulty',
  'fall-injury': 'Fall or injury',
  unconscious: 'Unconscious person',
  seizure: 'Seizure',
  'heavy-bleeding': 'Heavy bleeding',
  'road-accident': 'Road accident',
  'allergic-reaction': 'Allergic reaction',
  'child-emergency': 'Child emergency',
  unknown: 'Other or unknown',
};

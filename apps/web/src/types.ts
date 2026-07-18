import { z } from 'zod';

export const incidentCategories = [
  'chest-pain',
  'breathing-difficulty',
  'fall-injury',
  'unconscious',
  'seizure',
  'heavy-bleeding',
  'road-accident',
  'allergic-reaction',
  'child-emergency',
  'unknown',
] as const;

export type IncidentCategory = (typeof incidentCategories)[number];
export type PatientRelationship = 'self' | 'family' | 'bystander' | 'unknown';
export type TriState = 'yes' | 'no' | 'unknown';
export type SourceKind = 'user-reported' | 'profile' | 'ai-extracted' | 'confirmed' | 'system' | 'unknown';
export type TaskStatus = 'open' | 'accepted' | 'declined' | 'completed';
export type SessionStatus = 'active' | 'departed' | 'at-hospital' | 'closed';
export type ParticipantRole = 'owner' | 'family' | 'bystander' | 'caregiver';

const controlCharacterPattern = /\p{Cc}/u;
const boundedIncidentText = (maximum: number) => z.string().min(1).max(maximum).refine((value) => value.trim().length > 0 && !controlCharacterPattern.test(value), 'Text must be nonblank and contain no control characters.');

const criticalQuestionSchema = z.object({
  id: z.string().min(1).max(80).regex(/^[a-z0-9-]+$/),
  question: boundedIncidentText(240),
  answerType: z.enum(['yes_no', 'single_choice', 'time', 'text']),
}).strict().superRefine((question, context) => {
  const allowed = question.id === 'conscious'
    ? question.answerType === 'yes_no' && question.question === 'Is the person conscious?'
    : question.id === 'breathing' || question.id === 'breathing-normally'
      ? question.answerType === 'yes_no' && question.question === 'Is the person breathing normally?'
      : question.id === 'heavy-bleeding'
        ? question.answerType === 'yes_no' && question.question === 'Is heavy bleeding visible?'
        : question.id === 'confirm-facts'
          ? question.answerType === 'yes_no' && question.question === 'Do the extracted facts match what you reported?'
          : question.id === 'symptom-start-time'
            ? (question.answerType === 'time' || question.answerType === 'text') && question.question === 'When did the reported symptoms start?'
            : false;
  if (!allowed) context.addIssue({ code: 'custom', message: 'Question must match the reviewed server allowlist.' });
});

export const incidentExtractionSchema = z.object({
  detectedLanguage: boundedIncidentText(35).refine((value) => value.length >= 2, 'Language must have at least two characters.'),
  languageConfidence: z.number().min(0).max(1),
  incidentCategory: z.enum(incidentCategories),
  patientRelationship: z.enum(['self', 'family', 'bystander', 'unknown']),
  observations: z.array(boundedIncidentText(240)).max(12),
  reportedSymptomStartTime: boundedIncidentText(100).nullable(),
  isConscious: z.enum(['yes', 'no', 'unknown']),
  isBreathingNormally: z.enum(['yes', 'no', 'unknown']),
  isHeavyBleedingReported: z.enum(['yes', 'no', 'unknown']),
  locationDescription: boundedIncidentText(300).nullable(),
  urgencyClassification: z.enum(['emergency', 'urgent', 'unknown']),
  criticalMissingQuestions: z.array(criticalQuestionSchema).max(3).superRefine((questions, context) => {
    const ids = new Set<string>();
    questions.forEach((question, index) => {
      if (ids.has(question.id)) context.addIssue({ code: 'custom', path: [index, 'id'], message: 'Question IDs must be unique.' });
      ids.add(question.id);
    });
  }),
  handoverFacts: z.array(boundedIncidentText(240)).max(16),
  uncertainties: z.array(boundedIncidentText(240)).max(8),
  confidence: z.number().min(0).max(1),
}).strict();

export type IncidentExtraction = z.infer<typeof incidentExtractionSchema>;

export interface EmergencyContact {
  id: string;
  name: string;
  relationship: string;
  phone: string;
  verified: boolean;
}

export interface EmergencyProfile {
  name: string;
  dateOfBirth: string;
  preferredLanguage: string;
  responseMode: 'text' | 'audio' | 'both';
  bloodGroup?: string;
  allergies: string[];
  conditions: string[];
  medicines: string[];
  procedures: string[];
  preferredHospital: string;
  doctor: string;
  insurance: string;
  contacts: EmergencyContact[];
  shareFields: string[];
  reviewedAt: string;
  allergyStatusCompleted: boolean;
  medicationStatusCompleted: boolean;
  sharingReviewed: boolean;
  locationPermissionReviewed: boolean;
  qrGenerated: boolean;
}

export interface TimelineEvent {
  id: string;
  at: string;
  title: string;
  detail: string;
  source: SourceKind;
  pending?: boolean;
}

export interface EmergencyTask {
  id: string;
  title: string;
  assignee: string;
  status: TaskStatus;
  critical: boolean;
  updatedAt: string;
  assignedParticipantId?: string;
  concurrencyToken: string;
}

export interface EmergencyParticipant {
  id: string;
  displayName: string;
  role: ParticipantRole;
  acknowledgedAtUtc: string | null;
}

export interface SessionObservation {
  id: string;
  kind: string;
  value: string;
  source: string;
  isConfirmed: boolean;
  createdAtUtc: string;
}

export interface PatientSnapshot {
  fullName: string | null;
  approximateAge: number | null;
  allergies: string[];
  conditions: string[];
  medications: string[];
  procedures: { name: string; year: number | null }[];
  emergencyContact: { name: string; relationship: string; phoneNumber: string } | null;
  capturedAtUtc: string;
  source: 'profile_snapshot';
}

export interface EmergencySession {
  id: string;
  owner: string;
  patient: string;
  relationship: PatientRelationship;
  category: IncidentCategory;
  status: SessionStatus;
  createdAt: string;
  updatedAt: string;
  location: string;
  originalInput: string;
  normalizedInput: string;
  interpretationUncertain: boolean;
  extraction: IncidentExtraction;
  timeline: TimelineEvent[];
  tasks: EmergencyTask[];
  participants: string[];
  participantOptions?: { id: string; label: string }[];
  participantDetails: EmergencyParticipant[];
  patientSnapshot: PatientSnapshot | null;
  observations: SessionObservation[];
  protocolVersion: string;
  protocol?: EmergencyProtocol;
  sharingFields: string[];
  emergencyNumber: string;
  concurrencyToken: string;
}

export interface Preferences {
  theme: 'light' | 'dark' | 'system';
  simpleMode: boolean;
  reducedMotion: boolean;
  highContrast: boolean;
  largeText: boolean;
  language: string;
  offlineCardEnabled: boolean;
}

export interface DraftEmergency {
  relationship: PatientRelationship;
  category: IncidentCategory;
  useOwnerProfileForPatient: boolean;
  input: string;
  location: string;
  answers: Record<string, string>;
}

export interface ProtocolStep {
  id: string;
  title: string;
  detail: string;
}

export interface EmergencyProtocol {
  id: string;
  title: string;
  version: string;
  reviewStatus: string;
  country: string;
  emergencyCallInstruction: string;
  doActions: ProtocolStep[];
  doNotActions: string[];
  escalationRule: string;
  source: string;
  disclaimer: string;
}

export interface ReadinessResult {
  score: number;
  completed: string[];
  improvements: string[];
}

export type ConnectionState = 'connecting' | 'connected' | 'polling' | 'offline';

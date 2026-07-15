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
export type SourceKind = 'user-reported' | 'profile' | 'ai-extracted' | 'confirmed' | 'unknown';
export type TaskStatus = 'open' | 'accepted' | 'declined' | 'completed';
export type SessionStatus = 'active' | 'departed' | 'at-hospital' | 'closed';

export const incidentExtractionSchema = z.object({
  detectedLanguage: z.string().min(2).max(35),
  languageConfidence: z.number().min(0).max(1),
  incidentCategory: z.enum(incidentCategories),
  patientRelationship: z.enum(['self', 'family', 'bystander', 'unknown']),
  observations: z.array(z.string().min(1).max(240)).max(12),
  reportedSymptomStartTime: z.string().datetime().nullable(),
  isConscious: z.enum(['yes', 'no', 'unknown']),
  isBreathingNormally: z.enum(['yes', 'no', 'unknown']),
  isHeavyBleedingReported: z.enum(['yes', 'no', 'unknown']),
  locationDescription: z.string().max(300).nullable(),
  urgencyClassification: z.enum(['emergency', 'urgent', 'unknown']),
  criticalMissingQuestions: z
    .array(
      z.object({
        id: z.string().min(1).max(80),
        question: z.string().min(1).max(240),
        answerType: z.enum(['yes_no', 'single_choice', 'time', 'text']),
      }),
    )
    .max(3),
  handoverFacts: z.array(z.string().min(1).max(240)).max(16),
  uncertainties: z.array(z.string().min(1).max(240)).max(8),
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
  extraction: IncidentExtraction;
  timeline: TimelineEvent[];
  tasks: EmergencyTask[];
  participants: string[];
  participantOptions?: { id: string; label: string }[];
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
  input: string;
  location: string;
  answers: Record<string, TriState>;
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

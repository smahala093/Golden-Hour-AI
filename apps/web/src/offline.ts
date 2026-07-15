import type { EmergencyProfile } from './types';

const QUEUE_KEY = 'gh-noncritical-queue-v1';
const CARD_KEY = 'gh-minimal-card-v1';

export interface QueuedTimelineUpdate {
  idempotencyKey: string;
  sessionId: string;
  kind: 'note' | 'observation';
  eventCode: 'noncritical-check-recorded';
  message: string;
  createdAtUtc: string;
}

export interface MinimalOfflineCard {
  version: 1;
  savedAtUtc: string;
  name: string;
  approximateAge: number | null;
  criticalAllergies: string[];
  emergencyContact: { name: string; relationship: string; phone: string } | null;
}

function readJson<T>(key: string, fallback: T): T {
  try {
    const raw = window.localStorage.getItem(key);
    return raw ? (JSON.parse(raw) as T) : fallback;
  } catch {
    return fallback;
  }
}

export function getQueuedUpdates(): QueuedTimelineUpdate[] {
  if (typeof window === 'undefined') return [];
  return readJson<QueuedTimelineUpdate[]>(QUEUE_KEY, []);
}

export function queueNoncriticalUpdate(update: Pick<QueuedTimelineUpdate, 'sessionId' | 'kind' | 'eventCode'>): QueuedTimelineUpdate {
  const item: QueuedTimelineUpdate = {
    ...update,
    message: 'A noncritical status check was recorded while offline.',
    idempotencyKey: globalThis.crypto.randomUUID(),
    createdAtUtc: new Date().toISOString(),
  };
  const queue = getQueuedUpdates();
  queue.push(item);
  window.localStorage.setItem(QUEUE_KEY, JSON.stringify(queue));
  window.dispatchEvent(new CustomEvent('gh-queue-changed'));
  return item;
}

export async function flushQueuedUpdates(apiBaseUrl = ''): Promise<number> {
  const queue = getQueuedUpdates();
  if (!navigator.onLine || queue.length === 0) return 0;
  const remaining: QueuedTimelineUpdate[] = [];
  let synced = 0;

  for (const item of queue) {
    try {
      const response = await fetch(`${apiBaseUrl}/api/v1/sessions/${encodeURIComponent(item.sessionId)}/timeline`, {
        method: 'POST',
        credentials: 'include',
        headers: { 'Content-Type': 'application/json', 'Idempotency-Key': item.idempotencyKey },
        body: JSON.stringify({ type: item.kind, message: item.message, idempotencyKey: item.idempotencyKey }),
      });
      if (response.ok) synced += 1;
      else remaining.push(item);
    } catch {
      remaining.push(item);
    }
  }

  window.localStorage.setItem(QUEUE_KEY, JSON.stringify(remaining));
  window.dispatchEvent(new CustomEvent('gh-queue-changed'));
  return synced;
}

function ageFromDateOfBirth(dateOfBirth: string): number | null {
  const dob = new Date(dateOfBirth);
  if (Number.isNaN(dob.getTime())) return null;
  const today = new Date();
  let age = today.getUTCFullYear() - dob.getUTCFullYear();
  const beforeBirthday = today.getUTCMonth() < dob.getUTCMonth() || (today.getUTCMonth() === dob.getUTCMonth() && today.getUTCDate() < dob.getUTCDate());
  if (beforeBirthday) age -= 1;
  return Math.max(0, age);
}

export function saveMinimalOfflineCard(profile: EmergencyProfile): MinimalOfflineCard {
  const contact = profile.contacts[0];
  const card: MinimalOfflineCard = {
    version: 1,
    savedAtUtc: new Date().toISOString(),
    name: profile.shareFields.includes('name') ? profile.name : '',
    approximateAge: profile.shareFields.includes('approximateAge') ? ageFromDateOfBirth(profile.dateOfBirth) : null,
    criticalAllergies: profile.shareFields.includes('allergies') ? [...profile.allergies] : [],
    emergencyContact: profile.shareFields.includes('emergencyContact') && contact ? { name: contact.name, relationship: contact.relationship, phone: contact.phone } : null,
  };
  window.localStorage.setItem(CARD_KEY, JSON.stringify(card));
  return card;
}

export function removeMinimalOfflineCard(): void {
  window.localStorage.removeItem(CARD_KEY);
}

export function getMinimalOfflineCard(): MinimalOfflineCard | null {
  if (typeof window === 'undefined') return null;
  return readJson<MinimalOfflineCard | null>(CARD_KEY, null);
}

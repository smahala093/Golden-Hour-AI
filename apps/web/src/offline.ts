import type { EmergencyProfile } from './types';
import { api } from './api';

const QUEUE_KEY = 'gh-noncritical-queue-v1';
const CARD_KEY = 'gh-minimal-card-v1';
const OFFLINE_DB_NAME = 'golden-hour-private-offline-v2';
const OFFLINE_STORE_NAME = 'encrypted-card';
const ENCRYPTION_KEY_ID = 'aes-gcm-key';
const ENCRYPTED_CARD_ID = 'minimal-card';

// Version 1 stored the card as plaintext JSON. Never leave that legacy PHI in
// localStorage, even when this browser cannot open IndexedDB or WebCrypto.
if (typeof window !== 'undefined') window.localStorage.removeItem(CARD_KEY);

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

export async function flushQueuedUpdates(): Promise<number> {
  const queue = getQueuedUpdates();
  if (!navigator.onLine || queue.length === 0) return 0;
  const snapshotIds = new Set(queue.map((item) => item.idempotencyKey));
  const remaining: QueuedTimelineUpdate[] = [];
  let synced = 0;

  for (const item of queue) {
    try {
      await api.addTimeline(item.sessionId, item.kind, item.message, item.idempotencyKey);
      synced += 1;
    } catch {
      remaining.push(item);
    }
  }

  const queuedDuringFlush = getQueuedUpdates().filter((item) => !snapshotIds.has(item.idempotencyKey));
  window.localStorage.setItem(QUEUE_KEY, JSON.stringify([...remaining, ...queuedDuringFlush]));
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

interface EncryptedOfflineCard {
  version: 2;
  iv: number[];
  ciphertext: number[];
}

let cardMutationChain: Promise<void> = Promise.resolve();

function runCardMutation<T>(operation: () => Promise<T>): Promise<T> {
  const result = cardMutationChain.then(operation, operation);
  cardMutationChain = result.then(() => undefined, () => undefined);
  return result;
}

function requestResult<T>(request: IDBRequest<T>): Promise<T> {
  return new Promise((resolve, reject) => {
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error ?? new Error('IndexedDB request failed.'));
  });
}

function transactionComplete(transaction: IDBTransaction): Promise<void> {
  return new Promise((resolve, reject) => {
    transaction.oncomplete = () => resolve();
    transaction.onabort = () => reject(transaction.error ?? new Error('IndexedDB transaction was aborted.'));
    transaction.onerror = () => reject(transaction.error ?? new Error('IndexedDB transaction failed.'));
  });
}

function openOfflineDatabase(): Promise<IDBDatabase> {
  if (typeof indexedDB === 'undefined' || !globalThis.crypto?.subtle) return Promise.reject(new Error('Encrypted offline storage is unavailable.'));
  return new Promise((resolve, reject) => {
    const request = indexedDB.open(OFFLINE_DB_NAME, 1);
    request.onupgradeneeded = () => {
      if (!request.result.objectStoreNames.contains(OFFLINE_STORE_NAME)) request.result.createObjectStore(OFFLINE_STORE_NAME);
    };
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error ?? new Error('Encrypted offline storage could not be opened.'));
    request.onblocked = () => reject(new Error('Encrypted offline storage is blocked.'));
  });
}

async function readOfflineValue<T>(id: string): Promise<T | undefined> {
  const database = await openOfflineDatabase();
  try {
    const transaction = database.transaction(OFFLINE_STORE_NAME, 'readonly');
    const result: unknown = await requestResult<unknown>(transaction.objectStore(OFFLINE_STORE_NAME).get(id));
    await transactionComplete(transaction);
    return result as T | undefined;
  } finally {
    database.close();
  }
}

async function writeOfflineValue(id: string, value: unknown): Promise<void> {
  const database = await openOfflineDatabase();
  try {
    const transaction = database.transaction(OFFLINE_STORE_NAME, 'readwrite');
    transaction.objectStore(OFFLINE_STORE_NAME).put(value, id);
    await transactionComplete(transaction);
  } finally {
    database.close();
  }
}

async function getOrCreateEncryptionKey(): Promise<CryptoKey> {
  const existing = await readOfflineValue<CryptoKey>(ENCRYPTION_KEY_ID);
  if (existing) return existing;
  const generated = await globalThis.crypto.subtle.generateKey({ name: 'AES-GCM', length: 256 }, false, ['encrypt', 'decrypt']);
  await writeOfflineValue(ENCRYPTION_KEY_ID, generated);
  return generated;
}

async function deleteEncryptedCardAndKey(): Promise<void> {
  if (typeof indexedDB === 'undefined') return;
  const database = await openOfflineDatabase().catch(() => null);
  if (!database) return;
  try {
    const transaction = database.transaction(OFFLINE_STORE_NAME, 'readwrite');
    const store = transaction.objectStore(OFFLINE_STORE_NAME);
    store.delete(ENCRYPTED_CARD_ID);
    store.delete(ENCRYPTION_KEY_ID);
    await transactionComplete(transaction);
  } finally {
    database.close();
  }
}

function isMinimalOfflineCard(value: unknown): value is MinimalOfflineCard {
  if (!value || typeof value !== 'object') return false;
  const card = value as Partial<MinimalOfflineCard>;
  const contactValid = card.emergencyContact === null || (typeof card.emergencyContact === 'object'
    && typeof card.emergencyContact.name === 'string'
    && typeof card.emergencyContact.relationship === 'string'
    && typeof card.emergencyContact.phone === 'string');
  return card.version === 1
    && typeof card.savedAtUtc === 'string'
    && typeof card.name === 'string'
    && (card.approximateAge === null || typeof card.approximateAge === 'number')
    && Array.isArray(card.criticalAllergies)
    && card.criticalAllergies.every((item) => typeof item === 'string')
    && contactValid;
}

export function saveMinimalOfflineCard(profile: EmergencyProfile): Promise<MinimalOfflineCard | null> {
  const contact = profile.contacts[0];
  const card: MinimalOfflineCard = {
    version: 1,
    savedAtUtc: new Date().toISOString(),
    name: profile.shareFields.includes('name') ? profile.name : '',
    approximateAge: profile.shareFields.includes('approximateAge') ? ageFromDateOfBirth(profile.dateOfBirth) : null,
    criticalAllergies: profile.shareFields.includes('allergies') ? [...profile.allergies] : [],
    emergencyContact: profile.shareFields.includes('emergencyContact') && contact ? { name: contact.name, relationship: contact.relationship, phone: contact.phone } : null,
  };
  window.localStorage.removeItem(CARD_KEY);
  return runCardMutation(async () => {
    try {
      const key = await getOrCreateEncryptionKey();
      const iv = globalThis.crypto.getRandomValues(new Uint8Array(12));
      const plaintext = new TextEncoder().encode(JSON.stringify(card));
      const ciphertext = await globalThis.crypto.subtle.encrypt({ name: 'AES-GCM', iv }, key, plaintext);
      await writeOfflineValue(ENCRYPTED_CARD_ID, { version: 2, iv: [...iv], ciphertext: [...new Uint8Array(ciphertext)] } satisfies EncryptedOfflineCard);
      return card;
    } catch {
      await deleteEncryptedCardAndKey();
      return null;
    }
  });
}

export function removeMinimalOfflineCard(): Promise<void> {
  window.localStorage.removeItem(CARD_KEY);
  return runCardMutation(deleteEncryptedCardAndKey);
}

export function clearUserScopedOfflineData(): Promise<void> {
  window.localStorage.removeItem(CARD_KEY);
  window.localStorage.removeItem(QUEUE_KEY);
  window.dispatchEvent(new CustomEvent('gh-queue-changed'));
  return removeMinimalOfflineCard();
}

export async function getMinimalOfflineCard(): Promise<MinimalOfflineCard | null> {
  if (typeof window === 'undefined') return null;
  window.localStorage.removeItem(CARD_KEY);
  await cardMutationChain;
  try {
    const [key, encrypted] = await Promise.all([
      readOfflineValue<CryptoKey>(ENCRYPTION_KEY_ID),
      readOfflineValue<EncryptedOfflineCard>(ENCRYPTED_CARD_ID),
    ]);
    if (!key || !encrypted || encrypted.version !== 2 || encrypted.iv.length !== 12) return null;
    const plaintext = await globalThis.crypto.subtle.decrypt({ name: 'AES-GCM', iv: new Uint8Array(encrypted.iv) }, key, new Uint8Array(encrypted.ciphertext));
    const parsed = JSON.parse(new TextDecoder().decode(plaintext)) as unknown;
    return isMinimalOfflineCard(parsed) ? parsed : null;
  } catch {
    await removeMinimalOfflineCard();
    return null;
  }
}

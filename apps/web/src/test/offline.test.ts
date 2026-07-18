import 'fake-indexeddb/auto';
import { webcrypto } from 'node:crypto';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { demoProfile } from '../demoData';
import { clearUserScopedOfflineData, flushQueuedUpdates, getMinimalOfflineCard, getQueuedUpdates, queueNoncriticalUpdate, saveMinimalOfflineCard } from '../offline';

function setOnline(value: boolean): void {
  Object.defineProperty(navigator, 'onLine', { configurable: true, value });
}

beforeEach(() => {
  setOnline(true);
  Object.defineProperty(globalThis, 'crypto', { configurable: true, value: webcrypto });
});
afterEach(async () => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
  vi.useRealTimers();
  setOnline(true);
  await clearUserScopedOfflineData();
});

describe('bounded offline persistence', () => {
  it('queues only a fixed noncritical marker with an idempotency key', () => {
    const queued = queueNoncriticalUpdate({
      sessionId: 'fictional-session',
      kind: 'observation',
      eventCode: 'noncritical-check-recorded',
    });

    expect(queued.idempotencyKey).toMatch(/^[0-9a-f-]{36}$/i);
    expect(queued.message).toBe('A noncritical status check was recorded while offline.');
    expect(JSON.stringify(getQueuedUpdates())).not.toContain('patient');
    expect(getQueuedUpdates()).toEqual([queued]);
  });

  it('does not attempt synchronization while offline', async () => {
    queueNoncriticalUpdate({ sessionId: 'fictional-session', kind: 'note', eventCode: 'noncritical-check-recorded' });
    setOnline(false);
    const fetchMock = vi.fn();
    vi.stubGlobal('fetch', fetchMock);

    await expect(flushQueuedUpdates()).resolves.toBe(0);
    expect(fetchMock).not.toHaveBeenCalled();
    expect(getQueuedUpdates()).toHaveLength(1);
  });

  it('removes successful updates but retains failures for a later retry', async () => {
    const first = queueNoncriticalUpdate({ sessionId: 'session-one', kind: 'note', eventCode: 'noncritical-check-recorded' });
    const second = queueNoncriticalUpdate({ sessionId: 'session-two', kind: 'observation', eventCode: 'noncritical-check-recorded' });
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response(null, { status: 204 }))
      .mockResolvedValueOnce(new Response(JSON.stringify({ title: 'Unavailable' }), { status: 503, headers: { 'content-type': 'application/problem+json' } }));
    vi.stubGlobal('fetch', fetchMock);

    await expect(flushQueuedUpdates()).resolves.toBe(1);
    expect(getQueuedUpdates()).toEqual([second]);
    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect(fetchMock).toHaveBeenNthCalledWith(1, '/api/v1/sessions/session-one/timeline', expect.objectContaining({ method: 'POST' }));

    const firstRequest = fetchMock.mock.calls[0]?.[1] as RequestInit;
    expect(typeof firstRequest.body).toBe('string');
    if (typeof firstRequest.body !== 'string') throw new Error('Expected a serialized JSON body.');
    const body = JSON.parse(firstRequest.body) as Record<string, string>;
    expect(body).toEqual({ type: 'note', message: first.message, idempotencyKey: first.idempotencyKey });
    expect(firstRequest.headers).toEqual(expect.objectContaining({ 'Idempotency-Key': first.idempotencyKey }));
  });

  it('reuses the queued idempotency key after an access refresh', async () => {
    const queued = queueNoncriticalUpdate({ sessionId: 'session-refresh', kind: 'note', eventCode: 'noncritical-check-recorded' });
    const fetchMock = vi.fn()
      .mockResolvedValueOnce(new Response(JSON.stringify({ title: 'Unauthorized' }), { status: 401, headers: { 'content-type': 'application/problem+json' } }))
      .mockResolvedValueOnce(new Response(null, { status: 204 }))
      .mockResolvedValueOnce(new Response(null, { status: 204 }));
    vi.stubGlobal('fetch', fetchMock);

    await expect(flushQueuedUpdates()).resolves.toBe(1);

    expect(fetchMock).toHaveBeenCalledTimes(3);
    expect(fetchMock.mock.calls[1]?.[0]).toBe('/api/v1/auth/refresh');
    const firstTimeline = fetchMock.mock.calls[0]?.[1] as RequestInit;
    const retriedTimeline = fetchMock.mock.calls[2]?.[1] as RequestInit;
    expect(firstTimeline.headers).toEqual(expect.objectContaining({ 'Idempotency-Key': queued.idempotencyKey }));
    expect(retriedTimeline.headers).toEqual(expect.objectContaining({ 'Idempotency-Key': queued.idempotencyKey }));
  });

  it('does not lose an update queued while synchronization is in progress', async () => {
    queueNoncriticalUpdate({ sessionId: 'session-existing', kind: 'note', eventCode: 'noncritical-check-recorded' });
    let resolveRequest: ((response: Response) => void) | undefined;
    vi.stubGlobal('fetch', vi.fn(() => new Promise<Response>((resolve) => { resolveRequest = resolve; })));

    const flushing = flushQueuedUpdates();
    await vi.waitFor(() => expect(resolveRequest).toBeTypeOf('function'));
    const addedDuringFlush = queueNoncriticalUpdate({ sessionId: 'session-new', kind: 'observation', eventCode: 'noncritical-check-recorded' });
    resolveRequest?.(new Response(null, { status: 204 }));

    await expect(flushing).resolves.toBe(1);
    expect(getQueuedUpdates()).toEqual([addedDuringFlush]);
  });

  it('encrypts only the explicit minimal-card allowlist and never stores plaintext PHI in localStorage', async () => {
    const card = await saveMinimalOfflineCard(demoProfile);
    const serialized = window.localStorage.getItem('gh-minimal-card-v1') ?? '';

    expect(card?.savedAtUtc).toMatch(/^\d{4}-\d{2}-\d{2}T/);
    expect(card && { ...card, savedAtUtc: '<validated-utc>' }).toEqual({
      version: 1,
      savedAtUtc: '<validated-utc>',
      name: 'Raj Kumar',
      approximateAge: 64,
      criticalAllergies: ['Penicillin (self-reported)'],
      emergencyContact: { name: 'Asha Kumar', relationship: 'Daughter', phone: '+91 ••••• 41012' },
    });
    expect(serialized).toBe('');
    expect(JSON.stringify(window.localStorage)).not.toContain('Raj Kumar');
    await expect(getMinimalOfflineCard()).resolves.toEqual(card);
  });

  it('deletes a legacy plaintext card and fails closed', async () => {
    window.localStorage.setItem('gh-minimal-card-v1', '{not-json');
    await expect(getMinimalOfflineCard()).resolves.toBeNull();
    expect(window.localStorage.getItem('gh-minimal-card-v1')).toBeNull();
  });
});

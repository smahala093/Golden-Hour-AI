import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { demoProfile } from '../demoData';
import { flushQueuedUpdates, getMinimalOfflineCard, getQueuedUpdates, queueNoncriticalUpdate, saveMinimalOfflineCard } from '../offline';

function setOnline(value: boolean): void {
  Object.defineProperty(navigator, 'onLine', { configurable: true, value });
}

beforeEach(() => setOnline(true));
afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
  vi.useRealTimers();
  setOnline(true);
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

    await expect(flushQueuedUpdates('/gateway')).resolves.toBe(0);
    expect(fetchMock).not.toHaveBeenCalled();
    expect(getQueuedUpdates()).toHaveLength(1);
  });

  it('removes successful updates but retains failures for a later retry', async () => {
    const first = queueNoncriticalUpdate({ sessionId: 'session-one', kind: 'note', eventCode: 'noncritical-check-recorded' });
    const second = queueNoncriticalUpdate({ sessionId: 'session-two', kind: 'observation', eventCode: 'noncritical-check-recorded' });
    const fetchMock = vi.fn()
      .mockResolvedValueOnce({ ok: true } satisfies Partial<Response>)
      .mockResolvedValueOnce({ ok: false } satisfies Partial<Response>);
    vi.stubGlobal('fetch', fetchMock);

    await expect(flushQueuedUpdates('/gateway')).resolves.toBe(1);
    expect(getQueuedUpdates()).toEqual([second]);
    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect(fetchMock).toHaveBeenNthCalledWith(1, '/gateway/api/v1/sessions/session-one/timeline', expect.objectContaining({ method: 'POST' }));

    const firstRequest = fetchMock.mock.calls[0]?.[1] as RequestInit;
    expect(typeof firstRequest.body).toBe('string');
    if (typeof firstRequest.body !== 'string') throw new Error('Expected a serialized JSON body.');
    const body = JSON.parse(firstRequest.body) as Record<string, string>;
    expect(body).toEqual({ type: 'note', message: first.message, idempotencyKey: first.idempotencyKey });
    expect(firstRequest.headers).toEqual(expect.objectContaining({ 'Idempotency-Key': first.idempotencyKey }));
  });

  it('stores only the explicit minimal-card allowlist', () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-07-16T12:00:00.000Z'));

    const card = saveMinimalOfflineCard(demoProfile);
    const serialized = window.localStorage.getItem('gh-minimal-card-v1') ?? '';

    expect(card).toEqual({
      version: 1,
      savedAtUtc: '2026-07-16T12:00:00.000Z',
      name: 'Raj Kumar',
      approximateAge: 64,
      criticalAllergies: ['Penicillin (self-reported)'],
      emergencyContact: { name: 'Asha Kumar', relationship: 'Daughter', phone: '+91 ••••• 41012' },
    });
    expect(serialized).not.toContain('Demo policy');
    expect(serialized).not.toContain('Hypertension');
    expect(serialized).not.toContain('Metformin');
    expect(serialized).not.toContain('Suryodaya');
  });

  it('fails closed when the stored card is corrupted', () => {
    window.localStorage.setItem('gh-minimal-card-v1', '{not-json');
    expect(getMinimalOfflineCard()).toBeNull();
  });
});

import { afterEach, describe, expect, it } from 'vitest';
import {
  captureAndScrubParticipantInvite,
  captureParticipantInviteFragment,
  hasPendingParticipantInvite,
  participantInviteSessionId,
  safeParticipantInviteDestination,
  submitPendingParticipantInvite,
} from '../participantInvite';

const sessionId = 'bba4f6d9-ef7e-4e58-8cc6-d7d46ca2906c';
const token = 'AbCdEfGhIjKlMnOpQrStUvWxYz0123456789_-TOKEN';

afterEach(() => {
  window.history.replaceState(null, '', '/');
});

describe('participant invitation handoff', () => {
  it('scrubs the fragment and keeps the token out of browser-persisted state', async () => {
    const path = `/emergency/${sessionId}/join`;
    window.localStorage.setItem('unrelated', 'retained');
    window.history.replaceState({ idx: 3, key: 'route-key', usr: { from: `${path}#${token}` } }, '', `${path}#${token}`);

    expect(captureAndScrubParticipantInvite(sessionId)).toBe(true);
    expect(window.location.hash).toBe('');
    expect(JSON.stringify(window.history.state)).not.toContain(token);
    expect(JSON.stringify(window.localStorage)).not.toContain(token);
    expect(JSON.stringify(window.sessionStorage)).not.toContain(token);
    expect(hasPendingParticipantInvite(sessionId)).toBe(true);

    await expect(submitPendingParticipantInvite(sessionId, (pendingToken) => Promise.resolve(pendingToken))).resolves.toBe(token);
    expect(hasPendingParticipantInvite(sessionId)).toBe(false);
  });

  it('retains the in-memory token after a failed join but expires it after the bounded TTL', async () => {
    const capturedAt = 1_000;
    expect(captureParticipantInviteFragment(sessionId, `#${token}`, capturedAt)).toBe(true);
    await expect(submitPendingParticipantInvite(sessionId, () => Promise.reject(new Error('network')), capturedAt + 1)).rejects.toThrow('network');
    expect(hasPendingParticipantInvite(sessionId, capturedAt + 2)).toBe(true);
    expect(hasPendingParticipantInvite(sessionId, capturedAt + 15 * 60_000)).toBe(false);
  });

  it('accepts only a path-only participant destination and rejects token-bearing router state', () => {
    const path = `/emergency/${sessionId}/join`;
    expect(participantInviteSessionId(path)).toBe(sessionId);
    expect(safeParticipantInviteDestination({ from: path })).toBe(path);
    expect(safeParticipantInviteDestination({ from: `${path}#${token}` })).toBeNull();
    expect(safeParticipantInviteDestination({ from: `${path}?token=${token}` })).toBeNull();
    expect(captureParticipantInviteFragment(sessionId, '#short')).toBe(false);
  });
});

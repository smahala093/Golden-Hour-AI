const INVITE_MEMORY_TTL_MS = 15 * 60_000;
const SESSION_ID_PATTERN = /^[a-z0-9-]{1,80}$/i;
const TOKEN_PATTERN = /^[a-z0-9_-]{20,512}$/i;

interface PendingParticipantInvite {
  token: string;
  expiresAt: number;
}

const pendingInvites = new Map<string, PendingParticipantInvite>();

function sessionKey(sessionId: string): string | null {
  return SESSION_ID_PATTERN.test(sessionId) ? sessionId.toLowerCase() : null;
}

function removeExpiredInvites(now: number): void {
  for (const [key, invite] of pendingInvites) {
    if (invite.expiresAt <= now) pendingInvites.delete(key);
  }
}

function safeRouterHistoryState(): Record<string, string | number> | null {
  const current = window.history.state as unknown;
  if (!current || typeof current !== 'object') return null;
  const state = current as Record<string, unknown>;
  const safe: Record<string, string | number> = {};
  if (typeof state.idx === 'number' && Number.isInteger(state.idx)) safe.idx = state.idx;
  if (typeof state.key === 'string' && /^[a-z0-9_-]{1,32}$/i.test(state.key)) safe.key = state.key;
  return Object.keys(safe).length > 0 ? safe : null;
}

export function participantInviteSessionId(pathname: string): string | null {
  const match = /^\/emergency\/([a-z0-9-]{1,80})\/join$/i.exec(pathname);
  return match?.[1] ?? null;
}

export function safeParticipantInviteDestination(state: unknown): string | null {
  if (!state || typeof state !== 'object' || !('from' in state)) return null;
  const requested = (state as { from?: unknown }).from;
  return typeof requested === 'string' && participantInviteSessionId(requested) ? requested : null;
}

export function captureParticipantInviteFragment(sessionId: string, hash: string, now = Date.now()): boolean {
  removeExpiredInvites(now);
  const key = sessionKey(sessionId);
  if (!key) return false;

  // A new fragment replaces any older pending value for this session, including
  // an invalid fragment. This prevents a stale invitation from being reused.
  pendingInvites.delete(key);
  if (!hash.startsWith('#') || hash.length > 1_025) return false;

  let token: string;
  try {
    token = decodeURIComponent(hash.slice(1));
  } catch {
    return false;
  }
  if (!TOKEN_PATTERN.test(token)) return false;

  pendingInvites.set(key, { token, expiresAt: now + INVITE_MEMORY_TTL_MS });
  return true;
}

export function captureAndScrubParticipantInvite(sessionId: string, hash = window.location.hash): boolean {
  const captured = captureParticipantInviteFragment(sessionId, hash);
  if (hash) {
    window.history.replaceState(
      safeRouterHistoryState(),
      document.title,
      `${window.location.pathname}${window.location.search}`,
    );
  }
  return captured;
}

export function hasPendingParticipantInvite(sessionId: string, now = Date.now()): boolean {
  removeExpiredInvites(now);
  const key = sessionKey(sessionId);
  return key ? pendingInvites.has(key) : false;
}

export async function submitPendingParticipantInvite<T>(
  sessionId: string,
  submit: (token: string) => Promise<T>,
  now = Date.now(),
): Promise<T> {
  removeExpiredInvites(now);
  const key = sessionKey(sessionId);
  const invite = key ? pendingInvites.get(key) : undefined;
  if (!key || !invite) throw new Error('Participant invitation is unavailable or expired.');

  const result = await submit(invite.token);
  if (pendingInvites.get(key) === invite) pendingInvites.delete(key);
  return result;
}

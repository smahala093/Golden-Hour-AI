import type { EmergencySession } from './types';
import { isValidEmergencyNumber } from './publicEmergencyNumber';

export function shouldLogEmergencyCall(
  pathname: string,
  session: Pick<EmergencySession, 'id' | 'status'>,
): boolean {
  if (!session.id || session.status === 'closed') return false;
  const match = /^\/emergency\/([^/]+)(?:\/|$)/.exec(pathname);
  if (!match?.[1]) return false;

  try {
    return decodeURIComponent(match[1]).toLowerCase() === session.id.toLowerCase();
  } catch {
    return false;
  }
}

export function emergencyCallNumberForRoute(
  pathname: string,
  session: Pick<EmergencySession, 'id' | 'status' | 'emergencyNumber'>,
  configuredDefault: string,
): string {
  return shouldLogEmergencyCall(pathname, session) && isValidEmergencyNumber(session.emergencyNumber)
    ? session.emergencyNumber
    : configuredDefault;
}

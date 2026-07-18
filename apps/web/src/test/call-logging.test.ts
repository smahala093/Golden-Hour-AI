import { describe, expect, it } from 'vitest';
import { emergencyCallNumberForRoute, shouldLogEmergencyCall } from '../callLogging';

const activeSession = { id: '7f985126-1b6f-4aa8-a391-c176bf172314', status: 'active' as const };

describe('persistent emergency call logging', () => {
  it('logs only on routes belonging to the same current session', () => {
    expect(shouldLogEmergencyCall(`/emergency/${activeSession.id}`, activeSession)).toBe(true);
    expect(shouldLogEmergencyCall(`/emergency/${activeSession.id}/tasks`, activeSession)).toBe(true);
    expect(shouldLogEmergencyCall('/home', activeSession)).toBe(false);
    expect(shouldLogEmergencyCall('/emergency/9ecf837f-d6e4-48d4-9728-31b99feb7af8/action', activeSession)).toBe(false);
    expect(shouldLogEmergencyCall(`/emergency/${activeSession.id}-other`, activeSession)).toBe(false);
  });

  it('does not write timeline events for a closed or missing session', () => {
    expect(shouldLogEmergencyCall(`/emergency/${activeSession.id}`, { ...activeSession, status: 'closed' })).toBe(false);
    expect(shouldLogEmergencyCall('/emergency/start', { id: '', status: 'active' })).toBe(false);
  });

  it('uses the session number only on that session route and otherwise keeps the public dial link safe', () => {
    const session = { ...activeSession, emergencyNumber: '+44123456789' };
    expect(emergencyCallNumberForRoute(`/emergency/${activeSession.id}/action`, session, '112')).toBe('+44123456789');
    expect(emergencyCallNumberForRoute('/home', session, '112')).toBe('112');
    expect(emergencyCallNumberForRoute('/emergency/another-session/action', session, '112')).toBe('112');
    expect(emergencyCallNumberForRoute(`/emergency/${activeSession.id}`, { ...session, status: 'closed' }, '112')).toBe('112');
    expect(emergencyCallNumberForRoute(`/emergency/${activeSession.id}`, { ...session, emergencyNumber: '112;911' }, '112')).toBe('112');
  });
});

const API_BASE = (import.meta.env.VITE_API_BASE_URL as string | undefined)?.replace(/\/$/, '') ?? '';
const DEFAULT_EMERGENCY_NUMBER = '112';

let hydratedEmergencyNumber: string | null = null;
let hydrationInFlight: Promise<string> | null = null;

export function isValidEmergencyNumber(value: unknown): value is string {
  return typeof value === 'string' && /^(?:[0-9]{2,16}|\+[0-9]{2,15})$/.test(value);
}

function configuredEmergencyNumber(): string {
  const configured = import.meta.env.VITE_EMERGENCY_NUMBER as string | undefined;
  return isValidEmergencyNumber(configured) ? configured : DEFAULT_EMERGENCY_NUMBER;
}

export function currentPublicEmergencyNumber(): string {
  return hydratedEmergencyNumber ?? configuredEmergencyNumber();
}

async function requestPublicEmergencyNumber(): Promise<string> {
  const response = await fetch(`${API_BASE}/api/v1/configuration`, {
    credentials: 'omit',
    headers: { Accept: 'application/json' },
  });
  if (!response.ok) throw new Error('Public emergency configuration is unavailable.');
  const payload = await response.json() as unknown;
  const value = payload && typeof payload === 'object' && 'emergencyNumber' in payload
    ? (payload as { emergencyNumber?: unknown }).emergencyNumber
    : undefined;
  if (!isValidEmergencyNumber(value)) throw new Error('Public emergency configuration is invalid.');
  hydratedEmergencyNumber = value;
  return value;
}

export function hydratePublicEmergencyNumber(): Promise<string> {
  if (hydratedEmergencyNumber) return Promise.resolve(hydratedEmergencyNumber);
  hydrationInFlight ??= requestPublicEmergencyNumber().finally(() => { hydrationInFlight = null; });
  return hydrationInFlight;
}

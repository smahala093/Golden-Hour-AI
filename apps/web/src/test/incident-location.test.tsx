import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { api } from '../api';
import { demoProfile, demoSession } from '../demoData';
import { IncidentCapturePage } from '../pages/EmergencyFlowPages';
import { renderWithApp } from './render';

afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe('incident location fallback', () => {
  it('keeps a typed landmark and sends consented bounded coordinates after geolocation denial', async () => {
    vi.spyOn(api, 'getCurrentUser').mockResolvedValue({ authenticated: true });
    vi.spyOn(api, 'getProfile').mockResolvedValue(demoProfile);
    vi.spyOn(api, 'submitIncident').mockResolvedValue(demoSession);
    const updateCoordinates = vi.spyOn(api, 'updateLocationCoordinates').mockResolvedValue({ ...demoSession, location: 'Typed Jaipur landmark' });
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('[]', { status: 200, headers: { 'content-type': 'application/json' } })));
    Object.defineProperty(navigator, 'geolocation', {
      configurable: true,
      value: { getCurrentPosition: (_success: PositionCallback, failure: PositionErrorCallback) => failure({ code: 1, message: 'denied', PERMISSION_DENIED: 1, POSITION_UNAVAILABLE: 2, TIMEOUT: 3 }) },
    });
    const user = userEvent.setup();
    renderWithApp(<IncidentCapturePage />, '/emergency/test-session/capture');

    await user.type(screen.getByLabelText('Type the situation'), 'A fictional incident report for boundary testing.');
    await user.type(screen.getByLabelText('Type location instead'), 'Typed Jaipur landmark');
    await user.click(screen.getByRole('button', { name: 'Use my location' }));

    const latitude = screen.getByLabelText('Latitude (−90 to 90)');
    const longitude = screen.getByLabelText('Longitude (−180 to 180)');
    expect(latitude).toHaveAttribute('min', '-90');
    expect(latitude).toHaveAttribute('max', '90');
    expect(longitude).toHaveAttribute('min', '-180');
    expect(longitude).toHaveAttribute('max', '180');

    await user.type(latitude, '26.9124');
    await user.type(longitude, '75.7873');
    await user.click(screen.getByLabelText('I consent to share these coordinates for this active emergency.'));
    await user.click(screen.getByRole('button', { name: 'Use this map pin' }));
    expect(screen.getByText(/Map pin is ready/)).toBeVisible();

    await user.click(screen.getByRole('button', { name: 'Use this description' }));

    await waitFor(() => expect(updateCoordinates).toHaveBeenCalledWith(demoSession.id, 26.9124, 75.7873, 'Typed Jaipur landmark'));
  }, 30_000);

  it('does not accept a manual pin without consent or within invalid ranges', async () => {
    vi.spyOn(api, 'getCurrentUser').mockRejectedValue({ status: 401 });
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('[]', { status: 200, headers: { 'content-type': 'application/json' } })));
    Object.defineProperty(navigator, 'geolocation', { configurable: true, value: undefined });
    const user = userEvent.setup();
    renderWithApp(<IncidentCapturePage />);

    await user.click(screen.getByRole('button', { name: 'Use my location' }));
    await user.type(screen.getByLabelText('Latitude (−90 to 90)'), '91');
    await user.type(screen.getByLabelText('Longitude (−180 to 180)'), '181');
    await user.click(screen.getByRole('button', { name: 'Use this map pin' }));
    expect(screen.getByText(/Confirm consent/)).toBeVisible();
    await user.click(screen.getByLabelText('I consent to share these coordinates for this active emergency.'));
    await user.click(screen.getByRole('button', { name: 'Use this map pin' }));
    expect(screen.getByText(/Enter a latitude/)).toBeVisible();
  });
});

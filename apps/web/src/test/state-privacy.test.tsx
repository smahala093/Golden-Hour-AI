import 'fake-indexeddb/auto';
import { webcrypto } from 'node:crypto';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { api } from '../api';
import { demoProfile, demoSession } from '../demoData';
import { clearUserScopedOfflineData, getMinimalOfflineCard, getQueuedUpdates, queueNoncriticalUpdate, saveMinimalOfflineCard } from '../offline';
import { SettingsPage } from '../pages/ProfilePages';
import { AppStateProvider, useAppState } from '../state';

function StateProbe() {
  const { authenticated, profileBootstrapComplete, profile, session, draft, setAuthenticated, setProfile, setSession, updateDraft } = useAppState();
  return <div><output aria-label="auth">{String(authenticated)}</output><output aria-label="profile-ready">{String(profileBootstrapComplete)}</output><output aria-label="profile-name">{profile.name}</output><output aria-label="session-id">{session.id}</output><output aria-label="draft-input">{draft.input}</output><button type="button" onClick={() => { setProfile(demoProfile); setSession(demoSession); updateDraft({ input: 'private prior-user report' }); }}>Seed prior user</button><button type="button" onClick={() => window.dispatchEvent(new Event('gh-auth-expired'))}>Expire auth</button><button type="button" onClick={() => setAuthenticated(true)}>Authenticate next user</button></div>;
}

function renderProbe() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(<QueryClientProvider client={client}><MemoryRouter><AppStateProvider><StateProbe /></AppStateProvider></MemoryRouter></QueryClientProvider>);
}

beforeEach(() => {
  Object.defineProperty(globalThis, 'crypto', { configurable: true, value: webcrypto });
  Object.defineProperty(navigator, 'onLine', { configurable: true, value: true });
});
afterEach(async () => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
  Object.defineProperty(navigator, 'onLine', { configurable: true, value: true });
  await clearUserScopedOfflineData();
});

describe('user-state privacy boundaries', () => {
  it('clears sensitive state on auth expiry and gates the next user until their profile bootstrap completes', async () => {
    const nextProfile = { ...demoProfile, name: 'Next User', contacts: [], allergies: [], conditions: [], medicines: [], procedures: [] };
    let resolveNextProfile: ((profile: typeof nextProfile) => void) | undefined;
    vi.spyOn(api, 'getCurrentUser').mockResolvedValue({ authenticated: true });
    vi.spyOn(api, 'getProfile')
      .mockResolvedValueOnce(demoProfile)
      .mockImplementationOnce(() => new Promise((resolve) => { resolveNextProfile = resolve; }));
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('[]', { status: 200, headers: { 'content-type': 'application/json' } })));
    const user = userEvent.setup();
    renderProbe();

    await waitFor(() => expect(screen.getByLabelText('profile-ready')).toHaveTextContent('true'));
    await user.click(screen.getByRole('button', { name: 'Seed prior user' }));
    queueNoncriticalUpdate({ sessionId: demoSession.id, kind: 'note', eventCode: 'noncritical-check-recorded' });
    await saveMinimalOfflineCard(demoProfile);
    expect(screen.getByLabelText('profile-name')).toHaveTextContent('Raj Kumar');

    await user.click(screen.getByRole('button', { name: 'Expire auth' }));
    await waitFor(() => expect(screen.getByLabelText('auth')).toHaveTextContent('false'));
    expect(screen.getByLabelText('profile-name')).toHaveTextContent('');
    expect(screen.getByLabelText('session-id')).toHaveTextContent('');
    expect(screen.getByLabelText('draft-input')).toHaveTextContent('');
    expect(getQueuedUpdates()).toEqual([]);
    await expect(getMinimalOfflineCard()).resolves.toBeNull();

    await user.click(screen.getByRole('button', { name: 'Authenticate next user' }));
    await waitFor(() => expect(resolveNextProfile).toBeTypeOf('function'));
    expect(screen.getByLabelText('profile-ready')).toHaveTextContent('false');
    expect(screen.getByLabelText('profile-name')).not.toHaveTextContent('Raj Kumar');
    resolveNextProfile?.(nextProfile);
    await waitFor(() => expect(screen.getByLabelText('profile-name')).toHaveTextContent('Next User'));
    expect(screen.getByLabelText('profile-ready')).toHaveTextContent('true');
  });

  it('preserves an existing opt-in encrypted card during an offline cold bootstrap', async () => {
    window.localStorage.setItem('gh-preferences-v1', JSON.stringify({ offlineCardEnabled: true }));
    await saveMinimalOfflineCard(demoProfile);
    Object.defineProperty(navigator, 'onLine', { configurable: true, value: false });

    renderProbe();

    await waitFor(() => expect(screen.getByLabelText('auth')).toHaveTextContent('false'));
    await expect(getMinimalOfflineCard()).resolves.toEqual(expect.objectContaining({ name: 'Raj Kumar' }));
  });

  it('clears queued and encrypted user data even when logout delivery fails', async () => {
    window.localStorage.setItem('gh-preferences-v1', JSON.stringify({ offlineCardEnabled: true }));
    vi.spyOn(api, 'getCurrentUser').mockResolvedValue({ authenticated: true });
    vi.spyOn(api, 'getProfile').mockResolvedValue(demoProfile);
    vi.spyOn(api, 'logout').mockRejectedValue(new Error('network unavailable'));
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('[]', { status: 200, headers: { 'content-type': 'application/json' } })));
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const user = userEvent.setup();
    render(<QueryClientProvider client={client}><MemoryRouter><AppStateProvider><SettingsPage /></AppStateProvider></MemoryRouter></QueryClientProvider>);
    await waitFor(() => expect(api.getProfile).toHaveBeenCalled());
    queueNoncriticalUpdate({ sessionId: demoSession.id, kind: 'note', eventCode: 'noncritical-check-recorded' });
    await saveMinimalOfflineCard(demoProfile);

    await user.click(screen.getByRole('button', { name: 'Sign out' }));

    await waitFor(() => expect(getQueuedUpdates()).toEqual([]));
    await expect(getMinimalOfflineCard()).resolves.toBeNull();
  });
});

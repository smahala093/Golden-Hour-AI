import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { api } from '../api';
import { ServerSummaryCard } from '../pages/SessionPages';
import { renderWithApp } from './render';

afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe('authoritative server summary', () => {
  it('renders and copies the server response rather than composing a client-only summary', async () => {
    vi.spyOn(api, 'getCurrentUser').mockRejectedValue({ status: 401 });
    vi.spyOn(api, 'generateSummary').mockResolvedValue({ id: 'summary-1', kind: 'hospital-handover', content: 'Authoritative server handover content', language: 'en', protocolVersion: 'reviewed-v1', createdAtUtc: '2026-07-18T10:00:00.000Z' });
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('[]', { status: 200, headers: { 'content-type': 'application/json' } })));
    const user = userEvent.setup();
    const writeText = vi.spyOn(navigator.clipboard, 'writeText').mockResolvedValue(undefined);
    renderWithApp(<ServerSummaryCard sessionId="session-1" kind="hospital-handover" />);

    await user.click(screen.getByRole('button', { name: 'Generate server summary' }));

    expect(await screen.findByText('Authoritative server handover content')).toBeVisible();
    expect(api.generateSummary).toHaveBeenCalledWith('session-1', 'hospital-handover');
    await user.click(screen.getByRole('button', { name: 'Copy brief' }));
    expect(writeText).toHaveBeenCalledWith('Authoritative server handover content');
    expect(screen.getByRole('status')).toHaveTextContent('Server summary copied.');
  });
});

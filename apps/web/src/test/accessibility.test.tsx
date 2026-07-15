import axe from 'axe-core';
import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { PublicShell, ConnectionBanner } from '../components/AppShell';
import { LandingPage } from '../pages/PublicPages';
import { renderWithApp } from './render';

afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

function unavailableApi(): void {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
    ok: false,
    status: 401,
    headers: new Headers({ 'content-type': 'application/problem+json' }),
    json: () => Promise.resolve({ title: 'Unauthorized' }),
  } satisfies Partial<Response>));
}

describe('public emergency entry accessibility', () => {
  it('has no automated axe violations and keeps the emergency call prominent', async () => {
    unavailableApi();
    const { container } = renderWithApp(<PublicShell><LandingPage /></PublicShell>);

    const call = screen.getByRole('link', { name: 'Call 112' });
    expect(call).toHaveAttribute('href', 'tel:112');
    expect(screen.getByText(/not a doctor, diagnostic system/i)).toBeVisible();

    const results = await axe.run(container);
    expect(results.violations, results.violations.map((violation) => `${violation.id}: ${violation.help}`).join('\n')).toEqual([]);
  });

  it('makes every primary entry action reachable in keyboard order', async () => {
    unavailableApi();
    const user = userEvent.setup();
    renderWithApp(<PublicShell><LandingPage /></PublicShell>);
    const expected = [
      screen.getByRole('button', { name: 'Open demo' }),
      screen.getByRole('link', { name: 'Help someone nearby' }),
      screen.getByRole('link', { name: 'Create an account' }),
      screen.getByRole('link', { name: 'Sign in' }),
      screen.getByRole('link', { name: 'Call 112' }),
    ];
    const reached = new Set<Element>();

    for (let index = 0; index < 12; index += 1) {
      await user.tab();
      if (document.activeElement) reached.add(document.activeElement);
    }

    for (const control of expected) expect(reached.has(control)).toBe(true);
  });

  it('announces realtime fallback state politely', () => {
    unavailableApi();
    renderWithApp(<ConnectionBanner state="polling" />);
    expect(screen.getByRole('status')).toHaveTextContent('Live updates unavailable · checking periodically');
    expect(screen.getByRole('status')).toHaveAttribute('aria-live', 'polite');
  });
});

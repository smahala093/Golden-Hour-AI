import { screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { PublicShell } from '../components/AppShell';
import { renderWithApp } from './render';

afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe('public emergency number hydration', () => {
  it('renders a working fallback link immediately, then replaces it with validated server configuration', async () => {
    let resolveConfiguration: ((response: Response) => void) | undefined;
    const configuration = new Promise<Response>((resolve) => { resolveConfiguration = resolve; });
    vi.stubGlobal('fetch', vi.fn((input: RequestInfo | URL) => {
      const url = typeof input === 'string' ? input : input instanceof URL ? input.toString() : input.url;
      if (url.endsWith('/api/v1/configuration')) return configuration;
      if (url.includes('/api/v1/protocols')) return Promise.resolve(new Response('[]', { status: 200, headers: { 'content-type': 'application/json' } }));
      return Promise.resolve(new Response(JSON.stringify({ title: 'Unauthorized' }), { status: 401, headers: { 'content-type': 'application/problem+json' } }));
    }));

    renderWithApp(<PublicShell><p>Public page</p></PublicShell>);

    expect(screen.getByRole('link', { name: 'Call 112' })).toHaveAttribute('href', 'tel:112');
    resolveConfiguration?.(new Response(JSON.stringify({ emergencyNumber: '+44123456789' }), { status: 200, headers: { 'content-type': 'application/json' } }));

    expect(await screen.findByRole('link', { name: 'Call +44123456789' })).toHaveAttribute('href', 'tel:+44123456789');
  });
});

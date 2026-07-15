import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, type RenderResult } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import type { ReactElement } from 'react';
import { AppStateProvider } from '../state';

export function renderWithApp(ui: ReactElement, route = '/'): RenderResult {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false, gcTime: 0 },
      mutations: { retry: false },
    },
  });

  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[route]}>
        <AppStateProvider>{ui}</AppStateProvider>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

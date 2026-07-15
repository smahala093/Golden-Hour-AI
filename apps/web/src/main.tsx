import React from 'react';
import ReactDOM from 'react-dom/client';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { BrowserRouter } from 'react-router-dom';
import { registerSW } from 'virtual:pwa-register';
import './i18n';
import './styles.css';
import { App } from './App';
import { AppStateProvider } from './state';

const queryClient = new QueryClient({
  defaultOptions: {
    queries: { staleTime: 10_000, retry: 1, refetchOnWindowFocus: true },
    mutations: { retry: false },
  },
});

registerSW({ immediate: true });

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <AppStateProvider><App /></AppStateProvider>
      </BrowserRouter>
    </QueryClientProvider>
  </React.StrictMode>,
);

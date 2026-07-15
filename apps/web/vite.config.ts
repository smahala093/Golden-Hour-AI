import react from '@vitejs/plugin-react';
import { defineConfig } from 'vitest/config';
import { VitePWA } from 'vite-plugin-pwa';

export default defineConfig({
  plugins: [
    react(),
    VitePWA({
      registerType: 'autoUpdate',
      includeAssets: ['brand-mark.svg'],
      manifest: {
        name: 'Golden Hour AI',
        short_name: 'Golden Hour',
        description: 'Turn panic into coordinated action.',
        theme_color: '#a83a2a',
        background_color: '#fff9ef',
        display: 'standalone',
        orientation: 'portrait-primary',
        start_url: '/home',
        scope: '/',
        categories: ['medical', 'utilities'],
        icons: [
          { src: '/icon-192.png', sizes: '192x192', type: 'image/png', purpose: 'any' },
          { src: '/icon-512.png', sizes: '512x512', type: 'image/png', purpose: 'any' },
          { src: '/icon-maskable-512.png', sizes: '512x512', type: 'image/png', purpose: 'maskable' },
        ],
      },
      workbox: {
        navigateFallback: '/index.html',
        globPatterns: ['**/*.{js,css,html,svg,json,woff2}'],
        runtimeCaching: [
          {
            urlPattern: ({ url }) => url.pathname === '/api/v1/protocols' && url.searchParams.get('country') === 'IN',
            handler: 'CacheFirst',
            options: { cacheName: 'reviewed-server-protocols-v1', expiration: { maxEntries: 2, maxAgeSeconds: 2592000 } },
          },
        ],
      },
      devOptions: { enabled: true },
    }),
  ],
  server: {
    port: 5173,
    proxy: {
      '/api': { target: 'http://localhost:8080', changeOrigin: true },
      '/hubs': { target: 'http://localhost:8080', changeOrigin: true, ws: true },
    },
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    css: true,
    coverage: {
      provider: 'v8',
      reporter: ['text', 'html', 'json', 'lcov'],
      reportsDirectory: './coverage',
      include: ['src/**/*.{ts,tsx}'],
      exclude: ['src/main.tsx', 'src/test/**'],
    },
  },
});

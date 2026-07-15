import { defineConfig, devices } from '@playwright/test';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const e2eDirectory = dirname(fileURLToPath(import.meta.url));
const repositoryRoot = resolve(e2eDirectory, '..', '..');
const mockBaseUrl = 'http://127.0.0.1:5173';
const realBaseUrl = 'http://127.0.0.1:5174';

export default defineConfig({
  testDir: './specs',
  outputDir: './test-results',
  fullyParallel: false,
  workers: 1,
  retries: process.env.CI ? 2 : 0,
  timeout: 90_000,
  expect: { timeout: 10_000 },
  forbidOnly: Boolean(process.env.CI),
  reporter: [
    ['line'],
    ['html', { open: 'never', outputFolder: 'playwright-report' }],
  ],
  use: {
    actionTimeout: 10_000,
    navigationTimeout: 20_000,
    screenshot: 'only-on-failure',
    trace: 'off',
    video: 'off',
    serviceWorkers: 'allow',
  },
  webServer: [
    {
      command: 'npm run dev --prefix apps/web -- --host 127.0.0.1 --port 5173 --strictPort',
      cwd: repositoryRoot,
      env: { VITE_MOCK_MODE: 'true', VITE_EMERGENCY_NUMBER: '112' },
      url: mockBaseUrl,
      reuseExistingServer: !process.env.CI,
      timeout: 120_000,
    },
    {
      command: 'dotnet run --project apps/api/GoldenHour.Api.csproj --no-launch-profile',
      cwd: repositoryRoot,
      env: {
        ASPNETCORE_ENVIRONMENT: 'Development',
        ASPNETCORE_URLS: 'http://127.0.0.1:8080',
        DOTNET_CLI_HOME: resolve(repositoryRoot, '.dotnet-home'),
        Database__UseInMemory: 'true',
        Providers__UseMocks: 'true',
      },
      url: 'http://127.0.0.1:8080/health/live',
      reuseExistingServer: !process.env.CI,
      timeout: 120_000,
    },
    {
      command: 'npm run dev --prefix apps/web -- --host 127.0.0.1 --port 5174 --strictPort',
      cwd: repositoryRoot,
      env: { VITE_MOCK_MODE: 'false', VITE_EMERGENCY_NUMBER: '112' },
      url: realBaseUrl,
      reuseExistingServer: !process.env.CI,
      timeout: 120_000,
    },
  ],
  projects: [
    {
      name: 'mock-desktop',
      testMatch: /demo-flow\.mock\.spec\.ts/,
      use: { ...devices['Desktop Chrome'], baseURL: mockBaseUrl },
    },
    {
      name: 'mock-mobile',
      testMatch: /mobile-accessibility\.mock\.spec\.ts/,
      use: { ...devices['Pixel 7'], baseURL: mockBaseUrl },
    },
    {
      name: 'real-api',
      testMatch: /real-api\.spec\.ts/,
      use: { ...devices['Desktop Chrome'], baseURL: realBaseUrl },
    },
  ],
});

// Testy E2E (SF-21): prawdziwa aplikacja Angular (ng serve, konfiguracja production, proxy /api),
// prawdziwe API (global setup) i SQL Server z bazą StoneFlowE2E. Uruchomienie: npm run e2e.
// Projekty: `setup` (logowanie i zapis sesji) → `chromium` (procesy, dostępność, klawiatura) →
// `session` (wylogowanie kończy wszystkie sesje konta, więc biegnie ostatni).
import { defineConfig, devices } from '@playwright/test';
import { API_URL, APP_PORT, APP_URL, STORAGE_STATE } from './e2e/support/env';

const ci = !!process.env['CI'];

export default defineConfig({
  testDir: 'e2e',
  fullyParallel: false,
  workers: 1,
  retries: ci ? 1 : 0,
  forbidOnly: ci,
  timeout: 60_000,
  expect: { timeout: 10_000 },
  reporter: [['list'], ['html', { open: 'never', outputFolder: 'playwright-report' }]],
  outputDir: 'test-results',
  globalSetup: './e2e/global-setup.ts',
  use: {
    baseURL: APP_URL,
    locale: 'pl-PL',
    timezoneId: 'Europe/Warsaw',
    // Kolejność części pola `type="date"` zależy od języka przeglądarki, nie od `locale` kontekstu.
    // Na Linuksie Chromium bierze go z LANGUAGE/LANG, na Windows z `--lang`.
    launchOptions: {
      args: ['--lang=pl-PL'],
      env: { ...process.env, LANG: 'pl_PL.UTF-8', LANGUAGE: 'pl_PL:pl' },
    },
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  webServer: {
    command: `npx ng serve --configuration production --port ${APP_PORT}`,
    url: `${APP_URL}/logowanie`,
    // Nigdy nie używa już działającego serwera: jego proxy mogłoby kierować do API deweloperskiego.
    reuseExistingServer: false,
    timeout: 240_000,
    env: { STONEFLOW_API_URL: API_URL },
  },
  projects: [
    {
      name: 'setup',
      testMatch: /auth\.setup\.ts/,
      use: { ...devices['Desktop Chrome'], trace: 'off' },
    },
    {
      name: 'chromium',
      testMatch: /.*\.e2e\.ts/,
      testIgnore: /session\.e2e\.ts/,
      dependencies: ['setup'],
      use: { ...devices['Desktop Chrome'], storageState: STORAGE_STATE },
    },
    {
      name: 'session',
      testMatch: /session\.e2e\.ts/,
      dependencies: ['chromium'],
      use: { ...devices['Desktop Chrome'], trace: 'off' },
    },
  ],
});

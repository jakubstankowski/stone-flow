// Global setup E2E (SF-21): data przebiegu, proces API na bazie StoneFlowE2E i konto właściciela
// z losowym hasłem ustawionym istniejącym przepływem resetu hasła (link z logu API, zdarzenie 6051).
// Hasło zostaje tylko w pamięci (process.env dziedziczą procesy robocze Playwright); nic nie
// trafia na dysk. Zwracana funkcja zatrzymuje API po przebiegu.
import { request } from '@playwright/test';
import { randomBytes } from 'node:crypto';
import { ApiProcess } from './support/api-process';
import { databaseConnection } from './support/connection';
import { launchDateFor, warsawToday } from './support/dates';
import { API_URL, APP_URL, OWNER_EMAIL } from './support/env';

const MIGRATE_HINT =
  'Czy baza StoneFlowE2E jest zmigrowana (deploy/scripts/migrate.sh, instrukcja: .sdd/context/conventions.md, sekcja Verification)?';

export default async function globalSetup(): Promise<() => Promise<void>> {
  const server = process.env['STONEFLOW_E2E_SQLSERVER'];
  if (!server) {
    throw new Error(
      'Brak STONEFLOW_E2E_SQLSERVER — connection string do SQL Server z bazą StoneFlowE2E (bez nazwy bazy).',
    );
  }

  const today = warsawToday();
  process.env['STONEFLOW_E2E_TODAY'] = today;

  const api = await ApiProcess.start({
    connectionString: databaseConnection(server, 'StoneFlowE2E'),
    launchDate: launchDateFor(today),
    ownerEmail: OWNER_EMAIL,
    appUrl: APP_URL,
  });
  try {
    process.env['STONEFLOW_E2E_OWNER_PASSWORD'] = await setOwnerPassword(api);
  } catch (error) {
    await api.stop();
    throw error;
  }
  return () => api.stop();
}

async function setOwnerPassword(api: ApiProcess): Promise<string> {
  const context = await request.newContext({ baseURL: API_URL });
  try {
    const csrf = await context.get('/api/auth/csrf');
    if (csrf.status() >= 500) {
      throw new Error(`GET /api/auth/csrf: ${csrf.status()}. ${MIGRATE_HINT}`);
    }
    const token = (await context.storageState()).cookies.find((c) => c.name === 'XSRF-TOKEN');
    if (!token) {
      throw new Error('API nie ustawiło ciasteczka XSRF-TOKEN.');
    }
    const headers = { 'X-XSRF-TOKEN': token.value };

    const before = api.outputLength;
    const requested = await context.post('/api/auth/password-reset/request', {
      data: { email: OWNER_EMAIL },
      headers,
    });
    if (requested.status() >= 500) {
      throw new Error(`Prośba o reset hasła: ${requested.status()}. ${MIGRATE_HINT}`);
    }
    if (requested.status() !== 202) {
      throw new Error(`Prośba o reset hasła: oczekiwano 202, jest ${requested.status()}.`);
    }

    const link = await api.waitForResetLink(30_000, before);
    const password = randomBytes(24).toString('base64url');
    const confirmed = await context.post('/api/auth/password-reset/confirm', {
      data: { accountId: link.accountId, token: link.token, newPassword: password },
      headers,
    });
    if (confirmed.status() !== 204) {
      throw new Error(`Ustawienie hasła: oczekiwano 204, jest ${confirmed.status()}.`);
    }
    return password;
  } finally {
    await context.dispose();
  }
}

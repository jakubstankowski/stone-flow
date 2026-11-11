// Adresy stosu E2E. Porty domyślne jak w środowisku deweloperskim (API 5080, aplikacja 4200);
// lokalnie, gdy działa już serwer deweloperski, można je zmienić zmiennymi
// STONEFLOW_E2E_API_PORT i STONEFLOW_E2E_APP_PORT (np. 5180 i 4300).

function port(name: string, fallback: number): number {
  const value = process.env[name];
  if (value === undefined || value === '') {
    return fallback;
  }
  const parsed = Number(value);
  if (!Number.isInteger(parsed) || parsed < 1 || parsed > 65535) {
    throw new Error(`${name} musi być numerem portu (1–65535).`);
  }
  return parsed;
}

export const API_PORT = port('STONEFLOW_E2E_API_PORT', 5080);
export const APP_PORT = port('STONEFLOW_E2E_APP_PORT', 4200);
export const API_URL = `http://localhost:${API_PORT}`;
export const APP_URL = `http://localhost:${APP_PORT}`;

/** Fikcyjny adres właściciela (konto powstaje przy pierwszej prośbie o reset hasła). */
export const OWNER_EMAIL = 'owner@example.com';

/** Stan zalogowanej sesji (ignorowany w git, poza artefaktem CI). */
export const STORAGE_STATE = 'e2e/.auth/owner.json';

/** Hasło właściciela ustawione przez global setup — tylko w pamięci procesów przebiegu. */
export function ownerPassword(): string {
  const password = process.env['STONEFLOW_E2E_OWNER_PASSWORD'];
  if (!password) {
    throw new Error('Brak hasła właściciela — global setup nie został uruchomiony.');
  }
  return password;
}

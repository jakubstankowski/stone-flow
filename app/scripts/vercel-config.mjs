// Konfiguracja Vercel (SF-4).
// - app/vercel.json (w repozytorium) jest bezpieczny dla każdego wdrożenia, także preview:
//   fallback SPA do index.html, bez przepisania /api/* — preview nie ma drogi do API, bazy
//   ani integracji (/api/* zwraca 404).
// - Wariant produkcyjny generuje tylko workflow publikacji: dodaje na początku przepisanie
//   /api/* na origin API (jeden origin dla przeglądarki, ciasteczko pierwszej strony).
//
// Użycie: STONEFLOW_API_ORIGIN=https://<adres-api> node scripts/vercel-config.mjs <plik wyjściowy>
import { readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';

export const baseConfigPath = join(import.meta.dirname, '..', 'vercel.json');

export function readBaseConfig() {
  return JSON.parse(readFileSync(baseConfigPath, 'utf8'));
}

/** Waliduje origin API: tylko https, bez ścieżki, zapytania, fragmentu i danych logowania. */
export function parseApiOrigin(value) {
  if (typeof value !== 'string' || value.trim() === '') {
    throw new Error('Brak STONEFLOW_API_ORIGIN.');
  }
  let url;
  try {
    url = new URL(value.trim());
  } catch {
    throw new Error('STONEFLOW_API_ORIGIN nie jest poprawnym adresem URL.');
  }
  if (url.protocol !== 'https:') {
    throw new Error('STONEFLOW_API_ORIGIN musi używać https://.');
  }
  if (url.username !== '' || url.password !== '') {
    throw new Error('STONEFLOW_API_ORIGIN nie może zawierać danych logowania.');
  }
  if (url.pathname !== '/' || url.search !== '' || url.hash !== '') {
    throw new Error('STONEFLOW_API_ORIGIN może zawierać tylko schemat, host i port.');
  }
  return url.origin;
}

/** Konfiguracja produkcyjna: przepisanie /api/* na API przed fallbackiem SPA. */
export function productionConfig(baseConfig, apiOrigin) {
  const origin = parseApiOrigin(apiOrigin);
  return {
    ...baseConfig,
    rewrites: [
      { source: '/api/:path*', destination: `${origin}/api/:path*` },
      ...(baseConfig.rewrites ?? []),
    ],
  };
}

function main() {
  const output = process.argv[2];
  if (!output) {
    console.error('Użycie: node scripts/vercel-config.mjs <plik wyjściowy>');
    process.exit(1);
  }
  try {
    const config = productionConfig(readBaseConfig(), process.env['STONEFLOW_API_ORIGIN']);
    writeFileSync(output, `${JSON.stringify(config, null, 2)}\n`);
    console.log(`Konfiguracja produkcyjna Vercel zapisana: ${output}`);
  } catch (error) {
    console.error(error instanceof Error ? error.message : String(error));
    process.exit(1);
  }
}

if (import.meta.url === pathToFileURL(process.argv[1] ?? '').href) {
  main();
}

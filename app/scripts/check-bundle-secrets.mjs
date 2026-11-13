// Skan zbudowanego pakietu frontendu (SF-21): nazwy kluczy konfiguracji serwera i wzorce sekretów
// nie mogą trafić do przeglądarki (invariant 6). Uzupełnia gitleaks w CI (`dist`) i przed
// publikacją (`.vercel/output/static`).
//
// Użycie: node scripts/check-bundle-secrets.mjs <katalog>
// Kod 0 — brak trafień; kod 1 — trafienie, brak katalogu albo brak plików tekstowych (skan nie
// przechodzi „na pusto”). Wynik wskazuje plik i regułę, nigdy dopasowany tekst.
import { readFileSync, readdirSync, statSync } from 'node:fs';
import { join, relative, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';

/**
 * Reguły rozróżniają wielkość liter i są zakotwiczone, żeby typowe fragmenty zminifikowanego JS
 * (`e.uid=1`, `n.Password=t`, `type="password"`, `consumerKey`) nie dawały fałszywych trafień.
 * Lista jest jedynym miejscem reguł skanu.
 */
export const RULES = [
  {
    // Klucze z deploy/api.env.example i wdrozenia.md §4, także w postaci zmiennych (`__`).
    id: 'server-config-key',
    pattern:
      /(?<![A-Za-z0-9])(?:ConnectionStrings|WebhookSecret|ConsumerKey|ConsumerSecret)(?![a-z0-9])/,
  },
  {
    id: 'smtp-password-key',
    pattern: /Smtp(?::|__)Password(?![a-z0-9])/,
  },
  {
    // Fragment connection stringu SQL z niepustą wartością, po początku tekstu, `;`, cudzysłowie albo białym znaku.
    id: 'sql-connection-fragment',
    pattern: /(?:^|[;"'\s])(?:Password|User Id|Uid|Initial Catalog)\s*=\s*[^;"'\s]/m,
  },
  {
    // Ta sama postać co reguła woocommerce-rest-key w .gitleaks.toml — zmieniać oba miejsca.
    id: 'woocommerce-rest-key',
    pattern: /\b(?:ck|cs)_[0-9a-f]{40}\b/,
  },
  {
    id: 'private-key-header',
    pattern: /-----BEGIN [A-Z ]*PRIVATE KEY-----/,
  },
  {
    // Nazwy sekretów wdrożeniowych z wdrozenia.md §3.
    id: 'deploy-secret-name',
    pattern: /\b(?:VERCEL_TOKEN|API_FTP_PASSWORD|PRODUCTION_MIGRATIONS_CONNECTION)\b/,
  },
];

const TEXT_FILE = /\.(?:js|mjs|css|html|json|map|txt)$/;

function textFiles(directory) {
  return readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const path = join(directory, entry.name);
    if (entry.isDirectory()) {
      return textFiles(path);
    }
    return TEXT_FILE.test(entry.name) ? [path] : [];
  });
}

/**
 * Skanuje pliki tekstowe katalogu rekurencyjnie.
 * @param {string} directory
 * @returns {{ files: number, findings: { file: string, rule: string }[] }} ścieżki względem katalogu
 * @throws {Error} brak katalogu albo brak plików tekstowych
 */
export function scanDirectory(directory) {
  const root = resolve(directory);
  let isDirectory = false;
  try {
    isDirectory = statSync(root).isDirectory();
  } catch {
    isDirectory = false;
  }
  if (!isDirectory) {
    throw new Error(`Katalog ${directory} nie istnieje.`);
  }
  const files = textFiles(root);
  if (files.length === 0) {
    throw new Error(`Katalog ${directory} nie zawiera plików tekstowych do sprawdzenia.`);
  }
  const findings = files.flatMap((file) => {
    const content = readFileSync(file, 'utf8');
    return RULES.filter((rule) => rule.pattern.test(content)).map((rule) => ({
      file: relative(root, file).split('\\').join('/'),
      rule: rule.id,
    }));
  });
  return { files: files.length, findings };
}

function main(args) {
  if (args.length !== 1) {
    console.error('Użycie: node scripts/check-bundle-secrets.mjs <katalog>');
    return 1;
  }
  let result;
  try {
    result = scanDirectory(args[0]);
  } catch (error) {
    console.error(`Skan pakietu nie przeszedł: ${error.message}`);
    return 1;
  }
  if (result.findings.length > 0) {
    console.error('Wzorce sekretów albo kluczy konfiguracji serwera w pakiecie frontendu:');
    result.findings.forEach(({ file, rule }) => console.error(`  ${file}: ${rule}`));
    return 1;
  }
  console.log(`Brak wzorców sekretów w ${result.files} plikach.`);
  return 0;
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  process.exitCode = main(process.argv.slice(2));
}

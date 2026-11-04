// Kolory są zdefiniowane tylko w src/styles.css (tokeny i motyw daisyUI).
// Kod komponentów nie może zawierać lokalnych kodów kolorów (kryterium odbioru SF-5).
import { readFileSync, readdirSync } from 'node:fs';
import { join, relative } from 'node:path';

const appDir = join(import.meta.dirname, '..', 'src', 'app');
const colorLiteral = /#[0-9a-fA-F]{3,8}\b|\b(?:rgba?|hsla?|oklch|oklab)\(/;

function sourceFiles(directory) {
  return readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const path = join(directory, entry.name);
    if (entry.isDirectory()) {
      return sourceFiles(path);
    }
    return /\.(ts|html|css)$/.test(entry.name) && !entry.name.endsWith('.spec.ts') ? [path] : [];
  });
}

const files = sourceFiles(appDir);
const offenders = files.filter((file) => colorLiteral.test(readFileSync(file, 'utf8')));

if (files.length === 0) {
  console.error('Nie znaleziono plików źródłowych w src/app.');
  process.exit(1);
}
if (offenders.length > 0) {
  console.error('Lokalne kody kolorów (użyj tokenów --sf-* lub klas motywu):');
  offenders.forEach((file) => console.error(`  ${relative(process.cwd(), file)}`));
  process.exit(1);
}
console.log(`Brak lokalnych kodów kolorów w ${files.length} plikach src/app.`);

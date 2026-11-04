// Testy konfiguracji Vercel: node --test scripts/
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, it } from 'node:test';

import { parseApiOrigin, productionConfig, readBaseConfig } from './vercel-config.mjs';

const base = readBaseConfig();
const fallback = base.rewrites.at(-1);
// Wzorzec fallbacku to grupa wyrażenia regularnego w składni path-to-regexp; dopasowanie
// całej ścieżki odpowiada temu wyrażeniu z kotwicami.
const fallbackPattern = new RegExp(`^${fallback.source}$`);

describe('vercel.json (preview i każde wdrożenie poza produkcyjnym)', () => {
  it('kieruje podstrony SPA do index.html (odświeżenie podstrony)', () => {
    assert.equal(fallback.destination, '/index.html');
    for (const path of ['/', '/logowanie', '/reset-hasla', '/zamowienia/1', '/apis', '/a/api']) {
      assert.match(path, fallbackPattern, path);
    }
  });

  it('nie przepisuje /api/* — preview nie ma drogi do API, bazy ani integracji', () => {
    for (const path of ['/api', '/api/', '/api/auth/csrf']) {
      assert.doesNotMatch(path, fallbackPattern, path);
    }
    assert.equal(base.rewrites.length, 1);
    assert.ok(base.rewrites.every((rewrite) => !/^https?:/.test(rewrite.destination)));
  });

  it('wyłącza automatyczne wdrożenia z Git', () => {
    assert.equal(base.git.deploymentEnabled, false);
  });

  it('publikuje katalog wyjściowy budowy Angular', () => {
    const angular = JSON.parse(
      readFileSync(join(import.meta.dirname, '..', 'angular.json'), 'utf8'),
    );
    const projectName = Object.keys(angular.projects)[0];
    assert.equal(base.outputDirectory, `dist/${projectName}/browser`);
  });
});

describe('konfiguracja produkcyjna', () => {
  const production = productionConfig(base, 'https://api.example.com');

  it('przepisuje /api/* na origin API przed fallbackiem SPA', () => {
    assert.deepEqual(production.rewrites[0], {
      source: '/api/:path*',
      destination: 'https://api.example.com/api/:path*',
    });
    assert.deepEqual(production.rewrites.at(-1), fallback);
  });

  it('zachowuje pozostałe ustawienia i nie zmienia konfiguracji bazowej', () => {
    assert.equal(production.git.deploymentEnabled, false);
    assert.equal(production.outputDirectory, base.outputDirectory);
    assert.equal(base.rewrites.length, 1);
  });

  it('normalizuje origin z końcowym ukośnikiem i portem', () => {
    assert.equal(parseApiOrigin('https://api.example.com/'), 'https://api.example.com');
    assert.equal(parseApiOrigin(' https://api.example.com:8443 '), 'https://api.example.com:8443');
  });

  for (const [name, value] of [
    ['brak wartości', undefined],
    ['pusta wartość', ''],
    ['nie URL', 'api.example.com'],
    ['http', 'http://api.example.com'],
    ['ścieżka', 'https://api.example.com/api'],
    ['zapytanie', 'https://api.example.com/?a=1'],
    ['fragment', 'https://api.example.com/#x'],
    ['dane logowania', 'https://user:secret@api.example.com'],
  ]) {
    it(`odrzuca origin: ${name}`, () => {
      assert.throws(
        () => productionConfig(base, value),
        (error) => {
          assert.ok(error instanceof Error);
          assert.doesNotMatch(error.message, /secret/);
          return true;
        },
      );
    });
  }
});

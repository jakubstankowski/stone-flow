// Testy skanu pakietu frontendu: node --test scripts/
// Próbki sekretów są składane w czasie testu, żeby żaden plik repozytorium nie zawierał tekstu
// pasującego do reguł gitleaks.
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, describe, it } from 'node:test';

import { RULES, scanDirectory } from './check-bundle-secrets.mjs';

const script = join(import.meta.dirname, 'check-bundle-secrets.mjs');
const created = [];

after(() => created.forEach((dir) => rmSync(dir, { recursive: true, force: true })));

function bundle(files) {
  const dir = mkdtempSync(join(tmpdir(), 'sf-bundle-'));
  created.push(dir);
  for (const [name, content] of Object.entries(files)) {
    const path = join(dir, name);
    mkdirSync(join(path, '..'), { recursive: true });
    writeFileSync(path, content);
  }
  return dir;
}

function run(dir) {
  return spawnSync(process.execPath, [script, dir], { encoding: 'utf8' });
}

const hex40 = 'a'.repeat(40);
const positives = {
  'server-config-key': [
    'const k="ConnectionStrings:StoneFlow"',
    'StoneFlow__WooCommerce__ConsumerKey',
    'x={WebhookSecret:1}',
    'ConsumerSecret',
  ],
  'smtp-password-key': ['StoneFlow:Smtp:Password', 'StoneFlow__Smtp__Password'],
  'sql-connection-fragment': [
    '"Server=db;Password=abc"',
    'Server=db; User Id=sa',
    'Uid=sa;',
    '"Initial Catalog=StoneFlow"',
  ],
  'woocommerce-rest-key': ['k="ck_' + hex40 + '"', 'cs_' + hex40],
  'private-key-header': [
    '-----BEGIN ' + 'PRIVATE KEY-----',
    '-----BEGIN RSA ' + 'PRIVATE KEY-----',
  ],
  'deploy-secret-name': ['VERCEL_TOKEN', 'x.API_FTP_PASSWORD', 'PRODUCTION_MIGRATIONS_CONNECTION'],
};

// Typowe fragmenty zminifikowanego JS i tekstów UI, które nie są sekretami.
const negatives = [
  'e.uid=1',
  'n.Password=t',
  '{uid:e}',
  '<input type="password">',
  'Hasło',
  'connectionStrings',
  'consumerKey',
  'ConsumerKeys',
  'ck_' + 'a'.repeat(39),
  'CK_' + 'A'.repeat(40),
  'Password=',
  'smtpPassword',
];

describe('check-bundle-secrets', () => {
  it('S-1: każda reguła wykrywa swoją próbkę', () => {
    assert.deepEqual(Object.keys(positives).sort(), RULES.map((rule) => rule.id).sort());
    for (const [rule, samples] of Object.entries(positives)) {
      for (const sample of samples) {
        const { findings } = scanDirectory(bundle({ 'main.js': sample }));
        assert.ok(
          findings.some((finding) => finding.rule === rule),
          `${rule}: próbka nie została wykryta (${sample.length} znaków)`,
        );
      }
    }
  });

  it('S-2: typowe fragmenty pakietu nie są trafieniami', () => {
    for (const sample of negatives) {
      assert.deepEqual(scanDirectory(bundle({ 'main.js': sample })).findings, [], sample);
    }
    const result = run(
      bundle({ 'main.js': negatives.join(';'), 'index.html': '<label>Hasło</label>' }),
    );
    assert.equal(result.status, 0, result.stderr);
    assert.match(result.stdout, /Brak wzorców sekretów w 2 plikach\./);
  });

  it('S-3: brak katalogu albo brak plików tekstowych kończy się błędem', () => {
    const missing = join(tmpdir(), `sf-bundle-missing-${process.pid}`);
    assert.throws(() => scanDirectory(missing), /nie istnieje/);
    assert.equal(run(missing).status, 1);

    const empty = bundle({});
    assert.throws(() => scanDirectory(empty), /nie zawiera plików tekstowych/);
    assert.equal(run(empty).status, 1);

    const binaryOnly = bundle({ 'favicon.ico': 'x' });
    assert.equal(run(binaryOnly).status, 1);
    assert.equal(spawnSync(process.execPath, [script], { encoding: 'utf8' }).status, 1);
  });

  it('S-4: wynik nie zawiera dopasowanego tekstu', () => {
    const secret = 'ck_' + 'b'.repeat(40);
    const result = run(bundle({ 'main.js': `x="${secret}";y="Password=tajne"` }));
    assert.equal(result.status, 1);
    const output = result.stdout + result.stderr;
    assert.ok(!output.includes(secret));
    assert.ok(!output.includes('tajne'));
  });

  it('S-5: wynik wskazuje plik i regułę', () => {
    const result = run(
      bundle({
        'browser/main-ABC.js': 'ok',
        'browser/chunk-1.js': 'a.ConnectionStrings',
        'browser/styles.css': 'body{}',
      }),
    );
    assert.equal(result.status, 1);
    assert.match(result.stderr, /browser\/chunk-1\.js: server-config-key/);
    assert.doesNotMatch(result.stderr, /main-ABC\.js/);
  });
});
